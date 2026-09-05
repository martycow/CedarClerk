use anyhow::{Context, Result, bail, ensure};
use cedar_cli::{
    config, deploy,
    operations::{self, Request},
    runner::Runner,
    ui,
};
use clap::{Parser, Subcommand};
use std::{fs, io::IsTerminal, path::PathBuf, sync::atomic::Ordering};

#[derive(Parser)]
#[command(
    name = "cedar",
    version,
    about = "Cedar operations console — deploy, test and run your programs"
)]
struct Cli {
    #[arg(long, global = true)]
    config: Option<PathBuf>,
    #[arg(short, long, global = true)]
    program: Option<String>,
    #[arg(long, global = true)]
    repo: Option<PathBuf>,
    #[arg(long, global = true)]
    host: Option<String>,
    #[arg(long, global = true)]
    dry_run: bool,
    #[arg(short = 'y', long, global = true)]
    yes: bool,
    #[arg(long, alias = "no-logo", global = true)]
    no_animation: bool,
    #[command(subcommand)]
    command: Option<Commands>,
}

#[derive(Subcommand)]
enum Commands {
    /// Interactive animated operations dashboard.
    Menu,
    /// Named programs in the JSON configuration.
    Programs,
    /// Available command actions for the selected program.
    Actions,
    /// Run an action defined in the program's JSON configuration.
    Action { name: String },
    /// Build web, server and optionally desktop artifacts.
    Build {
        #[arg(long, conflicts_with = "desktop_only")]
        no_desktop: bool,
        #[arg(long)]
        desktop_only: bool,
        #[arg(long, conflicts_with = "no_desktop")]
        installer: bool,
        #[arg(long, conflicts_with = "no_desktop")]
        run: bool,
    },
    /// Run local tests and report each failing suite.
    Test {
        #[arg(long)]
        backend: bool,
        #[arg(long)]
        frontend: bool,
        #[arg(long)]
        smoke: bool,
        #[arg(long)]
        cli: bool,
    },
    /// Stage a checksummed release and confirm before switching production.
    Deploy {
        #[arg(long, conflicts_with = "rollback")]
        skip_build: bool,
        #[arg(long, conflicts_with = "rollback")]
        preflight: bool,
        #[arg(long)]
        rollback: bool,
        #[arg(long)]
        force: bool,
        #[arg(long, conflicts_with = "rollback")]
        desktop: bool,
        #[arg(long, default_value_t = 5)]
        retries: u32,
    },
    /// Serve the built artifact locally; Ctrl+C stops the owned server.
    Run {
        #[arg(long)]
        no_build: bool,
        #[arg(long)]
        no_open: bool,
    },
    /// Read public health and remote resource usage.
    Status,
    /// Refresh remote status until cancelled.
    Watch {
        #[arg(long, default_value_t = 5)]
        interval: u64,
    },
    /// Read a bounded service journal.
    Logs {
        #[arg(long, default_value_t = 80)]
        tail: u32,
        #[arg(long)]
        errors: bool,
        #[arg(long, default_value = "-24h")]
        since: String,
    },
    /// Read-only SQLite quick_check and core row counts.
    Db,
    /// Verify local database backup inventory.
    Backup {
        #[arg(default_value = "verify")]
        operation: String,
    },
    /// Confirm and restart the selected remote service.
    Restart,
    /// Open a configured link, or the desktop action.
    Open {
        #[arg(default_value = "browser")]
        destination: String,
    },
    /// Start the configured Claude action.
    Claude,
    /// Inspect, initialize or migrate JSON configuration.
    Config {
        #[command(subcommand)]
        operation: Option<ConfigCommand>,
    },
    /// Render an exact Ratatui frame as a standalone HTML preview.
    Preview {
        #[arg(long)]
        output: PathBuf,
        #[arg(long, default_value_t = 120)]
        width: u16,
        #[arg(long, default_value_t = 42)]
        height: u16,
        #[arg(long, default_value_t = 4.0)]
        time: f64,
    },
}
#[derive(Subcommand)]
enum ConfigCommand {
    Path,
    Validate,
    Init {
        #[arg(long, default_value = "cedar.json")]
        output: PathBuf,
    },
    Migrate {
        #[arg(long)]
        output: PathBuf,
    },
}

fn main() {
    if let Err(error) = run() {
        eprintln!("cedar: {error:#}");
        std::process::exit(1);
    }
}

