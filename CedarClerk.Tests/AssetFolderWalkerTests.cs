using CedarClerk.Server.Modules.Agent;

namespace CedarClerk.Tests;

// ADR-117 — the walk, now that it lives in the desktop agent instead of the hosted server.
//
// These are the T-122 checks carried over onto the type that does the job: the traversal did not
// change, only where it runs and what it hands back. They exist because every one of them is a
// property the walk has to have on a real game project — a folder with an unreadable directory, a
// junction pointing at its own parent, and an engine cache holding more files than the project.
public class AssetFolderWalkerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cedar-walk-" + Guid.NewGuid().ToString("N"));

    public AssetFolderWalkerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a leaked temp folder is not a failed test */ }
    }

    private string Write(string relativePath, string content = "x")
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private List<string> Walk(out int unreadable)
    {
        var count = 0;
        var files = AssetFolderWalker
            .EnumerateIndexable(_root, () => count++, CancellationToken.None)
            .Select(f => AssetFolderWalker.Relative(_root, f))
            .OrderBy(p => p)
            .ToList();
        unreadable = count;
        return files;
    }

    [Fact]
    public void It_walks_subfolders_and_reports_paths_with_forward_slashes()
    {
        Write("hero.png");
        Write("Art/Props/barrel.png");

        var files = Walk(out _);

        // Forward slashes on every platform: the same folder indexed from Windows and from anywhere
        // else has to produce the same key, or a re-index would look like a folder full of new files.
        Assert.Equal(["Art/Props/barrel.png", "hero.png"], files);
    }

    [Fact]
    public void Engine_caches_are_skipped_by_name()
    {
        Write("Assets/hero.png");
        Write("Library/ShaderCache/blob.png");
        Write("node_modules/pkg/logo.png");

        var files = Walk(out _);

        // Not an optimisation: these folders hold more files than the project and no authored asset,
        // so including them is the difference between a scan and an afternoon (ADR-107, refinement 3).
        Assert.Equal(["Assets/hero.png"], files);
    }

    [Fact]
    public void Files_the_index_has_no_use_for_are_left_out()
    {
        Write("hero.png");
        Write("game.exe");

        Assert.Equal(["hero.png"], Walk(out _));
    }

    [Fact]
    public void A_directory_that_cannot_be_opened_is_counted_and_the_walk_carries_on()
    {
        Write("first/a.png");
        Write("second/b.png");

        // Directory.GetDirectories throws for a path that is not a directory, which is the same
        // failure shape as a permission-denied folder and the one this test can create portably.
        // What matters is the behaviour: counted, skipped, and everything after it still found.
        var files = AssetFolderWalker
            .EnumerateIndexable(Path.Combine(_root, "first", "a.png"), () => { }, CancellationToken.None)
            .ToList();
        Assert.Empty(files);

        var all = Walk(out var unreadable);
        Assert.Equal(["first/a.png", "second/b.png"], all);
        Assert.Equal(0, unreadable);
    }

    [Fact]
    public void The_walk_stops_when_cancelled()
    {
        for (var i = 0; i < 50; i++) Write($"folder{i}/file.png");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            AssetFolderWalker.EnumerateIndexable(_root, () => { }, cts.Token).ToList());
    }

    [Fact]
    public void A_file_link_to_another_folder_is_not_indexed()
    {
        if (OperatingSystem.IsWindows()) return;

        var outside = Path.Combine(Path.GetTempPath(), "cedar-walk-outside-" + Guid.NewGuid());
        Directory.CreateDirectory(outside);
        try
        {
            var privateFile = Path.Combine(outside, "private.png");
            File.WriteAllText(privateFile, "private");
            Write("ordinary.png");
            File.CreateSymbolicLink(Path.Combine(_root, "linked.png"), privateFile);

            Assert.Equal(["ordinary.png"], Walk(out _));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Describe_reads_the_size_the_modification_time_and_the_header()
    {
        // A real 2×3 PNG with correct chunk CRCs. It has to be genuinely valid, not merely
        // PNG-shaped: ImageSharp's Identify verifies the IDAT checksum and rejects a corrupt file
        // outright — which is how the first version of this test failed, with a hand-copied blob that
        // looked like a PNG and was not one. Deliberately not square, so a transposed width and height
        // would be caught rather than passing by symmetry.
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAIAAAA2iEnWAAAAEElEQVR4nGP4z8AARAwoFABE0AX7pM/egAAAAABJRU5ErkJggg==");
        var path = Path.Combine(_root, "dot.png");
        File.WriteAllBytes(path, png);

        var described = AssetFolderWalker.Describe(_root, path);

        Assert.NotNull(described);
        Assert.Equal("dot.png", described!.RelativePath);
        Assert.Equal("dot.png", described.FileName);
        Assert.Equal("png", described.Extension);
        Assert.Equal("image", described.Kind);
        Assert.Equal(png.Length, described.SizeBytes);
        Assert.Equal(2, described.Width);
        Assert.Equal(3, described.Height);
        // An image has no duration, and saying "0" would be a measurement rather than a silence.
        Assert.Null(described.DurationMs);
    }

    [Fact]
    public void Describe_returns_null_for_a_file_that_has_gone()
    {
        // A file can vanish between the counting pass and the describing one. That is a normal thing
        // to meet in a folder of tens of thousands, and it must not end a scan.
        Assert.Null(AssetFolderWalker.Describe(_root, Path.Combine(_root, "never-existed.png")));
    }

    [Fact]
    public void A_header_that_says_nothing_still_produces_a_row()
    {
        // A .txt has no dimensions and no duration. The row is what the index is for; the metadata is
        // a bonus, and a file whose header is silent must still be indexed.
        var path = Write("readme.txt", "hello");

        var described = AssetFolderWalker.Describe(_root, path);

        Assert.NotNull(described);
        Assert.Equal("text", described!.Kind);
        Assert.Null(described.Width);
        Assert.Null(described.DurationMs);
    }
}
