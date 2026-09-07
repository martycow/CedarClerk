# Cedar operations console

`cedar` is MooTool's native Rust operations executable with a Ratatui dashboard (ADR-291).
Its source, installer and Rust tests live in MooTool's `modules/cedar` crate.
Cedar Clerk owns the active `cedar.json` profile.

## Install and start

Source installation requires PowerShell 7, Rust stable, Visual Studio C++ Build Tools, the Windows SDK x64 component, and Git for Windows.
The SDK must include `kernel32.lib`, UCRT libraries and headers.
From the MooTool root, run `./modules/cedar/Scripts/install-cli.ps1 -Repo <CedarClerk-checkout>`.
The installer runs Rust tests and builds the release executable before installation.
It reuses the existing `cedar.exe` directory, or selects `%LOCALAPPDATA%/MooTool/bin` for a fresh installation.
It preserves configuration and keeps the previous executable as `cedar.previous.<id>.exe`.
The installation directory comes first in the user PATH. Open a new terminal after installation.

Cedar Clerk actions require Node/npm, a .NET SDK and the .NET 8 + ASP.NET Core 8 runtimes.
The console executable itself needs no .NET runtime or source checkout.

| Command | Purpose |
|---|---|
| `cedar` | Open the animated dashboard |
| `cedar --no-animation` | Open a still dashboard; `A` also toggles animation |
| `cedar test` | Run backend, frontend, icon, contrast and density tests, then the Angular production build |
| `cedar test --backend` | Run backend tests |
| `cedar test --frontend` | Run frontend tests, design contracts and the Angular production build |
| `cedar test --cli` | Validate the active profile with the installed executable |
| `cedar test --smoke` | Add the isolated Playwright suite |
| `cedar build --no-desktop` | Build the web and server deployment artifact |
| `cedar build --installer` | Also build the desktop shell and installer |
| `cedar run` | Build and serve locally with the Telegram bot disabled |
| `cedar run --no-build --no-open` | Reuse the artifact without opening a browser |
| `cedar deploy --preflight` | Check source, remote tools, service, disk, backup age and health |
| `cedar deploy` | Build, stage, verify and ask before switching production |
| `cedar deploy --skip-build` | Resume a release from the verified existing artifact |
| `cedar deploy --rollback` | Verify and restore the previous release |
| `cedar deploy --desktop` | Publish the installer after the site passes health verification |
| `cedar deploy --dry-run` | Print the plan without processes, network requests or writes |
| `cedar status` / `cedar logs --errors` | Inspect production health or bounded service errors |
| `cedar db` / `cedar backup verify` | Read SQLite health or local backup inventory |
| `cedar config validate` / `cedar config path` | Validate or locate the active JSON |

The dashboard uses arrows or J/K to select, Enter to execute, Tab or Left/Right to switch programs, `/` to filter actions, `R` to reload JSON, and Q to exit. During a job, PageUp/PageDown scroll output and C requests cancellation. Enter returns after completion. Production confirmations default to Cancel; Y confirms.

## Configuration

Resolution order: `--config <path>`, the nearest ancestor `cedar.json`, then `%APPDATA%/cedar/config.json` on Windows (`$XDG_CONFIG_HOME/cedar/config.json` or `~/.config/cedar/config.json` elsewhere). Relative program roots resolve against the JSON file's directory. Command working directories and copy/clear paths resolve within the selected program root. `{root}` expands in argument and environment values.

`cedar config init --output cedar.local.json` creates a configuration without overwriting existing files. `cedar --config cedar.local.json` selects it. Keep personal overrides outside tracked files when preparing a clean release. Authentication remains in OpenSSH configuration or the SSH agent; store no passwords or tokens in this JSON.

The previous `Host`/`RepoRoot` configuration loads as a single Cedar Clerk profile in memory. `cedar --config <old-file> config migrate --output <new-file>` saves a separate versioned file and preserves the original. Invalid configuration reports an error without silently reverting to another host.

Example configuration for another program:

```json
{
  "schemaVersion": 1,
  "defaultProgram": "my-tool",
  "appearance": { "animation": true, "fps": 24 },
  "programs": [
    {
      "id": "my-tool",
      "name": "My Tool",
      "description": "Build and check my Rust application",
      "root": "../my-tool",
      "actions": {
        "build": {
          "label": "Build release",
          "steps": [
            { "type": "exec", "name": "Cargo release", "command": "cargo", "args": ["build", "--release"] }
          ]
        },
        "test": {
          "label": "Run tests",
          "steps": [
            { "type": "exec", "name": "Cargo tests", "command": "cargo", "args": ["test"] }
          ]
        }
      }
    }
  ]
}
```

Add this program object to the existing `programs` array to manage both applications. Select it with `cedar --program my-tool`, or switch inside the dashboard. `cedar action <name>` runs any named action. An action can set `confirm: true` and `continueOnError: true`; the latter runs all its steps and still returns failure if any step failed.

Set `interactive: true` on an action that needs terminal input, such as Claude remote control. The dashboard releases the terminal and resumes when the command exits. Interactive actions require a terminal; ordinary actions stream output into the job panel.

The three step types are `exec` (executable, argument array, optional `cwd` and `env`), `copy` (`from` and `to` directories), and `clear` (a managed child output directory). Commands do not implicitly run through a shell. A shell script must name its interpreter explicitly. Configuration is executable operational policy: inspect commands before using a configuration obtained from someone else.

An optional `serve` object defines command, arguments, environment, working directory, loopback URLs, port and build action. An optional `deploy` object defines SSH host, identity-file path, dedicated remote root, systemd service, health URL, artifact directory, required files, branch and build action. Deployable programs also define a version source: a relative file and a marker followed by a quoted version string. The health endpoint must return JSON with a string `version` field.

## Deployment behavior

The source must be on the configured release branch with a clean working tree. A missing version tag is a warning. `--force` explicitly overrides branch/dirty checks; it does not bypass artifact hashes or health verification. A dirty artifact cannot establish a truthful LIVE commit. The preflight also reads the age of the newest `cedar-*.db.gz` under the remote `data/backups` from the server's own clock: missing or older than 36 hours is a warning naming `cedar backup verify` and the 03:30 UTC nightly, never a stop.

Build records the version, source commit and file hashes in `publish/.cedar-release.json`. A different commit, missing manifest or changed artifact requires a rebuild. Uploads use content-addressed archive names and validate a partial remote prefix before resuming. Staging checks SHA-256, required files and file count while production keeps serving.

The directory switch takes a remote lock, stops the service, preserves `app.prev`, swaps in the staged directory and restarts. A shell recovery handler tries to restore the previous directory and start the service if a rename fails. Health must report the intended version before local LIVE tags move. A health failure stays visible and gives the rollback command; it does not silently declare success. Cancellation waits for a production switch and its health verification to finish.

The deployment account needs OpenSSH, `tar`, `sha256sum`, `flock`, common POSIX tools, and permission to start/stop/restart the configured systemd service. Data stays outside `app`. SQLite and backup inspection are read-only. Desktop downloads publish their checksummed executable and blockmap before `latest.yml`.

## Verify a change

1. For console changes, run `./modules/cedar/Scripts/rust-cli.ps1 test --locked` and `./modules/cedar/Scripts/rust-cli.ps1 clippy --locked --all-targets` from MooTool. Deployment tests use disposable local directories with mocked service control.
2. Run `cedar test` and `cedar build --no-desktop` against the working branch.
3. Open the dashboard, select/filter actions, run a harmless action and return. Cancel a long local command and verify its owned child exits.
4. Use `cedar run --no-open`, verify local health and the disabled bot, then stop it.
5. Inspect title frames at 120×42 and 80×30. `cedar preview --output <file.html> --width 120 --height 42` exports the actual Ratatui buffer for visual inspection. It is a frame export, not a screenshot of Windows Terminal.
6. A real production deploy is a separate operator action. Local transport/script tests do not establish live SSH, production rollback or installer-download acceptance.
