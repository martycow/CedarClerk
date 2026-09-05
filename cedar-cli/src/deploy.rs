use crate::{
    config::{Deploy, Program},
    operations::run_action,
    runner::{OwnedChild, Runner, command},
};
use anyhow::{Context, Result, bail, ensure};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    collections::BTreeMap,
    fs::{self, File},
    io::{Read, Seek, SeekFrom, Write},
    path::Path,
    process::Stdio,
    sync::{
        Arc,
        atomic::{AtomicU64, Ordering},
    },
    thread,
    time::Duration,
};

#[derive(Clone, Debug, Default)]
pub struct Options {
    pub skip_build: bool,
    pub preflight: bool,
    pub rollback: bool,
    pub force: bool,
    pub desktop: bool,
    pub retries: u32,
}

#[derive(Serialize, Deserialize)]
struct Release {
    version: String,
    commit: String,
    dirty: bool,
    files: BTreeMap<String, String>,
}
const MANIFEST: &str = ".cedar-release.json";

pub fn quote(s: &str) -> String {
    format!("'{}'", s.replace('\'', "'\"'\"'"))
}
pub fn git(r: &Runner, p: &Program, args: &[&str]) -> Result<String> {
    Ok(r.capture(
        "git",
        &args.iter().map(|s| s.to_string()).collect::<Vec<_>>(),
        &p.root,
    )?
    .trim()
    .into())
}
pub fn ssh_args(d: &Deploy) -> Vec<String> {
    let mut args: Vec<String> = [
        "-T",
        "-o",
        "BatchMode=yes",
        "-o",
        "ConnectTimeout=10",
        "-o",
        "ServerAliveInterval=10",
        "-o",
        "ServerAliveCountMax=3",
    ]
    .iter()
    .map(|s| s.to_string())
    .collect();
    if !d.identity_file.is_empty() {
        args.extend(["-i".into(), d.identity_file.clone()]);
    }
    args.push(d.host.clone());
    args
}
pub fn remote(r: &Runner, p: &Program, script: &str) -> Result<String> {
    let d = p
        .deploy
        .as_ref()
        .context("No deployment configured for this program")?;
    let mut args = ssh_args(d);
    args.push("sh -s".into());
    r.script("ssh", &args, &p.root, &script.replace('\r', ""))
}

pub fn health(url: &str) -> Result<serde_json::Value> {
    Ok(reqwest::blocking::Client::builder()
        .timeout(Duration::from_secs(5))
        .build()?
        .get(url)
        .send()?
        .error_for_status()?
        .json()?)
}

pub fn hash(path: &Path) -> Result<String> {
    let mut file = File::open(path)?;
    let mut hasher = Sha256::new();
    let mut buf = [0u8; 64 * 1024];
    loop {
        let n = file.read(&mut buf)?;
        if n == 0 {
            break;
        }
        hasher.update(&buf[..n]);
    }
    Ok(format!("{:x}", hasher.finalize()))
}

fn inventory(root: &Path, dir: &Path, files: &mut BTreeMap<String, String>) -> Result<()> {
    for item in fs::read_dir(dir)? {
        let item = item?;
        let rel = item
            .path()
            .strip_prefix(root)?
            .to_string_lossy()
            .replace('\\', "/");
        if rel == MANIFEST {
            continue;
        }
        ensure!(
            !item.file_type()?.is_symlink(),
            "Artifact contains a symlink: {rel}"
        );
        #[cfg(windows)]
        {
            use std::os::windows::fs::MetadataExt;
            ensure!(
                item.metadata()?.file_attributes() & 0x400 == 0,
                "Artifact contains a reparse point"
            );
        }
        if item.file_type()?.is_dir() {
            inventory(root, &item.path(), files)?;
        } else {
            files.insert(rel, hash(&item.path())?);
        }
    }
    Ok(())
}

