use crate::{
    config::{Program, Step},
    deploy::{self, Options},
    runner::{Runner, copy_tree},
};
use anyhow::{Context, Result, bail, ensure};
use std::{
    collections::BTreeMap,
    fs,
    net::{Ipv4Addr, TcpListener},
    path::Path,
    sync::{atomic::Ordering, mpsc},
    thread,
    time::{Duration, Instant},
};

#[derive(Clone, Debug)]
pub enum Request {
    Action(String),
    Build {
        no_desktop: bool,
        desktop_only: bool,
        installer: bool,
        run: bool,
    },
    Test {
        backend: bool,
        frontend: bool,
        smoke: bool,
        cli: bool,
    },
    Deploy(Options),
    Run {
        no_build: bool,
        no_open: bool,
    },
    Status,
    Watch {
        interval: u64,
    },
    Logs {
        tail: u32,
        errors: bool,
        since: String,
    },
    Db,
    Backup,
    Restart,
    Open(String),
}

pub fn execute(r: &Runner, p: &Program, request: &Request) -> Result<()> {
    match request {
        Request::Action(id) => run_action(r, p, id),
        Request::Build {
            no_desktop,
            desktop_only,
            installer,
            run,
        } => {
            if !desktop_only {
                run_action(r, p, "build")?;
            }
            if !no_desktop && p.cedar_clerk {
                desktop_version(p)?;
                run_action(r, p, "build-desktop")?;
                if *installer {
                    run_action(r, p, "build-installer")?;
                }
                if *run {
                    run_action(r, p, "desktop")?;
                }
            }
            Ok(())
        }
        Request::Test {
            backend,
            frontend,
            smoke,
            cli,
        } => {
            let mut ids = Vec::new();
            if *cli {
                ids.push("test-cli");
            }
            if *backend {
                ids.push("test-backend");
            }
            if *frontend {
                ids.push("test-frontend");
            }
            if ids.is_empty() {
                ids.push("test");
            }
            if *smoke {
                ids.push("smoke");
            }
            let mut failures = Vec::new();
            for id in ids {
                if let Err(e) = run_action(r, p, id) {
                    r.log(format!("FAILED {id}: {e:#}"));
                    failures.push(id);
                }
                r.check()?;
            }
            ensure!(
                failures.is_empty(),
                "Failed suites: {}",
                failures.join(", ")
            );
            Ok(())
        }
        Request::Deploy(options) => deploy::run(r, p, options),
        Request::Run { no_build, no_open } => serve(r, p, *no_build, *no_open),
        Request::Status => status(r, p),
        Request::Watch { interval } => {
            ensure!(
                (1..=3600).contains(interval),
                "Watch interval must be 1..3600 seconds"
            );
            loop {
                status(r, p)?;
                if r.dry_run {
                    return Ok(());
                }
                r.pause(Duration::from_secs(*interval))?;
            }
        }
        Request::Logs {
            tail,
            errors,
            since,
        } => {
            ensure!((1..=2000).contains(tail), "Log tail must be 1..2000");
            let d = p.deploy.as_ref().context("No remote configured")?;
            let result = deploy::remote(
                r,
                p,
                &format!(
                    "journalctl -q -u {} -n {tail} --no-pager --since {} {}",
                    deploy::quote(&d.service),
                    deploy::quote(since),
                    if *errors { "-p warning" } else { "" }
                ),
            )?;
            r.log(result);
            Ok(())
        }
        Request::Db => {
            ensure!(
                p.cedar_clerk,
                "Use a configured action for this program's database"
            );
            let d = p.deploy.as_ref().context("No remote configured")?;
            let db = deploy::quote(&format!("{}/data/cedar.db", d.remote_root));
            let result = deploy::remote(
                r,
                p,
                &format!(
                    "set -e\nsqlite3 -readonly {db} 'PRAGMA quick_check; PRAGMA journal_mode; SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1; SELECT \"Drafts\",count(*) FROM Drafts; SELECT \"AspNetUsers\",count(*) FROM AspNetUsers; SELECT \"Projects\",count(*) FROM Projects;'"
                ),
            )?;
            if !r.dry_run {
                ensure!(
                    result.lines().next() == Some("ok"),
                    "Database quick_check failed: {result}"
                );
            }
            r.log(result);
            Ok(())
        }
        Request::Backup => {
            let d = p.deploy.as_ref().context("No remote configured")?;
            let dir = deploy::quote(&deploy::backups_dir(d));
            let result = deploy::remote(
                r,
                p,
                &format!(
                    "set -e\ntest -d {dir}\nfind {dir} -maxdepth 1 -name {glob} -type f -printf '%T+ %s %f\\n' | sort -r | head -14\ncrontab -l | grep 'backup' || true",
                    glob = deploy::quote(deploy::BACKUP_GLOB)
                ),
            )?;
            if !r.dry_run {
                ensure!(result.contains(".db.gz"), "No local database backups found");
            }
            r.log(result);
            r.log("This verifies the local backup inventory only. Off-site recovery is a separate check.");
            Ok(())
        }
        Request::Restart => {
            let d = p.deploy.as_ref().context("No remote configured")?;
            r.confirm(&format!("Restart {} on {}?", d.service, d.host))?;
            let result = deploy::remote(
                r,
                p,
                &format!(
                    "set -e\nsudo /bin/systemctl restart {}\nsystemctl is-active {}",
                    deploy::quote(&d.service),
                    deploy::quote(&d.service)
                ),
            )?;
            r.log(result);
            Ok(())
        }
        Request::Open(where_) => {
            if where_ == "desktop" {
                return run_action(r, p, "desktop");
            }
            let url = p
                .links
                .get(where_)
                .with_context(|| format!("No '{where_}' link configured"))?;
            open(r, url)
        }
    }
}

