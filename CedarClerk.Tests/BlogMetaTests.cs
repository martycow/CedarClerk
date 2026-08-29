using CedarClerk.Server;
using Microsoft.AspNetCore.Http;

namespace CedarClerk.Tests;

// ADR-124 — what each blog page reveals to link-preview crawlers. These drive the real
// HandleRequest end to end: the policy is only as good as the pages that actually emit (or
// withhold) the tags.
public class BlogMetaTests
{
    private const string ParagraphJson =
        """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Первый абзац девлога."}]}]}""";

    private static Draft Seed(CedarDbContext db, Action<Draft>? mutate = null)
    {
        db.Users.Add(new ApplicationUser { Id = "o1", UserName = "o1", IsAdmin = true });
        var draft = new Draft
        {
            Title = "Devlog 1",
            CedarJson = ParagraphJson,
            OwnerId = "o1",
            PrimaryLanguage = "ru",
            BlogSlug = "devlog-1",
            IsBlogPublished = true,
            BlogPublishedAt = DateTime.UtcNow,
        };
        mutate?.Invoke(draft);
        db.Drafts.Add(draft);
        db.SaveChanges();
        return draft;
    }

    private static async Task<string> Get(CedarDbContext db, string path, string query = "")
    {
        var ctx = BlogTestHost.Request("GET", path, db, query);
        await BlogEndpoints.HandleRequest(ctx);
        return BlogTestHost.Body(ctx);
    }

    [Fact]
    public async Task A_public_post_carries_the_full_set()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        var body = await Get(db, "/devlog-1");

        Assert.Contains("og:title\" content=\"Devlog 1\"", body);
        Assert.Contains("og:type\" content=\"article\"", body);
        Assert.Contains("og:description\" content=\"Первый абзац девлога.\"", body);
        Assert.Contains("rel=\"canonical\" href=\"https://tenant.cedarclerk.app/devlog-1\"", body);
        Assert.Contains("article:published_time", body);
        // Wave 1 item 1 — a post with no image of its own advertises the generated title card,
        // versioned so a title edit busts caches; the static og-default stays for non-post pages.
        Assert.Contains("og:image\" content=\"https://tenant.cedarclerk.app/og/devlog-1.png?v=", body);
        Assert.Contains("twitter:card\" content=\"summary_large_image\"", body);
        // Wave 1 item 4 — structured data rides along wherever the meta already tells everything.
        Assert.Contains("application/ld+json", body);
        Assert.Contains("\"@type\":\"Article\"", body);
    }

    [Fact]
    public async Task JsonLd_never_claims_a_modification_before_publication()
    {
        using var db = BlogTestHost.EmptyDatabase();
        // The first-publish shape: the draft was last saved before the publish click, so
        // UpdatedAt predates BlogPublishedAt.
        Seed(db, d =>
        {
            d.UpdatedAt = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
            d.BlogPublishedAt = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);
        });

        var body = await Get(db, "/devlog-1");

        Assert.Contains("\"datePublished\":\"2026-05-10T12:00:00Z\"", body);
        Assert.Contains("\"dateModified\":\"2026-05-10T12:00:00Z\"", body);
        Assert.DoesNotContain("\"dateModified\":\"2026-05-01", body);
    }

    [Fact]
    public async Task A_private_unlisted_post_reveals_nothing()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, d => d.IsPrivate = true);

        var body = await Get(db, "/devlog-1");

        Assert.DoesNotContain("og:", body);
        Assert.DoesNotContain("twitter:", body);
        Assert.DoesNotContain("canonical", body);
    }

    [Fact]
    public async Task A_private_gate_reveals_nothing()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, d =>
        {
            d.IsPrivate = true;
            d.RegistrationFormJson = """{"intro":"Привет","requireName":true,"requireEmail":true,"questions":[]}""";
        });

        var body = await Get(db, "/devlog-1");

        Assert.Contains("reg-gate", body);
        Assert.DoesNotContain("og:", body);
    }

    [Fact]
    public async Task A_listed_private_post_gets_title_and_image_only()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db, d =>
        {
            d.IsPrivate = true;
            d.IsListedWhilePrivate = true;
        });

        var body = await Get(db, "/devlog-1");

        Assert.Contains("og:title\" content=\"Devlog 1\"", body);
        // The card draws only the title, which TitleImageOnly already puts in the meta.
        Assert.Contains("og:image\" content=\"https://tenant.cedarclerk.app/og/devlog-1.png?v=", body);
        Assert.DoesNotContain("og:description", body);
        Assert.DoesNotContain("article:", body);
        Assert.DoesNotContain("hreflang", body);
        Assert.DoesNotContain("application/ld+json", body);
    }

    [Fact]
    public async Task Canonical_follows_the_language()
    {
        using var db = BlogTestHost.EmptyDatabase();
        var draft = Seed(db);
        db.DraftTranslations.Add(new DraftTranslation
        {
            DraftId = draft.Id, OwnerId = draft.OwnerId, Language = "en", Title = "Devlog 1 EN", CedarJson = ParagraphJson,
        });
        db.SaveChanges();

        var ru = await Get(db, "/devlog-1");
        var en = await Get(db, "/devlog-1", "?lang=en");

        Assert.Contains("rel=\"canonical\" href=\"https://tenant.cedarclerk.app/devlog-1\"", ru);
        Assert.Contains("rel=\"canonical\" href=\"https://tenant.cedarclerk.app/devlog-1?lang=en\"", en);
        Assert.Contains("hreflang=\"en\" href=\"https://tenant.cedarclerk.app/devlog-1?lang=en\"", ru);
        Assert.Contains("hreflang=\"x-default\" href=\"https://tenant.cedarclerk.app/devlog-1\"", ru);
    }

    [Fact]
    public async Task The_index_is_a_website()
    {
        using var db = BlogTestHost.EmptyDatabase();
        Seed(db);

        var body = await Get(db, "/");

        Assert.Contains("og:type\" content=\"website\"", body);
        Assert.Contains("og:image\" content=\"https://tenant.cedarclerk.app/og-default.png\"", body);
    }
}
