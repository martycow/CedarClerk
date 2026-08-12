using System.Diagnostics;
using CedarClerk.Cli.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CedarClerk.Cli.Pipelines;

public enum StageState { Pending, Running, Done, Warned, Failed, Skipped }

// A step that failed in a way that must stop everything. The pipeline throws it and the board turns
// it into the report — the direct descendant of Stop-Deploy in deploy.ps1, and it carries the same
// three parts, because the message alone was never the useful bit: State says what is true right
// now, Hints say what to type next.
public sealed class PipelineStop : Exception
{
    public PipelineStop(string message, IEnumerable<string>? state = null, IEnumerable<string>? hints = null)
        : base(message)
    {
        State = state?.ToArray() ?? Array.Empty<string>();
        Hints = hints?.ToArray() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> State { get; }
    public IReadOnlyList<string> Hints { get; }
}

public sealed class Stage
{
    public required string Name { get; init; }
    public string Detail { get; init; } = "";
    public StageState State { get; set; } = StageState.Pending;

    // Kept apart from State so that a step which warns halfway keeps drawing as running until it
    // actually finishes — the marker answers "where are we", the flag answers "how did it go".
    public bool Warned { get; set; }
    public TimeSpan Elapsed { get; set; }
    public string Note { get; set; } = "";
    public List<string> Lines { get; } = new();
}

// The screen a long job runs behind: the whole plan drawn up front, each step ticking over as it
// finishes, and the running one allowed to show anything it likes underneath itself.
//
// Drawing the plan before it happens is the same idea as the test grid's field of empty cells
// (ADR-118 decision 14) and it is here for the same reason: "seven steps, we are on three" is a
// different feeling from a line of text appearing every forty seconds with no idea how many are left.
//
// Nothing in here knows what a step does. A step reports a note, a warning or a renderable, and the
// board draws it — so adding a step to a pipeline never means touching this file, which is what
// keeps the two from growing into each other (ADR-119 decision 5).
public sealed class StageBoard
{
    private const int MaxLinesPerStage = 4;

    private readonly IAnsiConsole _console;
    private readonly Glyphs _glyphs;
    private readonly List<Stage> _stages = new();
    private readonly object _gate = new();
    private readonly Stopwatch _total = new();

    private LiveDisplayContext? _live;
    private Stage? _current;
    private Stopwatch? _stageWatch;
    private IRenderable? _detail;

    public StageBoard(IAnsiConsole console, Glyphs glyphs, string title)
    {
        _console = console;
        _glyphs = glyphs;
        Title = title;
    }

    public string Title { get; }
    public string Header { get; set; } = "";
    public Glyphs Glyphs => _glyphs;
    public IReadOnlyList<Stage> Stages => _stages;
    public TimeSpan Total => _total.Elapsed;

    // Declared before anything runs, so the list is a plan rather than a log.
    public StageBoard Plan(string name, string detail = "")
    {
        _stages.Add(new Stage { Name = name, Detail = detail });
        return this;
    }

    public void Skip(string name, string note = "")
    {
        var stage = Find(name);
        stage.State = StageState.Skipped;
        stage.Note = note;
        Refresh();
    }

    // Live needs a terminal it can rewind. A redirected one gets a line per step instead — the same
    // information, in the shape a log file can hold.
    private bool CanAnimate =>
        !Console.IsOutputRedirected && _console.Profile.Capabilities.Interactive && _console.Profile.Capabilities.Ansi;

    public async Task<int> RunAsync(Func<StageBoard, Task<int>> body, CancellationToken ct)
    {
        _total.Restart();

        if (!CanAnimate)
        {
            try
            {
                return await body(this);
            }
            finally
            {
                _total.Stop();
            }
        }

        var code = 0;
        Exception? escaped = null;

        await _console.Live(Compose())
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                lock (_gate) _live = ctx;

                using var ticker = new CancellationTokenSource();
                // The elapsed time of the running step has to keep moving even while nothing is
                // being reported — a frozen clock reads as a hung tool.
                var tick = Task.Run(async () =>
                {
                    while (!ticker.IsCancellationRequested)
                    {
                        Refresh();
                        try { await Task.Delay(120, ticker.Token); } catch (OperationCanceledException) { }
                    }
                }, CancellationToken.None);

                try
                {
                    code = await body(this);
                }
                catch (Exception ex)
                {
                    escaped = ex;
                }
                finally
                {
                    ticker.Cancel();
                    await tick;
                    lock (_gate) { _detail = null; }
                    Refresh();
                }
            });

        _total.Stop();
        lock (_gate) _live = null;

