using System.Text;
using CedarClerk.Core;

namespace CedarClerk.Tests;

public class CedarPackageTests
{
    private const string SampleDoc = """{"type":"doc","content":[{"type":"paragraph"}]}""";

    [Fact]
    public void Roundtrip_write_then_read_preserves_doc_title_and_assets()
    {
        var meta = new CedarPackageMeta("My Title", new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc));
        var assets = new[] { new CedarAsset("photo.jpg", Encoding.UTF8.GetBytes("fake-bytes")) };

        using var ms = new MemoryStream();
        CedarPackage.Write(ms, SampleDoc, meta, assets);
        ms.Position = 0;

        var result = CedarPackage.Read(ms);

        Assert.Equal("My Title", result.Title);
        Assert.Equal(meta.CreatedAt, result.CreatedAt);
        Assert.Equal(SampleDoc, result.DocumentJson);
        Assert.True(result.Assets.ContainsKey("photo.jpg"));
        Assert.Equal("fake-bytes", Encoding.UTF8.GetString(result.Assets["photo.jpg"]));
    }

    [Fact]
    public void Corrupted_zip_throws_clear_exception()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip file"));
        var ex = Assert.Throws<CedarPackageException>(() => CedarPackage.Read(ms));
        Assert.Contains("zip", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatVersion_greater_than_current_throws_clear_exception()
    {
        using var ms = new MemoryStream();
        CedarPackage.Write(ms, SampleDoc, new CedarPackageMeta("T", DateTime.UtcNow), []);
        ms.Position = 0;

        // bump formatVersion in the produced package to simulate a future format
        using var bumped = new MemoryStream();
        BumpFormatVersion(ms, bumped);
        bumped.Position = 0;

        var ex = Assert.Throws<CedarPackageException>(() => CedarPackage.Read(bumped));
        Assert.Contains("formatVersion", ex.Message);
    }

    [Fact]
    public void Missing_document_json_throws_clear_exception()
    {
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("assets/photo.jpg");
            using var s = entry.Open();
            s.Write("fake"u8);
        }
        ms.Position = 0;

        var ex = Assert.Throws<CedarPackageException>(() => CedarPackage.Read(ms));
        Assert.Contains("document.json", ex.Message);
    }

    [Fact]
    public void FindReferencedMediaPaths_collects_paths_from_nested_arrays_and_objects()
    {
        const string json = """
        {
            "type": "doc",
            "content": [
                { "type": "image", "attrs": { "src": "/media/a.jpg" } },
                { "type": "collage", "attrs": { "images": ["/media/b.png", "/media/c.gif"] } },
                { "type": "paragraph" }
            ]
        }
        """;

        var paths = CedarPackage.FindReferencedMediaPaths(json);

        Assert.Equal(["a.jpg", "b.png", "c.gif"], paths.OrderBy(p => p));
    }

    [Fact]
    public void RewriteMediaPaths_replaces_known_names_and_leaves_unmapped_untouched()
    {
        const string json = """{"type":"image","attrs":{"src":"/media/old.jpg"}}""";
        var rewritten = CedarPackage.RewriteMediaPaths(json, new Dictionary<string, string> { ["old.jpg"] = "new.jpg" });

        Assert.Contains("/media/new.jpg", rewritten);
        Assert.DoesNotContain("old.jpg", rewritten);
    }

    private static void BumpFormatVersion(Stream source, Stream destination)
    {
        using var srcArchive = new System.IO.Compression.ZipArchive(source, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        using var destArchive = new System.IO.Compression.ZipArchive(destination, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true);

        foreach (var entry in srcArchive.Entries)
        {
            using var srcStream = entry.Open();
            if (entry.FullName == "document.json")
            {
                using var reader = new StreamReader(srcStream);
                var text = reader.ReadToEnd().Replace("\"formatVersion\":1", "\"formatVersion\":999");
                var newEntry = destArchive.CreateEntry(entry.FullName);
                using var writer = new StreamWriter(newEntry.Open());
                writer.Write(text);
            }
            else
            {
                var newEntry = destArchive.CreateEntry(entry.FullName);
                using var destStream = newEntry.Open();
                srcStream.CopyTo(destStream);
            }
        }
    }
}
