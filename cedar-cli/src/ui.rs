use crate::{
    config::{Config, Loaded, Program},
    deploy,
    operations::{self, Request},
    runner::{Event, Runner},
};
use anyhow::Result;
use crossterm::event::{self, Event as Input, KeyCode, KeyEventKind, KeyModifiers};
use ratatui::{
    Frame, Terminal,
    backend::TestBackend,
    buffer::Buffer,
    layout::{Constraint, Layout, Rect},
    style::{Color, Modifier, Style},
    text::{Line, Span},
    widgets::{
        Block, BorderType, Borders, Clear, Gauge, List, ListItem, ListState, Paragraph, Wrap,
    },
};
use std::{
    collections::VecDeque,
    fs,
    path::Path,
    sync::{
        atomic::Ordering,
        mpsc::{self, Receiver, Sender},
    },
    thread,
    time::{Duration, Instant},
};

const BG: Color = Color::Rgb(9, 17, 22);
const PANEL: Color = Color::Rgb(15, 28, 32);
const EDGE: Color = Color::Rgb(42, 69, 70);
const MINT: Color = Color::Rgb(139, 239, 179);
const GOLD: Color = Color::Rgb(246, 194, 109);
const TEXT: Color = Color::Rgb(228, 235, 223);
const MUTED: Color = Color::Rgb(134, 159, 158);
const RED: Color = Color::Rgb(249, 137, 128);

#[derive(Clone)]
struct Choice {
    label: String,
    detail: String,
    request: Request,
    risk: bool,
}

fn choices(p: &Program) -> Vec<Choice> {
    let mut choices = Vec::new();
    let mut add = |label: &str, detail: &str, request, risk| {
        choices.push(Choice {
            label: label.into(),
            detail: detail.into(),
            request,
            risk,
        })
    };
    if p.deploy.is_some() {
        add(
            "Deploy release",
            "Build, upload and verify. Confirm immediately before switching production.",
            Request::Deploy(deploy::Options {
                retries: 5,
                ..Default::default()
            }),
            true,
        );
    }
    if p.actions.contains_key("test") {
        add(
            "Run local tests",
            "All configured suites. Each failure keeps its own output and exit status.",
            Request::Action("test".into()),
            false,
        );
    }
    if p.serve.is_some() {
        add(
            "Run locally",
            "Build and serve the published artifact. Stop only the process started here.",
            Request::Run {
                no_build: false,
                no_open: false,
            },
            false,
        );
    }
    if p.actions.contains_key("build") {
        add(
            "Build application",
            "Produce the deployment artifact and record its source and checksums.",
            Request::Action("build".into()),
            false,
        );
    }
    if p.deploy.is_some() {
        add(
            "Deploy preflight",
            "Read source state, public health and SSH readiness without switching production.",
            Request::Deploy(deploy::Options {
                preflight: true,
                retries: 5,
                ..Default::default()
            }),
            false,
        );
        add(
            "Resume deployment",
            "Reuse the verified build and continue the upload from its checked prefix.",
            Request::Deploy(deploy::Options {
                skip_build: true,
                retries: 5,
                ..Default::default()
            }),
            true,
        );
        add(
            "Production status",
            "Read health, service status, uptime, memory and disk usage.",
            Request::Status,
            false,
        );
        add(
            "Service logs",
            "Last 80 journal lines from the last 24 hours.",
            Request::Logs {
                tail: 80,
                errors: false,
                since: "-24h".into(),
            },
            false,
        );
        add(
            "Rollback release",
            "Verify app.prev, confirm and restore it. Keep the failed release in app.broken.",
            Request::Deploy(deploy::Options {
                rollback: true,
                retries: 5,
                ..Default::default()
            }),
            true,
        );
        add(
            "Restart service",
            "Confirm before restarting the selected production service.",
            Request::Restart,
            true,
        );
        add(
            "Verify backups",
            "Inspect local database copies and the server backup schedule.",
            Request::Backup,
            false,
        );
        if p.cedar_clerk {
            add(
                "Inspect database",
                "Read-only quick_check, migration and row counts.",
                Request::Db,
                false,
            );
        }
    }
    for (id, a) in &p.actions {
        if id == "test" || id == "build" {
            continue;
        }
        add(
            &a.label,
            &a.description,
            Request::Action(id.clone()),
            a.confirm,
        );
    }
    for id in p.links.keys() {
        add(
            &format!("Open {id}"),
            &p.links[id],
            Request::Open(id.clone()),
            false,
        );
    }
    choices
}

