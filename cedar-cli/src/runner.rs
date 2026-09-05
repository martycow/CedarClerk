use anyhow::{Context, Result, bail, ensure};
use std::{
    collections::BTreeMap,
    fs,
    io::{self, BufRead, BufReader, Read, Write},
    path::Path,
    process::{Child, Command, Stdio},
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
        mpsc::{self, Sender},
    },
    thread,
    time::{Duration, Instant},
};

#[derive(Debug)]
pub enum Event {
    Line(String),
    Stage(String),
    Progress(u64, u64),
    Confirm(String, Sender<bool>),
    Finished { ok: bool, message: String },
}

#[derive(Clone)]
pub struct Runner {
    pub dry_run: bool,
    pub yes: bool,
    pub cancel: Arc<AtomicBool>,
    pub events: Option<Sender<Event>>,
}

impl Default for Runner {
    fn default() -> Self {
        Self {
            dry_run: false,
            yes: false,
            cancel: Arc::new(AtomicBool::new(false)),
            events: None,
        }
    }
}

impl Runner {
    pub fn check(&self) -> Result<()> {
        ensure!(!self.cancel.load(Ordering::Relaxed), "Cancelled");
        Ok(())
    }
    pub fn log(&self, line: impl AsRef<str>) {
        let line = clean(line.as_ref());
        if let Some(tx) = &self.events {
            let _ = tx.send(Event::Line(line));
        } else {
            println!("{line}");
        }
    }
    pub fn stage(&self, name: &str) -> Result<()> {
        self.check()?;
        if let Some(tx) = &self.events {
            let _ = tx.send(Event::Stage(name.into()));
        } else {
            println!("\n> {name}");
        }
        Ok(())
    }
    pub fn progress(&self, sent: u64, total: u64) {
        if let Some(tx) = &self.events {
            let _ = tx.send(Event::Progress(sent, total));
        }
    }
    pub fn confirm(&self, question: &str) -> Result<()> {
        self.check()?;
        if self.dry_run {
            self.log(format!("[plan] confirmation: {question}"));
            return Ok(());
        }
        if self.yes {
            self.log(format!("Confirmed with --yes: {question}"));
            return Ok(());
        }
        let answer = if let Some(tx) = &self.events {
            let (reply, rx) = mpsc::channel();
            tx.send(Event::Confirm(question.into(), reply))?;
            loop {
                self.check()?;
                match rx.recv_timeout(Duration::from_millis(100)) {
                    Ok(v) => break v,
                    Err(mpsc::RecvTimeoutError::Timeout) => {}
                    Err(_) => bail!("Confirmation closed"),
                }
            }
        } else {
            use std::io::IsTerminal;
            ensure!(
                io::stdin().is_terminal(),
                "Confirmation required: {question}. Use --yes to authorize this operation."
            );
            print!("{question} [y/N] ");
            io::stdout().flush()?;
            let mut answer = String::new();
            io::stdin().read_line(&mut answer)?;
            matches!(answer.trim().to_lowercase().as_str(), "y" | "yes")
        };
        ensure!(answer, "Cancelled by operator");
        Ok(())
    }
    pub fn pause(&self, duration: Duration) -> Result<()> {
        let start = Instant::now();
        while start.elapsed() < duration {
            self.check()?;
            thread::sleep(Duration::from_millis(50));
        }
        Ok(())
    }
    pub fn exec(
        &self,
        executable: &str,
        args: &[String],
        cwd: &Path,
        env: &BTreeMap<String, String>,
    ) -> Result<String> {
        self.process(executable, args, cwd, env, None, true)
    }
    pub fn capture(&self, executable: &str, args: &[String], cwd: &Path) -> Result<String> {
        self.process(executable, args, cwd, &BTreeMap::new(), None, false)
    }
    pub fn interactive(
        &self,
        executable: &str,
        args: &[String],
        cwd: &Path,
        env: &BTreeMap<String, String>,
    ) -> Result<()> {
        use std::io::IsTerminal;
        self.check()?;
        if self.dry_run {
            self.log(format!("[plan] interactive {executable} {args:?}"));
            return Ok(());
        }
        ensure!(
            io::stdin().is_terminal(),
            "Interactive action requires a terminal"
        );
        ensure!(
            self.events.is_none(),
            "Interactive action requires the dashboard to release the terminal"
        );
        let mut child = OwnedChild(
            command(executable, args)
                .current_dir(cwd)
                .envs(env)
                .stdin(Stdio::inherit())
                .stdout(Stdio::inherit())
                .stderr(Stdio::inherit())
                .spawn()
                .with_context(|| format!("Could not start {executable}"))?,
        );
        loop {
            self.check()?;
            if let Some(status) = child.0.try_wait()? {
                ensure!(
                    status.success(),
                    "{executable} exited with {}",
                    status.code().unwrap_or(-1)
                );
                return Ok(());
            }
            thread::sleep(Duration::from_millis(50));
        }
    }
    pub fn script(
        &self,
        executable: &str,
        args: &[String],
        cwd: &Path,
        script: &str,
    ) -> Result<String> {
        self.process(
            executable,
            args,
            cwd,
            &BTreeMap::new(),
            Some(script.as_bytes().to_vec()),
            false,
        )
    }
    fn process(
        &self,
        executable: &str,
        args: &[String],
        cwd: &Path,
        env: &BTreeMap<String, String>,
        input: Option<Vec<u8>>,
        stream: bool,
    ) -> Result<String> {
        self.check()?;
        if self.dry_run {
            self.log(format!(
                "[plan] {} {:?} (in {})",
                executable,
                args,
                cwd.display()
            ));
            if let Some(input) = input {
                self.log(String::from_utf8_lossy(&input));
            }
            return Ok(String::new());
        }
        let mut command = command(executable, args);
        command
            .current_dir(cwd)
            .envs(env)
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .stdin(if input.is_some() {
                Stdio::piped()
            } else {
                Stdio::null()
            });
        let child = command
            .spawn()
            .with_context(|| format!("Could not start {executable} in {}", cwd.display()))?;
        let mut owned = OwnedChild(child);
        let (tx, rx) = mpsc::channel::<(bool, String)>();
        let out_thread = pipe(owned.0.stdout.take().unwrap(), tx.clone(), false);
        let err_thread = pipe(owned.0.stderr.take().unwrap(), tx, true);
        let input_thread = if let Some(input) = input {
            let mut stdin = owned.0.stdin.take().unwrap();
            Some(thread::spawn(move || stdin.write_all(&input)))
        } else {
            None
        };
        let mut output = String::new();
        let mut errors = String::new();
        let status = loop {
            self.check()?;
            for (err, line) in rx.try_iter() {
                if stream {
                    self.log(&line);
                }
                append_bounded(if err { &mut errors } else { &mut output }, &line);
            }
            if let Some(status) = owned.0.try_wait()? {
                break status;
            }
            thread::sleep(Duration::from_millis(35));
        };
        let _ = out_thread.join();
        let _ = err_thread.join();
        for (err, line) in rx.try_iter() {
            if stream {
                self.log(&line);
            }
            append_bounded(if err { &mut errors } else { &mut output }, &line);
        }
        if let Some(t) = input_thread {
            let _ = t.join();
        }
        ensure!(
            status.success(),
            "{} exited with {}\n{}",
            executable,
            status.code().unwrap_or(-1),
            clean(if errors.is_empty() { &output } else { &errors })
        );
        Ok(output)
    }
}