pub fn stamp(r: &Runner, p: &Program) -> Result<()> {
    let Some(d) = &p.deploy else {
        return Ok(());
    };
    if r.dry_run {
        r.log("[plan] record artifact version, source commit and file hashes");
        return Ok(());
    }
    let root = p.path(&d.artifact_dir)?;
    for required in &d.required_files {
        ensure!(
            root.join(required).is_file(),
            "Missing artifact: {}",
            required.display()
        );
    }
    let mut files = BTreeMap::new();
    inventory(&root, &root, &mut files)?;
    let release = Release {
        version: p.version()?,
        commit: git(r, p, &["rev-parse", "HEAD"])?,
        dirty: !git(r, p, &["status", "--porcelain"])?.is_empty(),
        files,
    };
    fs::write(root.join(MANIFEST), serde_json::to_vec_pretty(&release)?)?;
    Ok(())
}

pub fn guard(r: &Runner, p: &Program, force: bool) -> Result<()> {
    let d = p.deploy.as_ref().context("No deploy settings")?;
    let branch = git(r, p, &["rev-parse", "--abbrev-ref", "HEAD"])?;
    let dirty = git(r, p, &["status", "--porcelain"])?;
    let reason = if branch != d.branch {
        format!("Branch is '{branch}', expected '{}'", d.branch)
    } else if !dirty.is_empty() {
        "Working tree has uncommitted changes".into()
    } else {
        String::new()
    };
    if !reason.is_empty() {
        ensure!(
            force,
            "Deploy refused: {reason}. Commit the intended release or explicitly use --force."
        );
        r.log(format!("WARNING --force: {reason}"));
    }
    let v = p.version()?;
    if !git(r, p, &["tag", "--points-at", "HEAD"])?
        .lines()
        .any(|s| s == v)
    {
        r.log(format!("WARNING: HEAD has no version tag '{v}'"));
    }
    Ok(())
}

