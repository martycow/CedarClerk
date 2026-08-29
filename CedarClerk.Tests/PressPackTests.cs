using System.IO.Compression;
using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CedarClerk.Tests;

// Wave 1 item 6 — the press pack zip: factsheet plus whatever media actually exists on disk,
// scoped to the owner's own showcase and quietly skipping files that are gone.
public class PressPackTests : IDisposable
{
    private readonly string _mediaDir = Path.Combine(Path.GetTempPath(), "cedar-press-" + Guid.NewGuid().ToString("N"));

    public PressPackTests() => Directory.CreateDirectory(_mediaDir);

    public void Dispose() => Directory.Delete(_mediaDir, recursive: true);

    private HttpContext Context()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new MediaPaths(_mediaDir));
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        // Kestrel forbids synchronous IO on the response body; TestServer's pipe quietly allows
        // it, which is how a sync ZipArchive write once passed here and 500'd live. This stream
        // reproduces the production rule.
        ctx.Response.Body = new NoSyncIoStream();
        return ctx;
    }

    /// <summary>
    /// Refuses synchronous writes, the way Kestrel's response body does. Deliberately NOT a
    /// MemoryStream subclass: MemoryStream.CopyToAsync special-cases a MemoryStream destination
    /// into a synchronous Write — a fast path the real response body never takes.
    /// </summary>
    private sealed class NoSyncIoStream : Stream
    {
        private readonly MemoryStream _inner = new();

        private static Exception SyncIo() =>
            new InvalidOperationException("Synchronous operations are disallowed. Call the async version instead.");

        public override void Write(byte[] buffer, int offset, int count) => throw SyncIo();
        public override void Write(ReadOnlySpan<byte> buffer) => throw SyncIo();
        public override void WriteByte(byte value) => throw SyncIo();
        public override void Flush() => throw SyncIo();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            _inner.Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            _inner.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

        // Reads are the test's own inspection of what was sent; they stay ordinary.
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
    }

    private static Project PressProject(string ownerId = "o1") => new()
    {
        OwnerId = ownerId,
        Name = "Cedar Quest",
        Description = "A cozy forest adventure.",
        ShowcaseSlug = "cedar-quest",
        PressGenre = "Adventure",
        PressEngine = "Godot",
        PressPrice = "$9.99",
        PressContactEmail = "press@example.com",
        PressFactsheetRows = "Release date: 2027\nPlatforms: PC",
        ShowcaseLinks = "Steam|https://store.steampowered.com/app/1",
        ShowcaseGallery = "/media/shot-one.png\n/media/shot-missing.png",
        CoverUrl = "/media/cover.jpg",
    };

    [Fact]
    public async Task Pack_holds_factsheet_cover_and_existing_screenshots()
    {
        File.WriteAllBytes(Path.Combine(_mediaDir, "shot-one.png"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_mediaDir, "cover.jpg"), [4, 5, 6]);

        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Projects.Add(PressProject());
        db.SaveChanges();

        var ctx = Context();
        await PressPackEndpoint.HandleAsync(ctx, db, new BlogSite("o1", "tenant.cedarclerk.app"), "cedar-quest");

        Assert.Equal("application/zip", ctx.Response.ContentType);
        // The archive is buffered, so the response can promise its size — and a zero here is
        // exactly the sync-IO-on-Kestrel failure shape this test exists to catch.
        Assert.True(ctx.Response.ContentLength > 0);
        Assert.Equal(ctx.Response.ContentLength, ctx.Response.Body.Length);
        ctx.Response.Body.Position = 0;
        using var zip = new ZipArchive(ctx.Response.Body, ZipArchiveMode.Read);

        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("factsheet.txt", names);
        Assert.Contains("cover.jpg", names);
        Assert.Contains("screenshots/01-shot-one.png", names);
        // The missing screenshot is skipped, not an error and not an empty entry.
        Assert.DoesNotContain(names, n => n.Contains("shot-missing"));

        using var reader = new StreamReader(zip.GetEntry("factsheet.txt")!.Open());
        var factsheet = reader.ReadToEnd();
        Assert.Contains("Cedar Quest", factsheet);
        Assert.Contains("Genre: Adventure", factsheet);
        Assert.Contains("Press contact: press@example.com", factsheet);
        Assert.Contains("Release date: 2027", factsheet);
        Assert.Contains("Steam: https://store.steampowered.com/app/1", factsheet);
    }

    [Fact]
    public async Task Unknown_slug_and_foreign_owner_both_answer_404()
    {
        using var db = BlogTestHost.EmptyDatabase().WithOwner();
        db.Projects.Add(PressProject());
        db.SaveChanges();

        var wrongSlug = Context();
        await PressPackEndpoint.HandleAsync(wrongSlug, db, new BlogSite("o1", "tenant.cedarclerk.app"), "nope");
        Assert.Equal(StatusCodes.Status404NotFound, wrongSlug.Response.StatusCode);

        var wrongOwner = Context();
        await PressPackEndpoint.HandleAsync(wrongOwner, db, new BlogSite("somebody-else", "other.cedarclerk.app"), "cedar-quest");
        Assert.Equal(StatusCodes.Status404NotFound, wrongOwner.Response.StatusCode);
    }

    [Fact]
    public void Factsheet_omits_empty_fields()
    {
        var bare = new Project { OwnerId = "o1", Name = "Bare Game", ShowcaseSlug = "bare" };
        var text = PressPackEndpoint.FactsheetText(bare, new BlogSite("o1", "tenant.cedarclerk.app"));

        Assert.Contains("Bare Game", text);
        Assert.Contains("Website: https://tenant.cedarclerk.app/games/bare", text);
        Assert.DoesNotContain("Genre:", text);
        Assert.DoesNotContain("Price:", text);
        Assert.DoesNotContain("Links", text);
    }

    [Fact]
    public void Factsheet_prefers_the_custom_domain()
    {
        var project = PressProject();
        project.CustomDomain = "cedarquest.example";
        var text = PressPackEndpoint.FactsheetText(project, new BlogSite("o1", "tenant.cedarclerk.app"));
        Assert.Contains("Website: https://cedarquest.example", text);
    }
}
