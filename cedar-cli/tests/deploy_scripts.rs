use cedar_cli::{
    config,
    deploy::{quote, swap_script, verify_script},
};
use std::{
    fs,
    path::{Path, PathBuf},
    process::{Command, Output},
};

fn bash() -> PathBuf {
    if cfg!(windows) {
        PathBuf::from("C:/Program Files/Git/bin/bash.exe")
    } else {
        PathBuf::from("/bin/bash")
    }
}
fn shell(dir: &Path, script: &str) -> Output {
    Command::new(bash())
        .args(["--noprofile", "--norc", "-c", script])
        .current_dir(dir)
        .output()
        .expect("Bash is required for deployment script integration tests")
}
fn fixture() -> (tempfile::TempDir, config::Deploy) {
    let dir = tempfile::tempdir().unwrap();
    let output = shell(dir.path(), "pwd -P");
    assert!(output.status.success());
    let root = String::from_utf8(output.stdout).unwrap().trim().to_string();
    let mut d = config::bundled()
        .unwrap()
        .programs
        .remove(0)
        .deploy
        .unwrap();
    d.remote_root = root;
    d.required_files = vec!["server.bin".into(), "wwwroot/index.html".into()];
    (dir, d)
}
fn release(root: &Path, name: &str, version: &str) {
    fs::create_dir_all(root.join(name).join("wwwroot")).unwrap();
    fs::write(root.join(name).join("server.bin"), version).unwrap();
    fs::write(root.join(name).join("wwwroot/index.html"), version).unwrap();
}
fn mock_service(script: &str) -> String {
    // Test the real directory operations while preventing any system service access.
    format!(
        "sudo() {{ printf '%s\\n' \"$*\" >> service.calls; }}\nflock() {{ return 0; }}\n{script}"
    )
}

#[test]
fn successful_swap_keeps_old_release_and_starts_service() {
    let (tmp, d) = fixture();
    release(tmp.path(), "app", "old");
    release(tmp.path(), "incoming", "new");
    let script = swap_script(&d, &format!("{}/incoming", d.remote_root), false);
    let out = shell(tmp.path(), &mock_service(&script));
    assert!(
        out.status.success(),
        "{}",
        String::from_utf8_lossy(&out.stderr)
    );
    assert_eq!(
        fs::read_to_string(tmp.path().join("app/server.bin")).unwrap(),
        "new"
    );
    assert_eq!(
        fs::read_to_string(tmp.path().join("app.prev/server.bin")).unwrap(),
        "old"
    );
    assert!(
        fs::read_to_string(tmp.path().join("service.calls"))
            .unwrap()
            .contains("start cedarclerk")
    );
}

#[test]
fn failed_rename_restores_old_directory_and_restarts_service() {
    let (tmp, d) = fixture();
    release(tmp.path(), "app", "old");
    release(tmp.path(), "incoming", "new");
    let incoming = format!("{}/incoming", d.remote_root);
    let script = swap_script(&d, &incoming, false).replace(
        &format!(
            "mv {} {}",
            quote(&incoming),
            quote(&format!("{}/app", d.remote_root))
        ),
        "false",
    );
    let out = shell(tmp.path(), &mock_service(&script));
    assert!(!out.status.success());
    assert_eq!(
        fs::read_to_string(tmp.path().join("app/server.bin")).unwrap(),
        "old"
    );
    assert!(
        fs::read_to_string(tmp.path().join("service.calls"))
            .unwrap()
            .contains("start cedarclerk")
    );
}

#[test]
fn rollback_restores_previous_and_keeps_failed_release() {
    let (tmp, d) = fixture();
    release(tmp.path(), "app", "broken");
    release(tmp.path(), "app.prev", "good");
    let out = shell(
        tmp.path(),
        &mock_service(&swap_script(
            &d,
            &format!("{}/app.prev", d.remote_root),
            true,
        )),
    );
    assert!(
        out.status.success(),
        "{}",
        String::from_utf8_lossy(&out.stderr)
    );
    assert_eq!(
        fs::read_to_string(tmp.path().join("app/server.bin")).unwrap(),
        "good"
    );
    assert_eq!(
        fs::read_to_string(tmp.path().join("app.broken/server.bin")).unwrap(),
        "broken"
    );
}

#[test]
fn missing_incoming_never_stops_service() {
    let (tmp, d) = fixture();
    release(tmp.path(), "app", "old");
    let out = shell(
        tmp.path(),
        &mock_service(&swap_script(
            &d,
            &format!("{}/missing", d.remote_root),
            false,
        )),
    );
    assert!(!out.status.success());
    assert!(!tmp.path().join("service.calls").exists());
    assert_eq!(
        fs::read_to_string(tmp.path().join("app/server.bin")).unwrap(),
        "old"
    );
}

#[test]
fn corrupt_archive_never_changes_production_or_stages_files() {
    let (tmp, d) = fixture();
    release(tmp.path(), "app", "old");
    fs::write(tmp.path().join("bad.tar.gz"), "corrupt").unwrap();
    let script = verify_script(
        &d,
        &format!("{}/bad.tar.gz", d.remote_root),
        &format!("{}/incoming", d.remote_root),
        &"0".repeat(64),
        2,
    );
    let out = shell(tmp.path(), &mock_service(&script));
    assert!(!out.status.success());
    assert!(!tmp.path().join("incoming").exists());
    assert_eq!(
        fs::read_to_string(tmp.path().join("app/server.bin")).unwrap(),
        "old"
    );
}

#[test]
fn real_archive_must_match_file_count_and_required_files() {
    let (tmp, d) = fixture();
    release(tmp.path(), "build", "new");
    assert!(
        shell(tmp.path(), "tar -czf release.tar.gz -C build .")
            .status
            .success()
    );
    let digest = cedar_cli::deploy::hash(&tmp.path().join("release.tar.gz")).unwrap();
    let archive = format!("{}/release.tar.gz", d.remote_root);
    let incoming = format!("{}/incoming", d.remote_root);
    let out = shell(
        tmp.path(),
        &mock_service(&verify_script(&d, &archive, &incoming, &digest, 2)),
    );
    assert!(
        out.status.success(),
        "{}",
        String::from_utf8_lossy(&out.stderr)
    );
    let bad = shell(
        tmp.path(),
        &mock_service(&verify_script(&d, &archive, &incoming, &digest, 3)),
    );
    assert!(!bad.status.success());
}