pub fn run(r: &Runner, p: &Program, options: &Options) -> Result<()> {
    let d = p.deploy.as_ref().context("No deployment configured")?;
    ensure!((1..=20).contains(&options.retries), "Retries must be 1..20");
    if r.dry_run {
        for stage in if options.rollback {
            vec![
                "Validate app.prev and its version",
                "Confirm rollback",
                "Recoverable directory swap",
                "Verify restored health and update local LIVE tags",
            ]
        } else {
            vec![
                "Check branch, clean tree, version tag and LIVE",
                "Probe SSH service and disk",
                "Build (unless --skip-build)",
                "Verify artifact provenance and file hashes",
                "Pack SHA-256 addressed archive",
                "Resume upload with prefix verification",
                "Verify checksum, file count and required files",
                "Confirm production swap",
                "Swap with recovery trap",
                "Verify health and release version",
                "Update local LIVE / LIVE-PREV",
            ]
        } {
            r.log(format!("[plan] {stage}"));
            if options.preflight && stage == "Probe SSH service and disk" {
                break;
            }
        }
        if options.desktop {
            r.log("[plan] build desktop and upload checksummed installer; manifest last");
        }
        r.log(format!(
            "[plan] target {}:{} ({})",
            d.host, d.remote_root, d.service
        ));
        return Ok(());
    }
    if options.rollback {
        return rollback(r, p);
    }
    r.stage("Preflight · source and production")?;
    guard(r, p, options.force)?;
    let version = p.version()?;
    if options.desktop {
        crate::operations::desktop_version(p)?;
    }
    let probe = remote(
        r,
        p,
        &format!(
            "set -e\ntest -d {root}\ncommand -v tar\ncommand -v sha256sum\ncommand -v flock\nprintf 'Service: '\nsystemctl is-active {service} || true\ndf -Pk {root}\ntest ! -L {root}\n",
            root = quote(&d.remote_root),
            service = quote(&d.service)
        ),
    )?;
    r.log(probe);
    if let Ok(value) = health(&d.health_url) {
        r.log(format!("Production version: {}", value["version"]));
        if let Ok(live) = git(r, p, &["rev-parse", "--verify", "refs/tags/LIVE^{commit}"]) {
            let v = p.version.as_ref().unwrap();
            let old = git(
                r,
                p,
                &[
                    "show",
                    &format!("{live}:{}", v.file.to_string_lossy().replace('\\', "/")),
                ],
            );
            if old.is_ok_and(|s| {
                !s.lines().any(|l| {
                    l.contains(&v.marker) && l.contains(value["version"].as_str().unwrap_or("?"))
                })
            }) {
                r.log("WARNING: local LIVE does not match the production version");
            }
        }
    } else {
        r.log("WARNING: public health endpoint is not answering");
    }
    if options.preflight {
        return Ok(());
    }
    if !options.skip_build {
        run_action(r, p, &d.build_action)?;
    }
    r.stage("Verify release artifact")?;
    let artifact = p.path(&d.artifact_dir)?;
    let release: Release = serde_json::from_slice(
        &fs::read(artifact.join(MANIFEST))
            .context("Artifact has no provenance. Run cedar build before deploying it.")?,
    )?;
    ensure!(
        release.version == version,
        "Artifact version {} differs from source {version}; rebuild",
        release.version
    );
    ensure!(
        release.commit == git(r, p, &["rev-parse", "HEAD"])?,
        "Artifact belongs to a different commit; rebuild"
    );
    ensure!(
        !release.dirty || options.force,
        "Artifact was built from a dirty tree; rebuild from the committed release"
    );
    let mut actual = BTreeMap::new();
    inventory(&artifact, &artifact, &mut actual)?;
    ensure!(
        actual == release.files,
        "Artifact changed after build; rebuild before deploying"
    );
    for f in &d.required_files {
        ensure!(
            artifact.join(f).is_file(),
            "Missing required artifact {}",
            f.display()
        );
    }
    r.stage("Pack release · old version keeps serving")?;
    let cache = std::env::temp_dir().join("cedar-deploy").join(&p.id);
    fs::create_dir_all(&cache)?;
    let key = format!("{:x}", Sha256::digest(serde_json::to_vec(&release)?));
    let archive = cache.join(format!("{version}-{key}.tar.gz"));
    if !archive.is_file() {
        let temporary = cache.join(format!("{}.{}.tmp", key, std::process::id()));
        r.exec(
            "tar",
            &[
                "-czf".into(),
                temporary.to_string_lossy().into(),
                "-C".into(),
                artifact.to_string_lossy().into(),
                ".".into(),
            ],
            &p.root,
            &BTreeMap::new(),
        )?;
        fs::rename(&temporary, &archive)?;
    }
    let digest = hash(&archive)?;
    let remote_tar = format!("{}/staging/cedar-{version}-{digest}.tar.gz", d.remote_root);
    r.stage("Upload · resumable and checksummed")?;
    upload(r, p, &archive, &remote_tar, options.retries)?;
    let nonce = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)?
        .as_nanos();
    let incoming = format!("{}/incoming-{}-{nonce:x}", d.remote_root, &digest[..20]);
    r.stage("Verify and stage the release")?;
    let verify = verify_script(d, &remote_tar, &incoming, &digest, release.files.len() + 1);
    remote(r, p, &verify)?;
    // Recheck after the potentially long build/upload, before accepting new source state as LIVE.
    guard(r, p, options.force)?;
    ensure!(
        git(r, p, &["rev-parse", "HEAD"])? == release.commit,
        "HEAD changed during deploy; production was not swapped"
    );
    r.confirm(&format!(
        "Deploy {} v{} to {}? The {} service will briefly stop.",
        p.name, version, d.host, d.service
    ))?;
    r.stage("Switch production · recovery armed")?;
    let critical = Runner {
        cancel: Arc::new(std::sync::atomic::AtomicBool::new(false)),
        ..r.clone()
    };
    critical.log("Cancellation is deferred during the directory swap and health verification.");
    remote(&critical, p, &swap_script(d, &incoming, false))?;
    wait_health(&critical, d, Some(&version))?;
    if release.dirty {
        critical.log("WARNING: forced dirty artifact deployed; LIVE tags left unchanged because it matches no commit");
    } else {
        update_tags(&critical, p, &release.commit, false);
    }
    critical.log(format!(
        "Deployed {version}. Previous release is available for rollback."
    ));
    if options.desktop {
        desktop(r, p, &version, options.retries)?;
    }
    Ok(())
}

