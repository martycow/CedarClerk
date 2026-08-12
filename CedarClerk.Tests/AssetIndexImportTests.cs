using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;

namespace CedarClerk.Tests;

// ADR-117 — the cloud side of indexing: it accepts a description of a folder instead of walking one.
//
// Two things are pinned here, and both are the kind that fail silently rather than loudly.
//
// The **path check** is a security boundary. The cloud never opens these files, so a bad path cannot
// escape anything here — but the detail endpoint hands `Path.Combine(root, relativePath)` back to a
// desktop client, which does open files. That is the whole reason to be strict about a string that
// looks harmless.
//
// The **re-scan judgements** are the ones that cost real money if wrong: a preview needlessly dropped
// is a preview re-decoded and re-uploaded, and on Marty's "every preview, no limit" that multiplies by
// the size of a Unity folder.
public class AssetIndexImportTests
{
    private static AssetIndexEndpoints.FileRecord Record(
        string relativePath = "Art/hero.png", long size = 100, DateTime? modified = null,
        string? kind = AssetKinds.Image, int? width = 64, int? height = 32) =>
        new(relativePath, Path.GetFileName(relativePath), Path.GetExtension(relativePath).TrimStart('.'),
            kind, size, modified ?? new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc),
            width, height, null, null);

    private static readonly DateTime Now = new(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

    // ---- what may be stored as a path --------------------------------------------------------

    [Theory]
    [InlineData("hero.png")]
    [InlineData("Art/Props/barrel.png")]
    [InlineData("Art/one two three/a b.png")]
    // A dot inside a name is fine; only a whole segment of ".." is not.
    [InlineData("Art/v1.2/hero.final.png")]
    public void An_ordinary_relative_path_is_accepted(string path)
    {
        Assert.Equal(path, AssetIndexEndpoints.SanitiseRelativePath(path));
    }

    [Theory]
    // Climbing out. Harmless in the database, dangerous once a desktop client joins it to a root.
    [InlineData("../../../etc/passwd")]
    [InlineData("Art/../../secrets/key.png")]
    [InlineData("..")]
    // Absolute, and drive-qualified: neither is relative to anything.
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/System32/config")]
    [InlineData("C:\\Windows\\System32\\config")]
    // Backslashes at all: the agent normalises to '/', so one arriving here means something else sent
    // it, and accepting both spellings would make one file two rows.
    [InlineData("Art\\hero.png")]
    // Empty segments — a double slash is not a path anyone meant to write.
    [InlineData("Art//hero.png")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_path_that_could_escape_or_duplicate_is_refused(string? path)
    {
        Assert.Null(AssetIndexEndpoints.SanitiseRelativePath(path));
    }

    [Fact]
    public void An_absurdly_long_path_is_refused()
    {
        // Bounded so a client cannot grow rows without limit. 1024 is far above any real path and far
        // below anything that would matter per row.
        Assert.Null(AssetIndexEndpoints.SanitiseRelativePath("a/" + new string('x', 1100)));
    }

    // ---- folding a described file into a row -------------------------------------------------

    [Fact]
    public void A_new_file_becomes_a_row_carrying_its_owner_project_and_header()
    {
        var row = AssetIndexEndpoints.ApplyRecord(Record(), "Art/hero.png", null, "u1", Guid.Empty, Now);

        Assert.Equal("u1", row.OwnerId);
        Assert.Equal("Art/hero.png", row.RelativePath);
        Assert.Equal("hero.png", row.FileName);
        Assert.Equal("png", row.Extension);
        Assert.Equal(AssetKinds.Image, row.Kind);
        Assert.Equal(64, row.Width);
        Assert.Equal(Now, row.IndexedAt);
        // Stamped even though it could have been null, so a silent header is not re-read forever.
        Assert.Equal(row.ModifiedAt, row.MetadataForModifiedAt);
    }

    [Fact]
    public void An_unknown_kind_becomes_other_rather_than_being_stored_as_sent()
    {
        var row = AssetIndexEndpoints.ApplyRecord(
            Record(kind: "spaceship"), "Art/hero.png", null, "u1", Guid.Empty, Now);

        // The list endpoint filters on this column, so a value outside the known set would be a chip
        // that can never be clicked and a row that no filter reaches.
        Assert.Equal(AssetKinds.Other, row.Kind);
    }

    [Fact]
    public void A_file_found_again_stops_being_missing()
    {
        var existing = new AssetEntry
        {
            OwnerId = "u1", RelativePath = "Art/hero.png",
            ModifiedAt = Record().ModifiedAt, SizeBytes = 100,
            MissingSince = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var row = AssetIndexEndpoints.ApplyRecord(Record(), "Art/hero.png", existing, "u1", Guid.Empty, Now);

        Assert.Null(row.MissingSince);
        Assert.Same(existing, row);
    }

    [Fact]
    public void A_rescan_that_finds_the_same_file_keeps_its_preview()
    {
        var stamp = Record().ModifiedAt;
        var existing = new AssetEntry
        {
            OwnerId = "u1", RelativePath = "Art/hero.png",
            ModifiedAt = stamp, SizeBytes = 100,
            ThumbnailForModifiedAt = stamp,
        };

        var row = AssetIndexEndpoints.ApplyRecord(Record(), "Art/hero.png", existing, "u1", Guid.Empty, Now);

        // The expensive assertion in this file. If this drops the preview, every re-index re-decodes and
        // re-uploads the whole folder, and "every preview, no limit" stops being affordable.
        Assert.Equal(stamp, row.ThumbnailForModifiedAt);
    }

    [Fact]
    public void A_file_whose_bytes_changed_loses_its_preview()
    {
        var oldStamp = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new AssetEntry
        {
            OwnerId = "u1", RelativePath = "Art/hero.png",
            ModifiedAt = oldStamp, SizeBytes = 100,
            ThumbnailForModifiedAt = oldStamp,
        };

        var row = AssetIndexEndpoints.ApplyRecord(Record(size: 250), "Art/hero.png", existing, "u1", Guid.Empty, Now);

        // A replaced sprite showing its predecessor is the bug this prevents, and it is the sort nobody
        // reports because it looks like a caching quirk rather than wrong data.
        Assert.Null(row.ThumbnailForModifiedAt);
    }

    [Fact]
    public void A_file_touched_without_being_edited_also_loses_its_preview()
    {
        // Same size, new timestamp. We cannot tell "touched" from "edited to the same length" without
        // hashing the bytes, and guessing wrong in this direction only costs one re-render.
        var oldStamp = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new AssetEntry
        {
            OwnerId = "u1", RelativePath = "Art/hero.png",
            ModifiedAt = oldStamp, SizeBytes = 100,
            ThumbnailForModifiedAt = oldStamp,
        };

        var row = AssetIndexEndpoints.ApplyRecord(Record(), "Art/hero.png", existing, "u1", Guid.Empty, Now);

        Assert.Null(row.ThumbnailForModifiedAt);
    }

    // ---- what may be stored as a preview -----------------------------------------------------

    [Fact]
    public void Only_real_jpeg_bytes_are_accepted_as_a_preview()
    {
        // Without this the upload endpoint is a way to store arbitrary bytes on the server under a name
        // the asset grid then serves back with an image content type.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0xFF, 0xD9];
        Assert.True(AssetMetadata.LooksLikeJpeg(jpeg));

        Assert.False(AssetMetadata.LooksLikeJpeg("<html>hello</html>"u8.ToArray()));
        // A PNG is a perfectly good image and still refused: the column that says a preview exists
        // promises a JPEG, and the endpoint has no business storing something else under it.
        Assert.False(AssetMetadata.LooksLikeJpeg([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        // Truncated: starts like a JPEG, never ends like one.
        Assert.False(AssetMetadata.LooksLikeJpeg([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.False(AssetMetadata.LooksLikeJpeg([]));
    }
}
