using Spectre.Console;

namespace CedarClerk.Cli.Execution;

// The other half of "--dry-run touches nothing" (ADR-119).
//
// ICommandRunner covered every process and every ssh call, which was the whole story while the
// pipelines were wrappers around scripts. It stopped being the whole story the moment the build
// moved into C#: `Directory.Delete(publish)` is not a command, it is a method call, and it would
// have deleted a real directory during a run that had promised to change nothing.
//
// Same arrangement as the runner rather than a new one: the implementation is swapped, so no pipeline
// contains an "if dry run" branch — the branch that is eventually forgotten on exactly one path.
//
// Reads are deliberately not here. File.Exists and ReadAllText change nothing, and routing them
// through a seam would make every check unanswerable under --dry-run instead of merely inert.
public interface IFileWriter
{
    void DeleteDirectory(string path);

    void CopyTree(string source, string destination);

    void WriteText(string path, string content);
}

public sealed class RealFileWriter : IFileWriter
{
    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    // Copy-Item -Recurse, and it has to overwrite: publish/wwwroot is fresh every time, but the
    // desktop tree is not always deleted first.
    public void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, destination));

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination), overwrite: true);
    }

    public void WriteText(string path, string content) => File.WriteAllText(path, content);
}

public sealed class DryRunFileWriter : IFileWriter
{
    private readonly IAnsiConsole _console;

    public DryRunFileWriter(IAnsiConsole console) => _console = console;

    public void DeleteDirectory(string path) => Print("rm -r", path);

    public void CopyTree(string source, string destination) => Print("copy", $"{source} -> {destination}");

    public void WriteText(string path, string content) => Print("write", $"{path} ({content.Length} chars)");

    private void Print(string kind, string what) =>
        _console.MarkupLine($"[grey35]{kind,-5}[/] [grey]{Markup.Escape(what)}[/]");
}