pub fn verify_script(
    d: &Deploy,
    archive: &str,
    incoming: &str,
    digest: &str,
    count: usize,
) -> String {
    let checks = d
        .required_files
        .iter()
        .map(|f| {
            format!(
                "test -f {}",
                quote(&format!(
                    "{incoming}/{}",
                    f.to_string_lossy().replace('\\', "/")
                ))
            )
        })
        .collect::<Vec<_>>()
        .join("\n");
    format!(
        "set -e\nprintf '%s  %s\\n' {hash} {archive} | sha256sum -c -\ntest ! -L {incoming}\nmkdir -p {incoming}\nexec 9>{incoming}/.unpack.lock\nflock -n 9\nfind {incoming} -mindepth 1 -maxdepth 1 ! -name .unpack.lock -exec rm -rf -- {{}} +\ntar -xzf {archive} -C {incoming}\n{checks}\ntest \"$(find {incoming} -type f ! -name .unpack.lock | wc -l)\" -eq {count}\nrm -f {incoming}/.unpack.lock\nprintf 'Verified {count} files\\n'\n",
        hash = quote(digest),
        archive = quote(archive),
        incoming = quote(incoming)
    )
}

pub fn swap_script(d: &Deploy, incoming: &str, rollback: bool) -> String {
    let root = quote(&d.remote_root);
    let app = quote(&format!("{}/app", d.remote_root));
    let prev = quote(&format!(
        "{}/{}",
        d.remote_root,
        if rollback { "app.broken" } else { "app.prev" }
    ));
    let source = quote(incoming);
    let service = quote(&d.service);
    format!(
        r#"set -e
exec 9>{root}/.deploy.lock
flock -n 9 || {{ echo 'Another deploy is switching production'; exit 1; }}
test -d {source}
test ! -L {source}
test ! -L {app}
test ! -L {prev}
recover() {{
  code=$?
  trap - EXIT HUP INT TERM
  if test "$code" -ne 0; then
    if test ! -d {app} && test -d {prev}; then mv {prev} {app}; fi
    sudo /bin/systemctl start {service} || true
  fi
  exit "$code"
}}
trap recover EXIT
trap 'exit 1' HUP INT TERM
rm -rf -- {prev}
sudo /bin/systemctl stop {service}
if test -d {app}; then mv {app} {prev}; fi
mv {source} {app}
sudo /bin/systemctl start {service}
trap - EXIT HUP INT TERM
echo SWAPPED
"#
    )
}

fn wait_health(r: &Runner, d: &Deploy, version: Option<&str>) -> Result<String> {
    r.stage("Verify public health and version")?;
    let mut last = String::new();
    for attempt in 1..=d.health_attempts {
        r.check()?;
        if let Ok(value) = health(&d.health_url)
            && let Some(v) = value["version"].as_str()
        {
            if version.is_none_or(|expected| expected == v) {
                r.log(format!("Healthy: v{v}"));
                return Ok(v.into());
            }
            last = format!("Endpoint answers {v}, expected {}", version.unwrap());
        }
        r.log(format!(
            "Health attempt {attempt}/{} {last}",
            d.health_attempts
        ));
        r.pause(Duration::from_secs(d.health_delay_seconds))?;
    }
    bail!(
        "Release did not pass health verification. {last}. LIVE was not updated. Inspect `cedar logs --errors` and use `cedar deploy --rollback`."
    )
}