pub fn run_action(r: &Runner, p: &Program, id: &str) -> Result<()> {
    let a = p
        .actions
        .get(id)
        .with_context(|| format!("Unknown action '{id}' for {}", p.name))?;
    if a.confirm {
        r.confirm(&format!("Run '{}' for {}?", a.label, p.name))?;
    }
    let mut failures = Vec::new();
    for step in &a.steps {
        r.stage(step.name())?;
        let start = Instant::now();
        let result = if a.interactive {
            match step {
                Step::Exec {
                    command,
                    args,
                    cwd,
                    env,
                    ..
                } => r.interactive(
                    command,
                    &args.iter().map(|s| expand(s, p)).collect::<Vec<_>>(),
                    &p.path(cwd)?,
                    &env.iter().map(|(k, v)| (k.clone(), expand(v, p))).collect(),
                ),
                _ => run_step(r, p, step),
            }
        } else {
            run_step(r, p, step)
        };
        match result {
            Ok(()) => r.log(format!(
                "Completed in {:.1}s",
                start.elapsed().as_secs_f32()
            )),
            Err(e) => {
                r.log(format!("FAILED: {e:#}"));
                if !a.continue_on_error {
                    return Err(e);
                }
                failures.push(step.name());
            }
        }
    }
    ensure!(failures.is_empty(), "Failed: {}", failures.join(", "));
    if p.deploy.as_ref().is_some_and(|d| d.build_action == id) {
        deploy::stamp(r, p)?;
    }
    Ok(())
}

fn expand(value: &str, p: &Program) -> String {
    value.replace("{root}", &p.root.to_string_lossy())
}

fn run_step(r: &Runner, p: &Program, step: &Step) -> Result<()> {
    if r.dry_run {
        match step {
            Step::Exec {
                command, args, cwd, ..
            } => {
                r.exec(command, args, &p.root.join(cwd), &BTreeMap::new())?;
            }
            Step::Copy { from, to, .. } => r.log(format!(
                "[plan] copy {} -> {}",
                from.display(),
                to.display()
            )),
            Step::Clear { path, .. } => {
                r.log(format!("[plan] clear managed output {}", path.display()))
            }
        }
        return Ok(());
    }
    match step {
        Step::Exec {
            command,
            args,
            cwd,
            env,
            ..
        } => {
            let env = env.iter().map(|(k, v)| (k.clone(), expand(v, p))).collect();
            r.exec(
                command,
                &args.iter().map(|s| expand(s, p)).collect::<Vec<_>>(),
                &p.path(cwd)?,
                &env,
            )?;
        }
        Step::Copy { from, to, .. } => {
            let from = p.path(from)?;
            let to = p.path(to)?;
            ensure!(
                from != to && !to.starts_with(&from),
                "Copy destination cannot be inside source"
            );
            validate_tree(&from)?;
            if to.exists() {
                validate_tree(&to)?;
            }
            copy_tree(&from, &to)?;
        }
        Step::Clear { path, .. } => {
            let path = p.path(path)?;
            ensure!(
                path != fs::canonicalize(&p.root)?,
                "Cannot clear program root"
            );
            ensure!(
                !path.components().any(|c| matches!(
                    c.as_os_str().to_str(),
                    Some(".git" | "data" | "Data" | "node_modules")
                )),
                "Refusing to clear source data, git or dependencies"
            );
            if path.exists() {
                validate_tree(&path)?;
                fs::remove_dir_all(&path).with_context(|| {
                    format!(
                        "Cannot clear {}. Stop the process using this output and retry.",
                        path.display()
                    )
                })?;
            }
        }
    }
    Ok(())
}