struct Job {
    title: String,
    stage: String,
    logs: VecDeque<String>,
    started: Instant,
    finished: Option<bool>,
    duration: Option<Duration>,
    progress: Option<(u64, u64)>,
    receiver: Receiver<Event>,
    runner: Runner,
    confirm: Option<(String, Sender<bool>)>,
    scroll: usize,
    cancel_requested: bool,
}

struct App {
    config: Config,
    selected: usize,
    action: usize,
    actions: Vec<Choice>,
    job: Option<Job>,
    animated: bool,
    notice: String,
    filter: String,
    searching: bool,
    version: String,
}
impl App {
    fn new(config: Config, selected: &str, no_animation: bool) -> Self {
        let index = config
            .programs
            .iter()
            .position(|p| p.id == selected)
            .unwrap_or(0);
        let actions = choices(&config.programs[index]);
        let version = config.programs[index].version().unwrap_or_default();
        Self {
            animated: config.appearance.animation && !no_animation,
            config,
            selected: index,
            action: 0,
            actions,
            job: None,
            notice: "Ready when you are. Choose an action to begin.".into(),
            filter: String::new(),
            searching: false,
            version,
        }
    }
    fn program(&self) -> &Program {
        &self.config.programs[self.selected]
    }
    fn filtered(&self) -> Vec<&Choice> {
        self.actions
            .iter()
            .filter(|a| a.label.to_lowercase().contains(&self.filter.to_lowercase()))
            .collect()
    }
    fn switch(&mut self, delta: isize) {
        self.selected = (self.selected as isize + delta)
            .rem_euclid(self.config.programs.len() as isize) as usize;
        self.actions = choices(self.program());
        self.version = self.program().version().unwrap_or_default();
        self.action = 0;
        self.filter.clear();
    }
    fn launch(&mut self, base: &Runner) {
        let Some(choice) = self.filtered().get(self.action).cloned().cloned() else {
            return;
        };
        let (tx, rx) = mpsc::channel();
        let runner = Runner {
            cancel: Default::default(),
            events: Some(tx.clone()),
            ..base.clone()
        };
        let worker = runner.clone();
        let program = self.program().clone();
        let title = choice.label.clone();
        self.job = Some(Job {
            title,
            stage: "Preparing".into(),
            logs: VecDeque::new(),
            started: Instant::now(),
            finished: None,
            duration: None,
            progress: None,
            receiver: rx,
            runner,
            confirm: None,
            scroll: 0,
            cancel_requested: false,
        });
        thread::spawn(move || {
            let outcome = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                operations::execute(&worker, &program, &choice.request)
            }));
            let (ok, message) = match outcome {
                Ok(Ok(())) => (true, "Completed".into()),
                Ok(Err(e)) => (false, format!("{e:#}")),
                Err(_) => (
                    false,
                    "Worker stopped unexpectedly; terminal remains available".into(),
                ),
            };
            let _ = tx.send(Event::Finished { ok, message });
        });
    }
    fn receive(&mut self) {
        let Some(j) = &mut self.job else {
            return;
        };
        for event in j.receiver.try_iter() {
            match event {
                Event::Line(s) => {
                    for line in s.lines() {
                        j.logs.push_back(line.into());
                    }
                }
                Event::Stage(s) => {
                    j.stage = s.clone();
                    j.logs.push_back(format!("━━ {s}"));
                    j.progress = None;
                }
                Event::Progress(sent, total) => j.progress = Some((sent, total)),
                Event::Confirm(q, tx) => j.confirm = Some((q, tx)),
                Event::Finished { ok, message } => {
                    j.finished = Some(ok);
                    j.duration = Some(j.started.elapsed());
                    j.confirm = None;
                    j.logs.push_back(message);
                    j.stage = if j.cancel_requested {
                        "Stopped"
                    } else if ok {
                        "Completed"
                    } else {
                        "Needs attention"
                    }
                    .into();
                }
            }
            while j.logs.len() > 3000 {
                j.logs.pop_front();
            }
        }
    }
}

