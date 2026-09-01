using CedarClerk.Server;
using CedarClerk.Server.Tenancy;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CedarClerk.Tests;

// ADR-238 clauses 4 and 5 — the lookup behind GET /api/assets/meta. Two things it must never do:
// answer for a file another account owns (an asset guid on a published document is public), and
// throw because a file's header could not be read.
public class AssetMetaTests : IDisposable
{
    private const string OwnerA = "owner-a";
    private const string OwnerB = "owner-b";

    private readonly string mediaDir = Path.Combine(Path.GetTempPath(), "cedar-asset-meta-tests", Guid.NewGuid().ToString());
    private readonly Microsoft.Data.Sqlite.SqliteConnection connection;
    private readonly CedarDbContext db;
    private readonly MediaPaths media;

    public AssetMetaTests()
    {
        Directory.CreateDirectory(mediaDir);
        media = new MediaPaths(mediaDir);

        connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        db = new CedarDbContext(
            new DbContextOptionsBuilder<CedarDbContext>().UseSqlite(connection).Options,
            TenantProvider.Platform());
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = OwnerA, UserName = "a@x.test", TenantUsername = "a" });
        db.Users.Add(new ApplicationUser { Id = OwnerB, UserName = "b@x.test", TenantUsername = "b" });
        db.SaveChanges();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
        try { Directory.Delete(mediaDir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private Asset SeedImage(string ownerId, int width, int height, Guid? projectId = null)
    {
        var asset = new Asset
        {
            OwnerId = ownerId, FileName = "picture.png", ContentType = "image/png", ProjectId = projectId,
        };
        asset.LocalPath = $"asset_{asset.Id}.png";

        using (var image = new Image<Rgba32>(width, height))
            image.SaveAsPng(Path.Combine(mediaDir, asset.LocalPath));
        asset.SizeBytes = new FileInfo(Path.Combine(mediaDir, asset.LocalPath)).Length;

        db.Assets.Add(asset);
        db.SaveChanges();
        return asset;
    }

    private Asset SeedFile(string ownerId, string fileName, string contentType, byte[] bytes)
    {
        var asset = new Asset
        {
            OwnerId = ownerId, FileName = fileName, ContentType = contentType, SizeBytes = bytes.Length,
        };
        asset.LocalPath = $"asset_{asset.Id}{Path.GetExtension(fileName)}";
        File.WriteAllBytes(Path.Combine(mediaDir, asset.LocalPath), bytes);
        db.Assets.Add(asset);
        db.SaveChanges();
        return asset;
    }

    private Task<AssetEndpoints.AssetMeta?> Lookup(string ownerId, Guid? id = null, string? path = null) =>
        AssetEndpoints.LookupMetaAsync(db, ownerId, id, path, media);

    [Fact]
    public async Task An_image_answers_with_its_own_dimensions()
    {
        var asset = SeedImage(OwnerA, 320, 200);

        var meta = await Lookup(OwnerA, id: asset.Id);

        Assert.NotNull(meta);
        Assert.Equal(asset.Id, meta!.Id);
        Assert.Equal("picture.png", meta.FileName);
        Assert.Equal("image/png", meta.ContentType);
        Assert.Equal(asset.SizeBytes, meta.SizeBytes);
        Assert.Equal(320, meta.Width);
        Assert.Equal(200, meta.Height);
    }

    [Fact]
    public async Task The_project_is_carried_and_null_is_an_answer()
    {
        var project = new Project { OwnerId = OwnerA, Name = "Cedar Quest" };
        db.Projects.Add(project);
        db.SaveChanges();

        var filed = SeedImage(OwnerA, 8, 8, projectId: project.Id);
        var loose = SeedImage(OwnerA, 8, 8);

        Assert.Equal(project.Id, (await Lookup(OwnerA, id: filed.Id))!.ProjectId);
        Assert.Null((await Lookup(OwnerA, id: loose.Id))!.ProjectId);
    }

    [Fact]
    public async Task A_document_that_predates_the_asset_id_answers_by_its_media_path()
    {
        var asset = SeedImage(OwnerA, 64, 48);

        var meta = await Lookup(OwnerA, path: asset.LocalPath);

        Assert.Equal(asset.Id, meta!.Id);
        Assert.Equal(64, meta.Width);
    }

    [Fact]
    public async Task The_src_a_node_actually_carries_resolves_too()
    {
        var asset = SeedImage(OwnerA, 64, 48);

        var meta = await Lookup(OwnerA, path: $"/media/{asset.LocalPath}");

        Assert.Equal(asset.Id, meta!.Id);
    }

    [Fact]
    public async Task The_id_wins_when_both_are_given()
    {
        var byId = SeedImage(OwnerA, 10, 10);
        var byPath = SeedImage(OwnerA, 20, 20);

        var meta = await Lookup(OwnerA, id: byId.Id, path: byPath.LocalPath);

        Assert.Equal(byId.Id, meta!.Id);
    }

    [Fact]
    public async Task Another_accounts_asset_is_not_found_by_id()
    {
        var theirs = SeedImage(OwnerB, 100, 100);

        Assert.Null(await Lookup(OwnerA, id: theirs.Id));
    }

    [Fact]
    public async Task Another_accounts_asset_is_not_found_by_path_either()
    {
        var theirs = SeedImage(OwnerB, 100, 100);

        Assert.Null(await Lookup(OwnerA, path: theirs.LocalPath));
        Assert.Null(await Lookup(OwnerA, path: $"/media/{theirs.LocalPath}"));
    }

    [Fact]
    public async Task An_id_nobody_owns_is_not_found()
    {
        Assert.Null(await Lookup(OwnerA, id: Guid.NewGuid()));
    }

    [Fact]
    public async Task Nothing_named_resolves_to_nothing()
    {
        SeedImage(OwnerA, 10, 10);

        Assert.Null(await Lookup(OwnerA));
        Assert.Null(await Lookup(OwnerA, path: "   "));
    }

    [Fact]
    public async Task A_non_image_has_no_dimensions_rather_than_zeroes()
    {
        var asset = SeedFile(OwnerA, "theme.mp3", "audio/mpeg", "not really audio"u8.ToArray());

        var meta = await Lookup(OwnerA, id: asset.Id);

        Assert.Equal(asset.SizeBytes, meta!.SizeBytes);
        Assert.Null(meta.Width);
        Assert.Null(meta.Height);
    }

    [Fact]
    public async Task An_unreadable_image_answers_without_dimensions_instead_of_throwing()
    {
        var asset = SeedFile(OwnerA, "broken.png", "image/png", "this is not a png"u8.ToArray());

        var meta = await Lookup(OwnerA, id: asset.Id);

        Assert.Null(meta!.Width);
        Assert.Null(meta.Height);
    }

    [Fact]
    public async Task A_row_whose_file_has_vanished_still_answers()
    {
        var asset = SeedImage(OwnerA, 30, 30);
        File.Delete(Path.Combine(mediaDir, asset.LocalPath));

        var meta = await Lookup(OwnerA, id: asset.Id);

        Assert.Equal(asset.Id, meta!.Id);
        Assert.Null(meta.Width);
    }
}