fn append_bounded(text: &mut String, line: &str) {
    text.push_str(line);
    text.push('\n');
    if text.len() > 128 * 1024 {
        let mut cut = text.len() - 96 * 1024;
        while !text.is_char_boundary(cut) {
            cut += 1;
        }
        text.drain(..cut);
    }
}

fn pipe(
    reader: impl Read + Send + 'static,
    tx: Sender<(bool, String)>,
    error: bool,
) -> thread::JoinHandle<()> {
    thread::spawn(move || {
        let mut reader = BufReader::new(reader);
        let mut bytes = Vec::new();
        loop {
            bytes.clear();
            match reader
                .by_ref()
                .take(16 * 1024)
                .read_until(b'\n', &mut bytes)
            {
                Ok(0) | Err(_) => break,
                Ok(_) => {
                    if tx
                        .send((error, String::from_utf8_lossy(&bytes).trim_end().into()))
                        .is_err()
                    {
                        break;
                    }
                }
            }
        }
    })
}

pub fn command(executable: &str, args: &[String]) -> Command {
    #[cfg(windows)]
    if executable.eq_ignore_ascii_case("npm") || executable.eq_ignore_ascii_case("npm.cmd") {
        // npm-cli.js avoids cmd.exe interpreting metacharacters in user-supplied arguments.
        if let Some(path) = find_on_path("npm.cmd") {
            let script = path
                .parent()
                .unwrap()
                .join("node_modules/npm/bin/npm-cli.js");
            if script.is_file() {
                let mut c = Command::new("node");
                c.arg(script).args(args);
                return c;
            }
        }
    }
    let mut c = Command::new(executable);
    c.args(args);
    c
}

#[cfg(windows)]
fn find_on_path(exe: &str) -> Option<std::path::PathBuf> {
    std::env::split_paths(&std::env::var_os("PATH")?)
        .map(|p| p.join(exe))
        .find(|p| p.is_file())
}