fn rollback(r: &Runner, p: &Program) -> Result<()> {
    let d = p.deploy.as_ref().unwrap();
    let prev = format!("{}/app.prev", d.remote_root);
    r.stage("Validate rollback artifact")?;
    let checks = d
        .required_files
        .iter()
        .map(|f| {
            format!(
                "test -f {}",
                quote(&format!(
                    "{prev}/{}",
                    f.to_string_lossy().replace('\\', "/")
                ))
            )
        })
        .collect::<Vec<_>>()
        .join("\n");
    let metadata = remote(
        r,
        p,
        &format!(
            "set -e\n{checks}\nif test -f {manifest}; then cat {manifest}; fi",
            manifest = quote(&format!("{prev}/{MANIFEST}"))
        ),
    )?;
    let restored = serde_json::from_str::<Release>(&metadata).ok();
    let expected = restored.as_ref().map(|m| m.version.as_str());
    if expected.is_none() {
        r.log("Legacy rollback artifact has no version manifest; public health will be checked, commit attribution will remain unknown.");
    }
    r.confirm(&format!(
        "Restore app.prev on {} and restart {}?",
        d.host, d.service
    ))?;
    let critical = Runner {
        cancel: Arc::new(std::sync::atomic::AtomicBool::new(false)),
        ..r.clone()
    };
    remote(&critical, p, &swap_script(d, &prev, true))?;
    wait_health(&critical, d, expected)?;
    if let Some(release) = restored.filter(|m| !m.dirty) {
        update_tags(&critical, p, &release.commit, true);
    } else {
        let _ = git(&critical, p, &["tag", "-d", "LIVE"]);
        let _ = git(&critical, p, &["tag", "-d", "LIVE-PREV"]);
    }
    Ok(())
}

fn update_tags(r: &Runner, p: &Program, commit: &str, rollback: bool) {
    let result = (|| -> Result<()> {
        ensure!(
            [40, 64].contains(&commit.len()) && commit.bytes().all(|b| b.is_ascii_hexdigit()),
            "Invalid release commit"
        );
        git(r, p, &["cat-file", "-e", &format!("{commit}^{{commit}}")])?;
        let old = git(
            r,
            p,
            &[
                "rev-parse",
                "--verify",
                if rollback {
                    "refs/tags/LIVE-PREV^{commit}"
                } else {
                    "refs/tags/LIVE^{commit}"
                },
            ],
        )
        .ok();
        if rollback {
            git(r, p, &["tag", "-f", "LIVE", commit])?;
            let _ = git(r, p, &["tag", "-d", "LIVE-PREV"]);
        } else {
            if let Some(old) = old {
                git(r, p, &["tag", "-f", "LIVE-PREV", &old])?;
            } else {
                let _ = git(r, p, &["tag", "-d", "LIVE-PREV"]);
            }
            git(r, p, &["tag", "-f", "LIVE", commit])?;
        }
        Ok(())
    })();
    if let Err(error) = result {
        r.log(format!(
            "WARNING: production is healthy, but local LIVE tags could not be updated: {error}"
        ));
    }
}

pub fn upload(r: &Runner, p: &Program, file: &Path, target: &str, retries: u32) -> Result<()> {
    let d = p.deploy.as_ref().unwrap();
    let size = fs::metadata(file)?.len();
    let parent = target
        .rsplit_once('/')
        .context("Remote file needs a parent")?
        .0;
    let q = quote(target);
    remote(
        r,
        p,
        &format!("set -e\nmkdir -p {}\ntest ! -L {q}\n", quote(parent)),
    )?;
    for attempt in 1..=retries {
        r.check()?;
        let offset = remote(
            r,
            p,
            &format!("if test -f {q}; then stat -c %s {q}; else echo 0; fi"),
        )?
        .trim()
        .parse::<u64>()?;
        let mut offset = offset;
        if offset > 0 && offset <= size {
            let mut digest = Sha256::new();
            std::io::copy(&mut File::open(file)?.take(offset), &mut digest)?;
            let prefix = format!("{:x}", digest.finalize());
            let answer = remote(r, p, &format!("head -c {offset} {q} | sha256sum"))?;
            if answer.split_whitespace().next() != Some(&prefix) {
                offset = size + 1;
            }
        }
        if offset > size {
            remote(r, p, &format!(": > {q}"))?;
            offset = 0;
        }
        r.log(format!(
            "Upload attempt {attempt}/{retries}: resume at {offset} of {size} bytes"
        ));
        if offset == size {
            break;
        }
        let mut args = ssh_args(d);
        args.push(format!("sh -c {}", quote(&format!("cat >> {q}"))));
        let mut cmd = command("ssh", &args);
        cmd.stdin(Stdio::piped())
            .stdout(Stdio::null())
            .stderr(Stdio::null());
        let mut child = OwnedChild(cmd.spawn()?);
        let mut stdin = child.0.stdin.take().unwrap();
        let mut local = File::open(file)?;
        local.seek(SeekFrom::Start(offset))?;
        let sent = Arc::new(AtomicU64::new(offset));
        let counter = sent.clone();
        let writer = thread::spawn(move || -> Result<()> {
            let mut buf = [0; 64 * 1024];
            loop {
                let n = local.read(&mut buf)?;
                if n == 0 {
                    break;
                }
                stdin.write_all(&buf[..n])?;
                counter.fetch_add(n as u64, Ordering::Relaxed);
            }
            Ok(())
        });
        let ok = loop {
            r.check()?;
            r.progress(sent.load(Ordering::Relaxed), size);
            if let Some(status) = child.0.try_wait()? {
                break status.success();
            }
            r.pause(Duration::from_millis(80))?;
        };
        let written = writer
            .join()
            .map_err(|_| anyhow::anyhow!("Upload writer stopped"))?;
        if ok && written.is_ok() {
            break;
        }
        if attempt == retries {
            bail!("Upload interrupted. Production was not stopped; retry with --skip-build.");
        }
        r.pause(Duration::from_secs(2))?;
    }
    let expected = hash(file)?;
    let actual = remote(r, p, &format!("sha256sum {q}"))?;
    ensure!(
        actual.split_whitespace().next() == Some(&expected),
        "Remote checksum differs; production was not stopped"
    );
    r.progress(size, size);
    Ok(())
}