        // Rethrown outside Live: an exception escaping the callback would leave the terminal with the
        // cursor hidden and the live region half-drawn.
        if (escaped is not null) throw escaped;
        return code;
    }

    public async Task<T> StepAsync<T>(string name, Func<StageStep, Task<T>> body)
    {
        var stage = Find(name);
        lock (_gate)
        {
            _current = stage;
            _stageWatch = Stopwatch.StartNew();
            stage.State = StageState.Running;
            _detail = null;
        }

        if (!CanAnimate) _console.MarkupLine($"[grey]{_glyphs.Dot}[/] {Markup.Escape(stage.Name)}…");
        Refresh();

        try
        {
            var value = await body(new StageStep(this, stage));
            Close(stage, stage.Warned ? StageState.Warned : StageState.Done);
            return value;
        }
        catch (Exception)
        {
            Close(stage, StageState.Failed);
            throw;
        }
    }

    public Task StepAsync(string name, Func<StageStep, Task> body) =>
        StepAsync<object?>(name, async step => { await body(step); return null; });

    private void Close(Stage stage, StageState state)
    {
        lock (_gate)
        {
            stage.Elapsed = _stageWatch?.Elapsed ?? TimeSpan.Zero;
            stage.State = state;
            _current = null;
            _stageWatch = null;
            _detail = null;
        }

        if (!CanAnimate)
        {
            var mark = state == StageState.Failed ? _glyphs.Bad : state == StageState.Warned ? _glyphs.Warn : _glyphs.Ok;
            _console.MarkupLine($"  {mark} [grey]{Format.Duration(stage.Elapsed)}[/]   {Markup.Escape(stage.Note)}");
        }
        Refresh();
    }

    private Stage Find(string name) =>
        _stages.FirstOrDefault(s => s.Name == name)
        ?? throw new InvalidOperationException($"stage '{name}' was never planned");

    internal void SetDetail(IRenderable? renderable)
    {
        lock (_gate) _detail = renderable;
    }

    internal void AddLine(Stage stage, string markup)
    {
        lock (_gate)
        {
            stage.Lines.Add(markup);
            // Only the tail is kept: a step that reports fifty lines would push the plan off the top
            // of the screen, and the plan is the thing this board exists to keep visible.
            while (stage.Lines.Count > MaxLinesPerStage) stage.Lines.RemoveAt(0);
        }
        if (!CanAnimate) _console.MarkupLine($"      {markup}");
        Refresh();
    }

    public void Refresh()
    {
        lock (_gate)
        {
            if (_live is null) return;
            _live.UpdateTarget(Compose());
            _live.Refresh();
        }
    }

    private IRenderable Compose()
    {
        var rows = new List<IRenderable>();
        var width = _console.Profile.Width;

        rows.Add(Ui.Rule(_glyphs, Header.Length > 0 ? Header : $"[grey]{Markup.Escape(Title)}[/]", width));
        rows.Add(new Text(" "));

        var column = _stages.Count == 0 ? 8 : _stages.Max(s => s.Name.Length);
        var index = 0;

        foreach (var stage in _stages)
        {
            index++;
            var (mark, colour) = Marker(stage);
            var name = Markup.Escape(stage.Name.PadRight(column));

            var right = stage.State switch
            {
                StageState.Done or StageState.Warned or StageState.Failed =>
                    $"[grey]{Format.Duration(stage.Elapsed),8}[/]  [grey35]{Markup.Escape(stage.Note)}[/]",
                StageState.Running =>
                    $"[grey]{Format.Duration(_stageWatch?.Elapsed ?? TimeSpan.Zero),8}[/]  [grey35]{Markup.Escape(stage.Detail)}[/]",
                StageState.Skipped => $"[grey35]{"",8}  {Markup.Escape(stage.Note)}[/]",
                _ => $"[grey35]{"",8}  {Markup.Escape(stage.Detail)}[/]"
            };

            rows.Add(new Markup(
                $"[grey35]{index,2}[/] [{Palette.Hex(colour)}]{mark}[/] " +
                $"[{Palette.Hex(stage.State == StageState.Pending ? Palette.Faint : Palette.Text)}]{name}[/] {right}"));

            foreach (var line in stage.Lines)
                rows.Add(new Markup($"       {line}"));

            if (stage.State == StageState.Running && _detail is not null)
            {
                rows.Add(new Text(" "));
                rows.Add(new Padder(_detail, new Padding(7, 0, 0, 0)));
                rows.Add(new Text(" "));
            }
        }

        return new Rows(rows);
    }

    // Shape as well as colour, everywhere (ADR-118 decision 7): the pending marker is the same empty
    // cell the test grid uses, so "not yet" reads the same way in both screens.
    private (string Mark, Color Colour) Marker(Stage stage) => stage.State switch
    {
        StageState.Done => (_glyphs.Ok, Palette.Ok),
        StageState.Warned => (_glyphs.Warn, Palette.Warn),
        StageState.Failed => (_glyphs.Bad, Palette.Danger),
        StageState.Running => (_glyphs.Dot, Palette.Accent),
        StageState.Skipped => (_glyphs.Skip, Palette.Faint),
        _ => (_glyphs.CellPending, Palette.Faint)
    };
}

// What a running step is handed: somewhere to put a note, a warning, or a whole renderable of its
// own. It is a struct over the board rather than the board itself so a step cannot start another one.
public readonly struct StageStep
{
    private readonly StageBoard _board;
    private readonly Stage _stage;

    internal StageStep(StageBoard board, Stage stage)
    {
        _board = board;
        _stage = stage;
    }

    public void Note(string text) =>
        _board.AddLine(_stage, $"[grey]{Markup.Escape(text)}[/]");

    // A warning colours the step for the rest of its life: a run that warned and then finished is
    // not the same as one that simply finished, and the summary has to be able to say which.
    public void Warn(string text)
    {
        _stage.Warned = true;
        _board.AddLine(_stage, $"[{Palette.Hex(Palette.Warn)}]{Markup.Escape(text)}[/]");
    }

    public void Done(string note) => _stage.Note = note;

    public void Show(IRenderable? renderable)
    {
        _board.SetDetail(renderable);
        _board.Refresh();
    }
}