pub struct OwnedChild(pub Child);
impl Drop for OwnedChild {
    fn drop(&mut self) {
        if matches!(self.0.try_wait(), Ok(Some(_))) {
            return;
        }
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            let _ = Command::new("taskkill")
                .args(["/PID", &self.0.id().to_string(), "/T", "/F"])
                .creation_flags(0x08000000)
                .stdout(Stdio::null())
                .stderr(Stdio::null())
                .status();
        }
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}

pub fn clean(value: &str) -> String {
    let mut result = String::new();
    let mut escape = false;
    let mut csi = false;
    let mut osc = false;
    for c in value.chars() {
        if osc {
            if c == '\u{7}' {
                osc = false;
            }
            continue;
        }
        if escape {
            escape = false;
            match c {
                '[' => csi = true,
                ']' => osc = true,
                _ => {}
            }
            continue;
        }
        if csi {
            if ('@'..='~').contains(&c) {
                csi = false;
            }
            continue;
        }
        if c == '\u{1b}' {
            escape = true;
            continue;
        }
        if !c.is_control() || c == '\n' || c == '\t' {
            result.push(c);
        }
    }
    result
}

pub fn copy_tree(from: &Path, to: &Path) -> Result<()> {
    ensure!(
        from.is_dir(),
        "Missing source directory: {}",
        from.display()
    );
    fs::create_dir_all(to)?;
    for entry in fs::read_dir(from)? {
        let entry = entry?;
        let ty = entry.file_type()?;
        ensure!(
            !ty.is_symlink(),
            "Refusing to copy symlink {}",
            entry.path().display()
        );
        #[cfg(windows)]
        {
            use std::os::windows::fs::MetadataExt;
            ensure!(
                entry.metadata()?.file_attributes() & 0x400 == 0,
                "Refusing to copy reparse point"
            );
        }
        let dest = to.join(entry.file_name());
        if dest.exists() {
            ensure!(
                !fs::symlink_metadata(&dest)?.file_type().is_symlink(),
                "Destination is a symlink"
            );
        }
        if ty.is_dir() {
            copy_tree(&entry.path(), &dest)?;
        } else if ty.is_file() {
            fs::copy(entry.path(), dest)?;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn dry_run_never_spawns() {
        let r = Runner {
            dry_run: true,
            ..Default::default()
        };
        assert!(
            r.capture("does-not-exist", &[], Path::new("missing"))
                .is_ok()
        );
    }
    #[test]
    fn cancellation_prevents_spawn() {
        let r = Runner::default();
        r.cancel.store(true, Ordering::Relaxed);
        assert!(
            r.capture("does-not-exist", &[], Path::new("."))
                .unwrap_err()
                .to_string()
                .contains("Cancelled")
        );
    }
    #[test]
    fn strips_terminal_control_sequences() {
        assert_eq!(clean("\x1b[31mfail\x1b[0m\r\n"), "fail\n");
        assert_eq!(clean("a\x1b]0;bad\x07b"), "ab");
    }
    #[test]
    fn failures_keep_exit_and_error() {
        let r = Runner::default();
        let result = r.capture(
            "rustc",
            &["--cedar-invalid-argument".into()],
            Path::new("."),
        );
        assert!(result.unwrap_err().to_string().contains("exited with"));
    }

    #[test]
    #[cfg(windows)]
    fn cancellation_stops_the_owned_process() {
        let temp = tempfile::tempdir().unwrap();
        let pid_file = temp.path().join("owned.pid");
        let runner = Runner::default();
        let worker = runner.clone();
        let cwd = temp.path().to_path_buf();
        let handle =
            thread::spawn(move || {
                worker.capture("pwsh", &[
            "-NoProfile".into(), "-Command".into(),
            "[IO.File]::WriteAllText('owned.pid', [string]$PID); Start-Sleep -Seconds 60".into()
        ], &cwd)
            });
        let start = Instant::now();
        while !pid_file.is_file() && start.elapsed() < Duration::from_secs(15) {
            thread::sleep(Duration::from_millis(50));
        }
        runner.cancel.store(true, Ordering::Relaxed);
        let result = handle.join().unwrap();
        assert!(result.unwrap_err().to_string().contains("Cancelled"));
        let pid: u32 = fs::read_to_string(pid_file)
            .expect("child started")
            .parse()
            .unwrap();
        let check = Runner::default().capture(
            "pwsh",
            &[
                "-NoProfile".into(),
                "-Command".into(),
                format!("if (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ exit 1 }}"),
            ],
            temp.path(),
        );
        assert!(check.is_ok(), "owned child survived cancellation");
    }
}