fn validate_tree(root: &Path) -> Result<()> {
    for entry in fs::read_dir(root)? {
        let entry = entry?;
        let ty = entry.file_type()?;
        ensure!(
            !ty.is_symlink(),
            "Managed directory contains a symlink: {}",
            entry.path().display()
        );
        #[cfg(windows)]
        {
            use std::os::windows::fs::MetadataExt;
            ensure!(
                entry.metadata()?.file_attributes() & 0x400 == 0,
                "Managed directory contains a reparse point"
            );
        }
        if ty.is_dir() {
            validate_tree(&entry.path())?;
        }
    }
    Ok(())
}

pub fn desktop_version(p: &Program) -> Result<()> {
    ensure!(
        p.cedar_clerk,
        "Desktop packaging requires the Cedar Clerk profile"
    );
    let package: serde_json::Value = serde_json::from_str(&fs::read_to_string(
        p.root.join("CedarClerk.Desktop/package.json"),
    )?)?;
    ensure!(
        package["version"].as_str() == Some(&p.version()?),
        "Desktop package.json version differs from CurrentVersion. Update and commit both before building the installer."
    );
    Ok(())
}

fn status(r: &Runner, p: &Program) -> Result<()> {
    if r.dry_run {
        r.log("[plan] GET public health and read SSH service, memory, disk and uptime");
        return Ok(());
    }
    let d = p
        .deploy
        .as_ref()
        .context("No deployment configured; add a status action for this program")?;
    let health = deploy::health(&d.health_url);
    r.log(match &health {
        Ok(v) => serde_json::to_string_pretty(v)?,
        Err(e) => format!("Health unavailable: {e}"),
    });
    let facts = deploy::remote(
        r,
        p,
        &format!(
            "set -e\nprintf 'SERVICE '\nsystemctl is-active {} || true\nuptime\nfree -m\ndf -h {}\n",
            deploy::quote(&d.service),
            deploy::quote(&d.remote_root)
        ),
    )?;
    r.log(facts);
    health.context("Public health check failed")?;
    Ok(())
}

pub fn open(r: &Runner, url: &str) -> Result<()> {
    r.log(format!(
        "{} {url}",
        if r.dry_run { "[plan] open" } else { "Opening" }
    ));
    if !r.dry_run {
        webbrowser::open(url)?;
    }
    Ok(())
}

