use anyhow::{Context, Result, bail, ensure};
use serde::{Deserialize, Serialize};
use std::{
    collections::{BTreeMap, HashSet},
    fs,
    path::{Component, Path, PathBuf},
};

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Config {
    pub schema_version: u32,
    pub default_program: String,
    #[serde(default)]
    pub appearance: Appearance,
    pub programs: Vec<Program>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default, deny_unknown_fields)]
pub struct Appearance {
    pub animation: bool,
    pub fps: u16,
}
impl Default for Appearance {
    fn default() -> Self {
        Self {
            animation: true,
            fps: 24,
        }
    }
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Program {
    pub id: String,
    pub name: String,
    pub root: PathBuf,
    #[serde(default)]
    pub description: String,
    #[serde(default)]
    pub actions: BTreeMap<String, Action>,
    #[serde(default)]
    pub links: BTreeMap<String, String>,
    pub version: Option<Version>,
    pub deploy: Option<Deploy>,
    pub serve: Option<Serve>,
    #[serde(default)]
    pub cedar_clerk: bool,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Version {
    pub file: PathBuf,
    pub marker: String,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Action {
    pub label: String,
    #[serde(default)]
    pub description: String,
    #[serde(default)]
    pub confirm: bool,
    #[serde(default)]
    pub continue_on_error: bool,
    #[serde(default)]
    pub interactive: bool,
    pub steps: Vec<Step>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(tag = "type", rename_all = "camelCase", deny_unknown_fields)]
pub enum Step {
    Exec {
        name: String,
        command: String,
        #[serde(default)]
        args: Vec<String>,
        #[serde(default = "dot")]
        cwd: PathBuf,
        #[serde(default)]
        env: BTreeMap<String, String>,
    },
    Copy {
        name: String,
        from: PathBuf,
        to: PathBuf,
    },
    Clear {
        name: String,
        path: PathBuf,
    },
}
impl Step {
    pub fn name(&self) -> &str {
        match self {
            Self::Exec { name, .. } | Self::Copy { name, .. } | Self::Clear { name, .. } => name,
        }
    }
}
fn dot() -> PathBuf {
    PathBuf::from(".")
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Deploy {
    pub host: String,
    #[serde(default)]
    pub identity_file: String,
    pub remote_root: String,
    pub service: String,
    pub health_url: String,
    pub artifact_dir: PathBuf,
    pub required_files: Vec<PathBuf>,
    #[serde(default = "master")]
    pub branch: String,
    #[serde(default = "build")]
    pub build_action: String,
    #[serde(default = "health_attempts")]
    pub health_attempts: u32,
    #[serde(default = "health_delay")]
    pub health_delay_seconds: u64,
}
fn master() -> String {
    "master".into()
}
fn build() -> String {
    "build".into()
}
fn health_attempts() -> u32 {
    40
}
fn health_delay() -> u64 {
    3
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Serve {
    pub command: String,
    #[serde(default)]
    pub args: Vec<String>,
    #[serde(default = "dot")]
    pub cwd: PathBuf,
    #[serde(default)]
    pub env: BTreeMap<String, String>,
    pub port: u16,
    pub url: String,
    pub health_url: String,
    #[serde(default = "build")]
    pub build_action: String,
}

pub struct Loaded {
    pub config: Config,
    pub path: PathBuf,
    pub legacy: bool,
}

pub fn user_config() -> PathBuf {
    std::env::var_os("APPDATA")
        .or_else(|| std::env::var_os("XDG_CONFIG_HOME"))
        .map(PathBuf::from)
        .unwrap_or_else(|| {
            PathBuf::from(std::env::var_os("HOME").unwrap_or_default()).join(".config")
        })
        .join("cedar/config.json")
}

pub fn discover(explicit: Option<&Path>, cwd: &Path) -> PathBuf {
    if let Some(path) = explicit {
        return path.to_path_buf();
    }
    for dir in cwd.ancestors() {
        let path = dir.join("cedar.json");
        if path.is_file() {
            return path;
        }
    }
    user_config()
}

pub fn load(path: &Path, cwd: &Path) -> Result<Loaded> {
    let absolute = if path.is_absolute() {
        path.to_path_buf()
    } else {
        cwd.join(path)
    };
    let mut legacy = false;
    let mut config = if absolute.exists() {
        let text = fs::read_to_string(&absolute)
            .with_context(|| format!("Read {}", absolute.display()))?;
        let value: serde_json::Value = serde_json::from_str(text.trim_start_matches('\u{feff}'))
            .with_context(|| {
                format!(
                    "Invalid JSON in {}; the file was not changed",
                    absolute.display()
                )
            })?;
        if value.get("schemaVersion").is_some() {
            serde_json::from_value::<Config>(value)?
        } else if value.get("Host").is_some() || value.get("RepoRoot").is_some() {
            legacy = true;
            let mut c = bundled()?;
            let p = &mut c.programs[0];
            p.root = value["RepoRoot"]
                .as_str()
                .filter(|s| !s.is_empty())
                .map(PathBuf::from)
                .unwrap_or_else(|| cwd.to_path_buf());
            let d = p.deploy.as_mut().unwrap();
            for (key, dest) in [
                ("Host", &mut d.host),
                ("IdentityFile", &mut d.identity_file),
                ("RemoteRoot", &mut d.remote_root),
                ("HealthUrl", &mut d.health_url),
            ] {
                if let Some(s) = value[key].as_str() {
                    *dest = s.into();
                }
            }
            p.links.insert(
                "browser".into(),
                d.health_url.trim_end_matches("/api/health").into(),
            );
            c
        } else {
            bail!("Unknown configuration format in {}", absolute.display());
        }
    } else {
        bail!(
            "Configuration not found: {}. Run `cedar config init --output cedar.json` in your repository.",
            absolute.display()
        );
    };
    config.validate()?;
    let base = absolute.parent().unwrap_or(cwd);
    for program in &mut config.programs {
        if !program.root.is_absolute() {
            program.root = base.join(&program.root);
        }
        program.root = fs::canonicalize(&program.root).with_context(|| {
            format!(
                "Program '{}' root does not exist: {}",
                program.id,
                program.root.display()
            )
        })?;
    }
    Ok(Loaded {
        config,
        path: absolute,
        legacy,
    })
}

pub fn bundled() -> Result<Config> {
    Ok(serde_json::from_str(include_str!("../../cedar.json"))?)
}

pub fn identifier(s: &str) -> bool {
    !s.is_empty()
        && s.len() <= 80
        && s.chars()
            .all(|c| c.is_ascii_alphanumeric() || "-_.".contains(c))
        && s != "."
        && s != ".."
        && !s.starts_with('-')
}

pub fn relative(path: &Path) -> bool {
    !path.as_os_str().is_empty()
        && !path.is_absolute()
        && path
            .components()
            .all(|c| matches!(c, Component::Normal(_) | Component::CurDir))
}

impl Config {
    pub fn validate(&self) -> Result<()> {
        ensure!(
            self.schema_version == 1,
            "Unsupported schemaVersion {}; expected 1",
            self.schema_version
        );
        ensure!(!self.programs.is_empty(), "Configure at least one program");
        ensure!(
            (1..=60).contains(&self.appearance.fps),
            "appearance.fps must be 1..60"
        );
        let mut ids = HashSet::new();
        for p in &self.programs {
            ensure!(
                identifier(&p.id) && ids.insert(&p.id),
                "Invalid or duplicate program id '{}'",
                p.id
            );
            ensure!(!p.name.trim().is_empty(), "Program name cannot be empty");
            for (id, a) in &p.actions {
                ensure!(
                    identifier(id) && !a.steps.is_empty(),
                    "Invalid or empty action '{id}'"
                );
                for s in &a.steps {
                    match s {
                        Step::Exec { command, cwd, .. } => {
                            ensure!(!command.trim().is_empty(), "Empty executable in {id}");
                            ensure!(
                                relative(cwd),
                                "Action working directory must stay within program root"
                            );
                        }
                        Step::Copy { from, to, .. } => ensure!(
                            relative(from) && relative(to),
                            "Copy paths must stay within program root"
                        ),
                        Step::Clear { path, .. } => ensure!(
                            relative(path) && path != Path::new("."),
                            "Clear requires a child output directory"
                        ),
                    }
                }
            }
            if let Some(v) = &p.version {
                ensure!(
                    relative(&v.file) && !v.marker.is_empty(),
                    "Invalid version source"
                );
            }
            if let Some(d) = &p.deploy {
                ensure!(
                    !d.host.starts_with('-')
                        && !d.host.is_empty()
                        && d.host
                            .chars()
                            .all(|c| c.is_ascii_alphanumeric() || "@.-_:[]".contains(c)),
                    "Unsafe SSH host"
                );
                ensure!(identifier(&d.service), "Unsafe systemd service");
                let parts: Vec<_> = d.remote_root.split('/').collect();
                ensure!(
                    d.remote_root.starts_with('/')
                        && parts.len() >= 4
                        && parts[1..].iter().all(|s| identifier(s)),
                    "remoteRoot must be an absolute dedicated application directory, without trailing slash"
                );
                ensure!(
                    relative(&d.artifact_dir) && d.artifact_dir != Path::new("."),
                    "Invalid artifactDir"
                );
                ensure!(
                    !d.required_files.is_empty()
                        && d.required_files
                            .iter()
                            .all(|p| relative(p) && p != Path::new(".")),
                    "Specify relative requiredFiles"
                );
                ensure!(p.version.is_some(), "Deployment requires a version source");
                ensure!(
                    p.actions.contains_key(&d.build_action),
                    "Unknown deploy buildAction"
                );
                ensure!(
                    (1..=120).contains(&d.health_attempts)
                        && (1..=30).contains(&d.health_delay_seconds),
                    "Invalid health retry settings"
                );
                validate_url(&d.health_url)?;
            }
            if let Some(s) = &p.serve {
                ensure!(
                    relative(&s.cwd) && s.port > 0 && !s.command.is_empty(),
                    "Invalid serve settings"
                );
                ensure!(
                    p.actions.contains_key(&s.build_action),
                    "Unknown serve buildAction"
                );
                for address in [&s.url, &s.health_url] {
                    validate_url(address)?;
                    let u = reqwest::Url::parse(address)?;
                    ensure!(
                        matches!(u.host_str(), Some("localhost" | "127.0.0.1" | "[::1]"))
                            && u.port_or_known_default() == Some(s.port),
                        "Serve URLs must use loopback and the configured port"
                    );
                }
            }
            for url in p.links.values() {
                validate_url(url)?;
            }
        }
        ensure!(
            ids.contains(&self.default_program),
            "defaultProgram does not exist"
        );
        Ok(())
    }
    pub fn program(&self, id: Option<&str>) -> Result<&Program> {
        let id = id.unwrap_or(&self.default_program);
        self.programs
            .iter()
            .find(|p| p.id == id)
            .with_context(|| format!("Unknown program '{id}'"))
    }
}

pub fn validate_url(value: &str) -> Result<()> {
    let url = reqwest::Url::parse(value)?;
    ensure!(
        matches!(url.scheme(), "http" | "https")
            && url.host_str().is_some()
            && url.username().is_empty()
            && url.password().is_none(),
        "Expected an HTTP(S) URL without credentials"
    );
    Ok(())
}

impl Program {
    pub fn version(&self) -> Result<String> {
        let source = self
            .version
            .as_ref()
            .context("No version source configured")?;
        let text = fs::read_to_string(self.root.join(&source.file))?;
        let line = text
            .lines()
            .find(|l| l.contains(&source.marker))
            .context("Version marker not found")?;
        let after = line.split_once(&source.marker).unwrap().1;
        let version = after
            .split('"')
            .nth(1)
            .context("Version must be a quoted string after marker")?;
        ensure!(identifier(version), "Unsafe or empty version");
        Ok(version.into())
    }
    pub fn path(&self, relative_path: &Path) -> Result<PathBuf> {
        safe_child(&self.root, relative_path)
    }
}

pub fn safe_child(root: &Path, child: &Path) -> Result<PathBuf> {
    ensure!(
        relative(child),
        "Path must stay within program root: {}",
        child.display()
    );
    let root = fs::canonicalize(root)?;
    let mut path = root.clone();
    for part in child.components() {
        if let Component::Normal(name) = part {
            path.push(name);
            if let Ok(meta) = fs::symlink_metadata(&path) {
                ensure!(
                    !meta.file_type().is_symlink(),
                    "Symlink not allowed in managed path: {}",
                    path.display()
                );
                #[cfg(windows)]
                {
                    use std::os::windows::fs::MetadataExt;
                    ensure!(
                        meta.file_attributes() & 0x400 == 0,
                        "Reparse point not allowed: {}",
                        path.display()
                    );
                }
                ensure!(
                    fs::canonicalize(&path)?.starts_with(&root),
                    "Path escapes program root"
                );
            }
        }
    }
    Ok(path)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn bundled_profile_is_valid() {
        bundled().unwrap().validate().unwrap();
    }
    #[test]
    fn rejects_unsafe_remote_and_duplicate_ids() {
        for root in [
            "/",
            "/home",
            "/home/user/../data",
            "/home/user/app/",
            "/home/user/app';touch /tmp/x",
        ] {
            let mut c = bundled().unwrap();
            c.programs[0].deploy.as_mut().unwrap().remote_root = root.into();
            assert!(c.validate().is_err(), "{root}");
        }
        let mut c = bundled().unwrap();
        c.programs.push(c.programs[0].clone());
        assert!(c.validate().is_err());
    }
    #[test]
    fn rejects_parent_and_root_clear() {
        let temp = tempfile::tempdir().unwrap();
        assert!(safe_child(temp.path(), Path::new("../outside")).is_err());
        assert!(
            safe_child(temp.path(), Path::new("output/new"))
                .unwrap()
                .starts_with(temp.path().canonicalize().unwrap())
        );
    }
    #[test]
    fn legacy_config_loads_without_rewriting() {
        let dir = tempfile::tempdir().unwrap();
        let file = dir.path().join("config.json");
        let text = serde_json::json!({"RepoRoot":dir.path(),"Host":"user@example.org","RemoteRoot":"/home/user/product","HealthUrl":"https://example.org/api/health"}).to_string();
        fs::write(&file, &text).unwrap();
        let loaded = load(&file, dir.path()).unwrap();
        assert!(loaded.legacy);
        assert_eq!(
            loaded.config.programs[0].deploy.as_ref().unwrap().host,
            "user@example.org"
        );
        assert_eq!(fs::read_to_string(file).unwrap(), text);
    }
    #[test]
    fn rejects_malformed_and_unknown_schema() {
        let dir = tempfile::tempdir().unwrap();
        let file = dir.path().join("bad.json");
        fs::write(&file, "{}").unwrap();
        assert!(load(&file, dir.path()).is_err());
        let mut c = bundled().unwrap();
        c.schema_version = 99;
        assert!(c.validate().is_err());
    }
}