fn desktop(r: &Runner, p: &Program, version: &str, retries: u32) -> Result<()> {
    run_action(r, p, "build-desktop")?;
    run_action(r, p, "build-installer")?;
    let d = p.deploy.as_ref().unwrap();
    let downloads = format!("{}/data/downloads", d.remote_root);
    let stage = format!("{downloads}/.staging");
    let name = format!("CedarClerk-Setup-{version}.exe");
    r.stage("Publish desktop · manifest last")?;
    for name in [
        &name,
        &format!("{name}.blockmap"),
        &"latest.yml".to_string(),
    ] {
        upload(
            r,
            p,
            &p.root.join("CedarClerk.Desktop/dist").join(name),
            &format!("{stage}/{name}"),
            retries,
        )?;
    }
    remote(
        r,
        p,
        &format!(
            "set -e\nmv -f {exe} {dest}\nmv -f {block} {dest}\nmv -f {manifest} {dest}/latest.yml\n",
            exe = quote(&format!("{stage}/{name}")),
            block = quote(&format!("{stage}/{name}.blockmap")),
            manifest = quote(&format!("{stage}/latest.yml")),
            dest = quote(&downloads)
        ),
    )?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn swap_has_lock_recovery_and_correct_order() {
        let c = crate::config::bundled().unwrap();
        let d = c.programs[0].deploy.as_ref().unwrap();
        let script = swap_script(d, "/home/user/app/incoming", false);
        assert!(script.find("flock -n").unwrap() < script.find("systemctl stop").unwrap());
        assert!(script.contains("trap recover EXIT"));
        assert!(
            script.find("systemctl stop").unwrap()
                < script.find("mv '/home/user/app/incoming'").unwrap()
        );
    }
    #[test]
    fn shell_values_are_quoted() {
        assert_eq!(quote("a'b"), "'a'\"'\"'b'");
    }
    #[test]
    fn dry_deploy_does_not_require_artifacts_or_start_tools() {
        let c = crate::config::bundled().unwrap();
        let p = &c.programs[0];
        let r = Runner {
            dry_run: true,
            ..Default::default()
        };
        run(
            &r,
            p,
            &Options {
                retries: 5,
                ..Default::default()
            },
        )
        .unwrap();
    }
    #[test]
    fn checksum_is_content_based() {
        let t = tempfile::tempdir().unwrap();
        let p = t.path().join("x");
        fs::write(&p, "abc").unwrap();
        assert_eq!(
            hash(&p).unwrap(),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        );
    }
}