fn serve(r: &Runner, p: &Program, no_build: bool, no_open: bool) -> Result<()> {
    let s = p.serve.as_ref().context("No local serve configuration")?;
    if r.dry_run {
        r.log(format!("[plan] refuse occupied port {}", s.port));
        if !no_build {
            run_action(r, p, &s.build_action)?;
        }
        r.log(format!(
            "[plan] serve {} with {:?}; poll {}",
            s.command, s.args, s.health_url
        ));
        return Ok(());
    }
    let check_port = || -> Result<()> {
        TcpListener::bind((Ipv4Addr::LOCALHOST, s.port)).with_context(|| {
            format!(
                "Port {} is occupied. Stop its owner; cedar never kills an unrelated server.",
                s.port
            )
        })?;
        Ok(())
    };
    check_port()?;
    if !no_build {
        run_action(r, p, &s.build_action)?;
    }
    check_port()?;
    let mut env: BTreeMap<_, _> = s
        .env
        .iter()
        .map(|(k, v)| (k.clone(), expand(v, p)))
        .collect();
    if p.cedar_clerk {
        env.insert("Cedar__Telegram__BotToken".into(), " ".into());
        env.insert("ASPNETCORE_ENVIRONMENT".into(), "LocalNoBot".into());
        env.insert(
            "CEDAR_DATA_DIR".into(),
            p.root
                .join("CedarClerk.Server/data")
                .to_string_lossy()
                .into(),
        );
        for key in ["Cedar__Urls", "ASPNETCORE_URLS"] {
            env.insert(key.into(), format!("http://127.0.0.1:{}", s.port));
        }
    }
    r.stage("Start local server · Ctrl+C stops the owned process")?;
    let worker = r.clone();
    let cwd = p.path(&s.cwd)?;
    let exe = s.command.clone();
    let args = s.args.iter().map(|a| expand(a, p)).collect::<Vec<_>>();
    let (tx, rx) = mpsc::channel();
    let handle = thread::spawn(move || {
        let result = worker.exec(&exe, &args, &cwd, &env);
        let _ = tx.send(result);
    });
    let started = Instant::now();
    let mut ready = false;
    let result = loop {
        match rx.try_recv() {
            Ok(result) => {
                break result.and_then(|_| {
                    if ready {
                        Ok(())
                    } else {
                        bail!("Server exited before becoming ready")
                    }
                });
            }
            Err(mpsc::TryRecvError::Disconnected) => {
                break Err(anyhow::anyhow!("Server worker disconnected"));
            }
            Err(mpsc::TryRecvError::Empty) => {}
        }
        if r.cancel.load(Ordering::Relaxed) {
            break Ok(());
        }
        if !ready {
            if let Ok(health) = deploy::health(&s.health_url) {
                if p.cedar_clerk
                    && (health["env"] != "LocalNoBot"
                        || !health["externalAuth"]["telegramBot"].is_null())
                {
                    break Err(anyhow::anyhow!(
                        "Local server did not confirm LocalNoBot with a disabled Telegram bot; stopping the owned process"
                    ));
                }
                ready = true;
                if let Err(e) = r.stage("Serving locally · press Ctrl+C to stop") {
                    break Err(e);
                }
                r.log(format!("Ready: {}", s.url));
                if !no_open && let Err(e) = open(r, &s.url) {
                    r.log(format!("Browser could not open: {e}"));
                }
            } else if started.elapsed() > Duration::from_secs(90) {
                break Err(anyhow::anyhow!(
                    "Local server failed to become healthy in 90 seconds"
                ));
            }
        }
        thread::sleep(Duration::from_millis(200));
    };
    r.cancel.store(true, Ordering::Relaxed);
    let _ = handle.join();
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn dry_clear_preserves_directory() {
        let tmp = tempfile::tempdir().unwrap();
        fs::create_dir(tmp.path().join("publish")).unwrap();
        fs::write(tmp.path().join("publish/keep"), "x").unwrap();
        let mut p = crate::config::bundled().unwrap().programs.remove(0);
        p.root = tmp.path().into();
        let r = Runner {
            dry_run: true,
            ..Default::default()
        };
        run_step(
            &r,
            &p,
            &Step::Clear {
                name: "clear".into(),
                path: "publish".into(),
            },
        )
        .unwrap();
        assert!(tmp.path().join("publish/keep").exists());
    }
    #[test]
    fn clear_never_removes_program_root_or_data() {
        let tmp = tempfile::tempdir().unwrap();
        fs::create_dir(tmp.path().join("data")).unwrap();
        let mut p = crate::config::bundled().unwrap().programs.remove(0);
        p.root = tmp.path().into();
        for path in [".", "./", "data"] {
            assert!(
                run_step(
                    &Runner::default(),
                    &p,
                    &Step::Clear {
                        name: "clear".into(),
                        path: path.into()
                    }
                )
                .is_err()
            );
        }
        assert!(tmp.path().join("data").exists());
    }
    #[test]
    fn second_program_action_executes_without_cedar_adapter() {
        let tmp = tempfile::tempdir().unwrap();
        let mut p = crate::config::bundled().unwrap().programs.remove(0);
        p.id = "another".into();
        p.root = tmp.path().into();
        p.deploy = None;
        p.serve = None;
        p.cedar_clerk = false;
        p.actions = BTreeMap::from([(
            "check".into(),
            crate::config::Action {
                label: "Check Rust".into(),
                description: String::new(),
                confirm: false,
                continue_on_error: false,
                interactive: false,
                steps: vec![Step::Exec {
                    name: "version".into(),
                    command: "rustc".into(),
                    args: vec!["--version".into()],
                    cwd: ".".into(),
                    env: BTreeMap::new(),
                }],
            },
        )]);
        run_action(&Runner::default(), &p, "check").unwrap();
    }
}