pub fn run(loaded: Loaded, selected: &str, no_animation: bool, base: Runner) -> Result<()> {
    let mut app = App::new(loaded.config, selected, no_animation);
    let mut terminal = ratatui::init();
    let start = Instant::now();
    let result = (|| -> Result<()> {
        loop {
            app.receive();
            if base.cancel.swap(false, Ordering::Relaxed) {
                if let Some(job) = &mut app.job {
                    job.runner.cancel.store(true, Ordering::Relaxed);
                    job.cancel_requested = true;
                } else {
                    break;
                }
            }
            terminal.draw(|f| {
                draw(
                    f,
                    &app,
                    if app.animated {
                        start.elapsed().as_secs_f64()
                    } else {
                        4.0
                    },
                )
            })?;
            let wait = if app.animated {
                1000 / u64::from(app.config.appearance.fps)
            } else {
                200
            };
            if !event::poll(Duration::from_millis(wait))? {
                continue;
            }
            let Input::Key(key) = event::read()? else {
                continue;
            };
            if key.kind != KeyEventKind::Press {
                continue;
            }
            if let Some(job) = &mut app.job {
                if job.confirm.is_some() {
                    match key.code {
                        KeyCode::Char('y') | KeyCode::Char('Y') => {
                            let (_, tx) = job.confirm.take().unwrap();
                            let _ = tx.send(true);
                        }
                        KeyCode::Enter | KeyCode::Esc | KeyCode::Char('n') | KeyCode::Char('N') => {
                            let (_, tx) = job.confirm.take().unwrap();
                            let _ = tx.send(false);
                        }
                        _ => {}
                    }
                    continue;
                }
                match key.code {
                    KeyCode::Enter | KeyCode::Esc if job.finished.is_some() => {
                        app.job = None;
                    }
                    KeyCode::Char('c') | KeyCode::Esc if job.finished.is_none() => {
                        job.runner.cancel.store(true, Ordering::Relaxed);
                        job.cancel_requested = true;
                        job.logs.push_back("Cancellation requested. A production swap, if active, must finish verification.".into());
                    }
                    KeyCode::PageUp => {
                        job.scroll = (job.scroll + 10).min(job.logs.len().saturating_sub(1))
                    }
                    KeyCode::PageDown => job.scroll = job.scroll.saturating_sub(10),
                    KeyCode::Home => job.scroll = job.logs.len().saturating_sub(1),
                    KeyCode::End => job.scroll = 0,
                    _ => {}
                }
                continue;
            }
            if app.searching {
                match key.code {
                    KeyCode::Enter | KeyCode::Esc => app.searching = false,
                    KeyCode::Backspace => {
                        app.filter.pop();
                        app.action = 0;
                    }
                    KeyCode::Char(c) => {
                        app.filter.push(c);
                        app.action = 0;
                    }
                    _ => {}
                }
                continue;
            }
            match key.code {
                KeyCode::Char('q') | KeyCode::Esc => break,
                KeyCode::Char('c') if key.modifiers.contains(KeyModifiers::CONTROL) => break,
                KeyCode::Down | KeyCode::Char('j') => {
                    let n = app.filtered().len();
                    if n > 0 {
                        app.action = (app.action + 1) % n;
                    }
                }
                KeyCode::Up | KeyCode::Char('k') => {
                    let n = app.filtered().len();
                    if n > 0 {
                        app.action = (app.action + n - 1) % n;
                    }
                }
                KeyCode::Tab | KeyCode::Right => app.switch(1),
                KeyCode::BackTab | KeyCode::Left => app.switch(-1),
                KeyCode::Enter => {
                    let interactive = app.filtered().get(app.action).and_then(|choice| {
                        if let Request::Action(id) = &choice.request {
                            app.program()
                                .actions
                                .get(id)
                                .filter(|a| a.interactive)
                                .map(|_| choice.request.clone())
                        } else {
                            None
                        }
                    });
                    if let Some(request) = interactive {
                        ratatui::restore();
                        let result = operations::execute(&base, app.program(), &request);
                        base.cancel.store(false, Ordering::Relaxed);
                        terminal = ratatui::init();
                        app.notice = match result {
                            Ok(()) => "Interactive action completed".into(),
                            Err(e) => format!("{e:#}"),
                        };
                    } else {
                        app.launch(&base);
                    }
                }
                KeyCode::Char('/') => {
                    app.searching = true;
                    app.filter.clear();
                    app.action = 0;
                }
                KeyCode::Char('a') => app.animated = !app.animated,
                KeyCode::Char('r') => {
                    let id = app.program().id.clone();
                    match crate::config::load(&loaded.path, &std::env::current_dir()?) {
                        Ok(new) => {
                            app = App::new(new.config, &id, !app.animated);
                            app.notice = "Configuration reloaded".into();
                        }
                        Err(e) => app.notice = format!("Configuration unchanged: {e}"),
                    }
                }
                _ => {}
            }
        }
        Ok(())
    })();
    ratatui::restore();
    result
}