fn run() -> Result<()> {
    let cli = Cli::parse();
    let cwd = std::env::current_dir()?;
    let path = config::discover(cli.config.as_deref(), &cwd);
    if let Some(Commands::Config {
        operation: Some(ConfigCommand::Path),
    }) = &cli.command
    {
        println!("{}", path.display());
        return Ok(());
    }
    if let Some(Commands::Config {
        operation: Some(ConfigCommand::Init { output }),
    }) = &cli.command
    {
        ensure!(
            !output.exists(),
            "Refusing to overwrite {}",
            output.display()
        );
        if cli.dry_run {
            println!("[plan] initialize {}", output.display());
            return Ok(());
        }
        let mut config = config::bundled()?;
        config.programs[0].root = cli.repo.clone().unwrap_or(cwd);
        if let Some(host) = cli.host {
            config.programs[0].deploy.as_mut().unwrap().host = host;
        }
        config.validate()?;
        write_new(output, &serde_json::to_vec_pretty(&config)?)?;
        println!("Created {}", output.display());
        return Ok(());
    }
    let mut loaded = config::load(&path, &cwd)?;
    let selected = cli
        .program
        .as_deref()
        .unwrap_or(&loaded.config.default_program)
        .to_owned();
    for p in &mut loaded.config.programs {
        if p.id != selected {
            continue;
        }
        if let Some(root) = &cli.repo {
            p.root = fs::canonicalize(root)?;
        }
        if let Some(host) = &cli.host {
            p.deploy.as_mut().context("No deploy configuration")?.host = host.clone();
        }
    }
    loaded.config.validate()?;
    let program = loaded.config.program(Some(&selected))?.clone();
    let runner = Runner {
        dry_run: cli.dry_run,
        yes: cli.yes,
        ..Default::default()
    };
    let cancelled = runner.cancel.clone();
    ctrlc::set_handler(move || {
        cancelled.store(true, Ordering::Relaxed);
    })?;
    let request = match cli.command {
        None | Some(Commands::Menu) => {
            if cli.dry_run {
                println!("[plan] open terminal dashboard for {}", program.name);
                return Ok(());
            }
            ensure!(
                std::io::stdin().is_terminal() && std::io::stdout().is_terminal(),
                "Interactive menu needs a terminal. Use `cedar --help` for direct commands."
            );
            return ui::run(loaded, &selected, cli.no_animation, runner);
        }
        Some(Commands::Preview {
            output,
            width,
            height,
            time,
        }) => {
            ensure!(
                (30..=240).contains(&width)
                    && (12..=100).contains(&height)
                    && time.is_finite()
                    && time >= 0.0,
                "Invalid preview dimensions or time"
            );
            if cli.dry_run {
                println!("[plan] render preview to {}", output.display());
                return Ok(());
            }
            ui::preview(&loaded.config, &selected, width, height, time, &output)?;
            println!("{}", output.display());
            return Ok(());
        }
        Some(Commands::Programs) => {
            for p in &loaded.config.programs {
                println!("{:<18} {:<24} {}", p.id, p.name, p.root.display());
            }
            return Ok(());
        }
        Some(Commands::Actions) => {
            for (id, a) in &program.actions {
                println!("{:<20} {}", id, a.label);
            }
            return Ok(());
        }
        Some(Commands::Config { operation }) => {
            match operation {
                Some(ConfigCommand::Validate) => println!(
                    "Valid schema v1: {} programs in {}",
                    loaded.config.programs.len(),
                    loaded.path.display()
                ),
                Some(ConfigCommand::Migrate { output }) => {
                    ensure!(loaded.legacy, "Configuration is already schema v1");
                    if cli.dry_run {
                        println!("[plan] migrate {} to {}", path.display(), output.display());
                    } else {
                        write_new(&output, &serde_json::to_vec_pretty(&loaded.config)?)?;
                        println!("Migrated to {}; original preserved", output.display());
                    }
                }
                None => println!("{}", serde_json::to_string_pretty(&loaded.config)?),
                _ => unreachable!(),
            }
            return Ok(());
        }
        Some(Commands::Action { name }) => Request::Action(name),
        Some(Commands::Build {
            no_desktop,
            desktop_only,
            installer,
            run,
        }) => Request::Build {
            no_desktop,
            desktop_only,
            installer,
            run,
        },
        Some(Commands::Test {
            backend,
            frontend,
            smoke,
            cli,
        }) => Request::Test {
            backend,
            frontend,
            smoke,
            cli,
        },
        Some(Commands::Deploy {
            skip_build,
            preflight,
            rollback,
            force,
            desktop,
            retries,
        }) => Request::Deploy(deploy::Options {
            skip_build,
            preflight,
            rollback,
            force,
            desktop,
            retries,
        }),
        Some(Commands::Run { no_build, no_open }) => Request::Run { no_build, no_open },
        Some(Commands::Status) => Request::Status,
        Some(Commands::Watch { interval }) => Request::Watch { interval },
        Some(Commands::Logs {
            tail,
            errors,
            since,
        }) => Request::Logs {
            tail,
            errors,
            since,
        },
        Some(Commands::Db) => Request::Db,
        Some(Commands::Backup { operation }) => {
            if operation != "verify" {
                bail!("Only backup verify is supported; backups are owned by the server schedule");
            }
            Request::Backup
        }
        Some(Commands::Restart) => Request::Restart,
        Some(Commands::Open { destination }) => Request::Open(destination),
        Some(Commands::Claude) => Request::Action("claude".into()),
    };
    if loaded.legacy {
        runner.log("Using legacy config in memory. `cedar config migrate --output <path>` saves a separate schema v1 copy.");
    }
    operations::execute(&runner, &program, &request)
}

fn write_new(path: &std::path::Path, bytes: &[u8]) -> Result<()> {
    use std::io::Write;
    let mut file = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(path)
        .with_context(|| {
            format!(
                "Create {} without overwriting an existing file",
                path.display()
            )
        })?;
    file.write_all(bytes)?;
    Ok(())
}