fn panel(title: impl Into<Line<'static>>) -> Block<'static> {
    Block::default()
        .borders(Borders::ALL)
        .border_type(BorderType::Rounded)
        .border_style(Style::default().fg(EDGE))
        .style(Style::default().bg(PANEL).fg(TEXT))
        .title(title)
}

fn draw(f: &mut Frame, app: &App, time: f64) {
    let area = f.area();
    f.render_widget(
        Block::default().style(Style::default().bg(BG).fg(TEXT)),
        area,
    );
    if area.width < 40 || area.height < 16 {
        f.render_widget(
            Paragraph::new(
                "CEDAR\nOperations console\n\nResize to at least 40 × 16.\nQ / Esc to close.",
            )
            .style(Style::default().fg(MINT)),
            area,
        );
        return;
    }
    let rows = Layout::vertical([
        Constraint::Length(2),
        Constraint::Min(8),
        Constraint::Length(2),
    ])
    .split(area);
    let header = Line::from(vec![
        Span::styled(
            "  CEDAR ",
            Style::default().fg(MINT).add_modifier(Modifier::BOLD),
        ),
        Span::styled(" / OPERATIONS", Style::default().fg(MUTED)),
        Span::styled(
            format!(
                "    {}  {}",
                app.program().name,
                if app.version.is_empty() {
                    String::new()
                } else {
                    format!("v{}", app.version)
                }
            ),
            Style::default().fg(TEXT),
        ),
    ]);
    f.render_widget(Paragraph::new(header), rows[0]);
    if let Some(job) = &app.job {
        draw_job(f, rows[1], job);
    } else {
        let hero = if area.height >= 36 {
            19
        } else if area.height >= 28 {
            13
        } else {
            5
        };
        let body = Layout::vertical([Constraint::Length(hero), Constraint::Min(5)]).split(rows[1]);
        scene(f.buffer_mut(), body[0], time);
        let columns = if area.width >= 85 {
            Layout::horizontal([Constraint::Length(29), Constraint::Min(30)]).split(body[1])
        } else {
            Layout::horizontal([Constraint::Length(0), Constraint::Min(30)]).split(body[1])
        };
        if columns[0].width > 0 {
            let p = app.program();
            let mut lines = vec![
                Line::from(Span::styled(
                    &p.name,
                    Style::default().fg(MINT).add_modifier(Modifier::BOLD),
                )),
                Line::from(p.description.as_str()),
                Line::from(""),
            ];
            lines.push(Line::styled(
                format!(
                    "{} / {} programs",
                    app.selected + 1,
                    app.config.programs.len()
                ),
                Style::default().fg(MUTED),
            ));
            if let Some(d) = &p.deploy {
                lines.push(Line::from(""));
                lines.push(Line::styled("DEPLOY TARGET", Style::default().fg(GOLD)));
                lines.push(Line::from(d.host.clone()));
                lines.push(Line::from(d.service.clone()));
            }
            lines.push(Line::from(""));
            lines.push(Line::styled(
                "← →  Switch program",
                Style::default().fg(MUTED),
            ));
            f.render_widget(
                Paragraph::new(lines)
                    .block(panel(" PROGRAM "))
                    .wrap(Wrap { trim: false }),
                columns[0],
            );
        }
        let right = Layout::vertical([
            Constraint::Min(3),
            Constraint::Length(if columns[1].height >= 12 { 4 } else { 2 }),
        ])
        .split(columns[1]);
        let filtered = app.filtered();
        let items: Vec<_> = filtered
            .iter()
            .map(|c| {
                ListItem::new(Line::from(vec![
                    Span::styled(
                        if c.risk { "  !  " } else { "  ·  " },
                        Style::default().fg(if c.risk { GOLD } else { MUTED }),
                    ),
                    Span::raw(&c.label),
                ]))
            })
            .collect();
        let title = if app.searching || !app.filter.is_empty() {
            format!(
                " FIND / {}{} ",
                app.filter,
                if app.searching { "▏" } else { "" }
            )
        } else {
            " ACTIONS ".into()
        };
        let list = List::new(items)
            .block(panel(title))
            .highlight_style(
                Style::default()
                    .bg(Color::Rgb(38, 68, 58))
                    .fg(MINT)
                    .add_modifier(Modifier::BOLD),
            )
            .highlight_symbol("▸");
        let mut state = ListState::default().with_selected(if filtered.is_empty() {
            None
        } else {
            Some(app.action)
        });
        f.render_stateful_widget(list, right[0], &mut state);
        let detail = filtered
            .get(app.action)
            .map(|c| c.detail.as_str())
            .unwrap_or("No matching actions. Press / to change the filter.");
        f.render_widget(
            Paragraph::new(detail)
                .style(Style::default().fg(MUTED))
                .wrap(Wrap { trim: true })
                .block(Block::default().padding(ratatui::widgets::Padding::horizontal(1))),
            right[1],
        );
    }
    let footer = if app.job.is_some() {
        "  PgUp/PgDn Scroll   C Cancel   Enter Back when finished"
    } else if area.width < 85 {
        "  ↑↓ Select  Enter Run  Tab Program  / Find  Q Quit"
    } else {
        "  ↑↓ Select   Enter Run   ←→ Program   / Find   A Motion   R Reload JSON   Q Quit"
    };
    f.render_widget(
        Paragraph::new(vec![
            Line::styled(footer, Style::default().fg(MUTED)),
            Line::styled(format!("  {}", app.notice), Style::default().fg(EDGE)),
        ]),
        rows[2],
    );
    if let Some(job) = &app.job
        && let Some((question, _)) = &job.confirm
    {
        confirmation(f, question);
    }
}

fn draw_job(f: &mut Frame, area: Rect, j: &Job) {
    let rows = Layout::vertical([
        Constraint::Length(5),
        Constraint::Min(3),
        Constraint::Length(3),
    ])
    .split(area);
    let elapsed = j.duration.unwrap_or_else(|| j.started.elapsed());
    let color = match j.finished {
        Some(true) => MINT,
        Some(false) => RED,
        None => GOLD,
    };
    f.render_widget(
        Paragraph::new(vec![
            Line::styled(
                &j.stage,
                Style::default().fg(color).add_modifier(Modifier::BOLD),
            ),
            Line::styled(
                format!(
                    "Elapsed {:02}:{:02}   {}",
                    elapsed.as_secs() / 60,
                    elapsed.as_secs() % 60,
                    if j.cancel_requested {
                        "Cancellation requested"
                    } else if j.finished.is_some() {
                        "Press Enter to return"
                    } else {
                        "Running"
                    }
                ),
                Style::default().fg(MUTED),
            ),
        ])
        .block(panel(format!(" {} ", j.title))),
        rows[0],
    );
    let height = rows[1].height.saturating_sub(2) as usize;
    let end = j.logs.len().saturating_sub(j.scroll);
    let begin = end.saturating_sub(height);
    let lines = j
        .logs
        .iter()
        .skip(begin)
        .take(height)
        .map(|s| {
            Line::styled(
                s.as_str(),
                Style::default().fg(if s.starts_with("━━") {
                    MINT
                } else if s.contains("FAILED") || s.contains("error:") {
                    RED
                } else {
                    TEXT
                }),
            )
        })
        .collect::<Vec<_>>();
    f.render_widget(
        Paragraph::new(lines).block(panel(format!(
            " OUTPUT · {} lines{} ",
            j.logs.len(),
            if j.scroll > 0 { " · scrolled" } else { "" }
        ))),
        rows[1],
    );
    if let Some((sent, total)) = j.progress {
        f.render_widget(
            Gauge::default()
                .block(panel(" TRANSFER "))
                .gauge_style(Style::default().fg(MINT).bg(EDGE))
                .ratio((sent as f64 / total.max(1) as f64).clamp(0.0, 1.0))
                .label(format!(
                    "{:.1} / {:.1} MB",
                    sent as f64 / 1_048_576.0,
                    total as f64 / 1_048_576.0
                )),
            rows[2],
        );
    } else {
        f.render_widget(
            Paragraph::new(
                "C / Esc requests cancellation. Output remains available after the action stops.",
            )
            .style(Style::default().fg(MUTED))
            .wrap(Wrap { trim: true }),
            rows[2],
        );
    }
}

fn confirmation(f: &mut Frame, question: &str) {
    let area = f.area();
    let width = area.width.saturating_sub(6).min(76);
    let height = 10.min(area.height);
    let rect = Rect::new(
        (area.width - width) / 2,
        (area.height - height) / 2,
        width,
        height,
    );
    f.render_widget(Clear, rect);
    let lines = vec![
        Line::from(""),
        Line::styled(question, Style::default().fg(TEXT)),
        Line::from(""),
        Line::styled(
            "  [ Enter / N ]  Cancel       [ Y ]  Confirm",
            Style::default().fg(GOLD).add_modifier(Modifier::BOLD),
        ),
        Line::styled("  Default: cancel", Style::default().fg(MUTED)),
    ];
    f.render_widget(
        Paragraph::new(lines)
            .wrap(Wrap { trim: true })
            .block(panel(" CONFIRM ACTION ").border_style(Style::default().fg(GOLD))),
        rect,
    );
}

const WORD: [&str; 6] = [
    " ██████╗███████╗██████╗  █████╗ ██████╗ ",
    "██╔════╝██╔════╝██╔══██╗██╔══██╗██╔══██╗",
    "██║     █████╗  ██║  ██║███████║██████╔╝",
    "██║     ██╔══╝  ██║  ██║██╔══██║██╔══██╗",
    "╚██████╗███████╗██████╔╝██║  ██║██║  ██║",
    " ╚═════╝╚══════╝╚═════╝ ╚═╝  ╚═╝╚═╝  ╚═╝",
];

fn rgb(c: Color) -> (u8, u8, u8) {
    if let Color::Rgb(r, g, b) = c {
        (r, g, b)
    } else {
        (228, 235, 223)
    }
}
fn blend(a: Color, b: Color, t: f64) -> Color {
    let (ar, ag, ab) = rgb(a);
    let (br, bg, bb) = rgb(b);
    let t = t.clamp(0.0, 1.0);
    Color::Rgb(
        (ar as f64 + (br as f64 - ar as f64) * t) as u8,
        (ag as f64 + (bg as f64 - ag as f64) * t) as u8,
        (ab as f64 + (bb as f64 - ab as f64) * t) as u8,
    )
}

pub fn scene(buf: &mut Buffer, area: Rect, time: f64) {
    if area.width == 0 || area.height == 0 {
        return;
    }
    for y in 0..area.height {
        for x in 0..area.width {
            let u = x as f64 / area.width as f64;
            let v = y as f64 / area.height as f64;
            let curtain = ((u * 11.0 + time * 0.22).sin() * 0.10
                + (u * 5.0 - time * 0.11).cos() * 0.08
                + 0.28
                - v)
                .abs();
            let glow = (-curtain * 14.0).exp() * 0.62;
            let sky = blend(
                Color::Rgb(10, 19, 31),
                Color::Rgb(91, 63, 112),
                (u * 4.0 + time * 0.1).sin() * 0.16 + 0.15,
            );
            let mut color = blend(sky, Color::Rgb(44, 125, 99), glow);
            let mut glyph = " ";
            for layer in 0..3 {
                let center = (u * (12.0 + layer as f64 * 4.0)
                    + layer as f64 * 0.43
                    + time * 0.002 * (layer + 1) as f64)
                    .fract();
                let tree = 0.48 + layer as f64 * 0.12 + (center - 0.5).abs() * 0.40;
                if v > tree {
                    color = [
                        Color::Rgb(19, 47, 49),
                        Color::Rgb(15, 37, 39),
                        Color::Rgb(9, 27, 30),
                    ][layer];
                }
            }
            let n = (x as u64 * 7919 + y as u64 * 104729 + 13) % 211;
            if n == 1 && v < 0.55 {
                glyph = if (time * 0.7 + x as f64).sin() > 0.5 {
                    "*"
                } else {
                    "·"
                };
            }
            let cell = &mut buf[(area.x + x, area.y + y)];
            cell.set_symbol(glyph).set_bg(color).set_fg(blend(
                MUTED,
                GOLD,
                ((time + x as f64).sin() + 1.0) * 0.5,
            ));
        }
    }
    if area.height < 9 || area.width < 65 {
        let text = "C E D A R  /  O P E R A T I O N S";
        put(
            buf,
            area,
            (area.width.saturating_sub(text.len() as u16)) / 2,
            area.height / 2,
            text,
            MINT,
        );
        return;
    }
    let total = 64u16;
    let start = area.width.saturating_sub(total) / 2;
    let top = area.height.saturating_sub(14) / 2;
    let tree_height = 13u16.min(area.height);
    for y in 0..tree_height {
        let half = match y {
            0 => 0,
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 2,
            5 => 3,
            6 => 4,
            7 => 5,
            8 => 4,
            9 => 5,
            10 => 7,
            _ => 1,
        };
        for dx in -half..=half {
            let x = start as i32 + 9 + dx;
            let yy = top + y;
            if x >= 0 && (x as u16) < area.width && yy < area.height {
                let shimmer = ((dx as f64 * 0.7 + y as f64 * 0.4 - time * 1.2).sin() + 1.0) * 0.5;
                let c = if y >= 11 {
                    blend(Color::Rgb(122, 84, 54), GOLD, 0.25)
                } else {
                    blend(Color::Rgb(35, 120, 76), MINT, shimmer * 0.7)
                };
                let glyph = if y == 0 {
                    "+"
                } else if dx.abs() == half {
                    "▄"
                } else {
                    "█"
                };
                buf[(area.x + x as u16, area.y + yy)]
                    .set_symbol(glyph)
                    .set_fg(c);
            }
        }
    }
    for (row, text) in WORD.iter().enumerate() {
        let y = top + 2 + row as u16;
        for (i, c) in text.chars().enumerate() {
            let x = start + 24 + i as u16;
            if x >= area.width || y >= area.height || c == ' ' {
                continue;
            }
            let wave = ((i as f64 * 0.16 - time * 0.85 + row as f64 * 0.15).sin() + 1.0) * 0.5;
            let highlight = ((i as f64 - time * 9.0).rem_euclid(70.0) - 35.0).abs();
            let color = blend(
                blend(MINT, GOLD, wave * 0.65),
                Color::Rgb(245, 255, 235),
                (-highlight * 0.5).exp(),
            );
            buf[(area.x + x, area.y + y)]
                .set_symbol(&c.to_string())
                .set_fg(color);
        }
    }
    put(
        buf,
        area,
        start + 25,
        top + 9,
        "O P E R A T I O N S   C O N S O L E",
        GOLD,
    );
    if area.height >= 16 {
        put(
            buf,
            area,
            start + 25,
            top + 11,
            "BUILD  /  TEST  /  SHIP",
            MUTED,
        );
        let float_x = ((time * 3.0).rem_euclid(area.width as f64)) as u16;
        let float_y = (area.height as f64 * 0.63 + (time * 1.5).sin() * 2.0) as u16;
        if float_y < area.height {
            buf[(area.x + float_x, area.y + float_y)]
                .set_symbol("·")
                .set_fg(GOLD);
        }
        for x in 0..area.width {
            let color = blend(EDGE, MINT, ((x as f64 * 0.13 - time).sin() + 1.0) * 0.18);
            buf[(area.x + x, area.y + area.height - 1)]
                .set_symbol("─")
                .set_fg(color);
        }
    }
}

fn put(buf: &mut Buffer, area: Rect, x: u16, y: u16, text: &str, color: Color) {
    if y >= area.height {
        return;
    }
    for (i, c) in text.chars().enumerate() {
        if x as usize + i >= area.width as usize {
            break;
        }
        buf[(area.x + x + i as u16, area.y + y)]
            .set_symbol(&c.to_string())
            .set_fg(color);
    }
}

pub fn preview(
    config: &Config,
    selected: &str,
    width: u16,
    height: u16,
    time: f64,
    path: &Path,
) -> Result<()> {
    let app = App::new(config.clone(), selected, false);
    let mut terminal = Terminal::new(TestBackend::new(width, height))?;
    terminal.draw(|f| draw(f, &app, time))?;
    let buffer = terminal.backend().buffer();
    let mut html = String::from(
        "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Cedar · Ratatui frame</title><style>body{margin:0;background:#091116;display:grid;place-items:center;min-height:100vh}pre{font:14px/1.28 'Cascadia Mono','Consolas',monospace;white-space:pre;padding:22px;box-shadow:0 25px 90px #0008;border:1px solid #2a4546;border-radius:12px}span{display:inline}</style><pre aria-label=\"Exact Ratatui terminal buffer\">",
    );
    for y in 0..height {
        for x in 0..width {
            let cell = &buffer[(x, y)];
            let (fr, fg, fb) = rgb(cell.fg);
            let (br, bg, bb) = rgb(cell.bg);
            let text = cell
                .symbol()
                .replace('&', "&amp;")
                .replace('<', "&lt;")
                .replace('>', "&gt;");
            html.push_str(&format!("<span style=\"color:#{fr:02x}{fg:02x}{fb:02x};background:#{br:02x}{bg:02x}{bb:02x}\">{text}</span>"));
        }
        html.push('\n');
    }
    html.push_str("</pre></html>");
    fs::write(path, html)?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn all_terminal_sizes_render_without_panic() {
        for (w, h) in [
            (1, 1),
            (30, 12),
            (40, 16),
            (64, 24),
            (80, 30),
            (120, 42),
            (180, 55),
        ] {
            let config = crate::config::bundled().unwrap();
            let app = App::new(config, "cedarclerk", false);
            let mut term = Terminal::new(TestBackend::new(w, h)).unwrap();
            for t in [0.0, 1.0, 4.0, 19.0] {
                term.draw(|f| draw(f, &app, t)).unwrap();
            }
        }
    }
    #[test]
    fn animation_changes_pixels_and_keeps_actions_visible() {
        let app = App::new(crate::config::bundled().unwrap(), "cedarclerk", false);
        let mut term = Terminal::new(TestBackend::new(120, 42)).unwrap();
        term.draw(|f| draw(f, &app, 0.0)).unwrap();
        let before = term.backend().buffer().clone();
        term.draw(|f| draw(f, &app, 4.0)).unwrap();
        assert_ne!(&before, term.backend().buffer());
        let s: String = term
            .backend()
            .buffer()
            .content
            .iter()
            .map(|c| c.symbol())
            .collect();
        assert!(s.contains("Deploy release"));
        assert!(s.contains("Run local tests"));
    }
    #[test]
    fn confirmation_defaults_to_cancel_copy() {
        let mut term = Terminal::new(TestBackend::new(80, 24)).unwrap();
        term.draw(|f| confirmation(f, "Deploy release to production?"))
            .unwrap();
        let s: String = term
            .backend()
            .buffer()
            .content
            .iter()
            .map(|c| c.symbol())
            .collect();
        assert!(s.contains("Default: cancel"));
    }
}
