using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server;
using CedarClerk.Server.Modules.IndieDev;
using Microsoft.EntityFrameworkCore;

namespace CedarClerk.Tests;

public class CollectionOrderingTests
{
    [Fact]
    public void Media_library_orders_the_full_query_before_paging()
    {
        var assets = new[]
        {
            new Asset { Id = Id(3), FileName = "zeta.png", CreatedAt = new DateTime(2026, 8, 3) },
            new Asset { Id = Id(2), FileName = "alpha.png", CreatedAt = new DateTime(2026, 8, 2) },
            new Asset { Id = Id(1), FileName = "alpha.png", CreatedAt = new DateTime(2026, 8, 1) },
        }.AsQueryable();

        var page = AssetEndpoints.OrderForLibrary(assets, "name", "asc").Skip(1).Take(1).Single();

        Assert.Equal(Id(2), page.Id);
    }

    [Fact]
    public void Project_assets_order_the_full_query_before_paging()
    {
        var entries = new[]
        {
            new AssetEntry { Id = Id(3), RelativePath = "c.png", ModifiedAt = new DateTime(2026, 8, 1) },
            new AssetEntry { Id = Id(2), RelativePath = "b.png", ModifiedAt = new DateTime(2026, 8, 3) },
            new AssetEntry { Id = Id(1), RelativePath = "a.png", ModifiedAt = new DateTime(2026, 8, 3) },
        }.AsQueryable();

        var page = AssetIndexEndpoints.OrderForIndex(entries, "modified", "desc").Skip(1).Take(1).Single();

        Assert.Equal(Id(2), page.Id);
    }

    [Fact]
    public void Media_library_collection_query_validation_is_strict()
    {
        Assert.Null(AssetEndpoints.ValidateLibraryCollectionQuery("image", "none", "size", "desc"));
        Assert.Null(AssetEndpoints.ValidateLibraryCollectionQuery(null, Id(1).ToString(), "added", "asc"));
        Assert.Equal(ErrorMessages.UnknownMediaTypeFilter,
            AssetEndpoints.ValidateLibraryCollectionQuery("document", null, "added", "desc"));
        Assert.Equal(ErrorMessages.UnknownMediaProjectFilter,
            AssetEndpoints.ValidateLibraryCollectionQuery(null, "not-a-project", "added", "desc"));
        Assert.Equal(ErrorMessages.UnknownMediaSortKey,
            AssetEndpoints.ValidateLibraryCollectionQuery(null, null, "owner", "desc"));
        Assert.Equal(ErrorMessages.UnknownMediaSortDirection,
            AssetEndpoints.ValidateLibraryCollectionQuery(null, null, "name", "sideways"));
    }

    [Fact]
    public void Project_asset_collection_query_validation_is_strict()
    {
        Assert.Null(AssetIndexEndpoints.ValidateCollectionQuery("image", "status", "desc"));
        Assert.Null(AssetIndexEndpoints.ValidateCollectionQuery(null, "modified", "asc"));
        Assert.Equal(ErrorMessages.UnknownAssetKindFilter,
            AssetIndexEndpoints.ValidateCollectionQuery("document", "path", "asc"));
        Assert.Equal(ErrorMessages.UnknownAssetSortKey,
            AssetIndexEndpoints.ValidateCollectionQuery(null, "owner", "asc"));
        Assert.Equal(ErrorMessages.UnknownAssetSortDirection,
            AssetIndexEndpoints.ValidateCollectionQuery(null, "path", "sideways"));
    }

    [Fact]
    public async Task Admin_posts_filter_the_full_query_before_the_page_limit()
    {
        using var fixture = new CanvasFixture();
        var ownerId = fixture.User("admin-post-owner");
        await using var db = fixture.Platform();
        db.Drafts.AddRange(Enumerable.Range(1, Consts.Admin.PostPageSize + 1)
            .Select(i => new Draft
            {
                Id = Id(i),
                OwnerId = ownerId,
                Title = i == Consts.Admin.PostPageSize + 1 ? "Needle devlog" : $"Post {i:D3}",
            }));
        await db.SaveChangesAsync();

        var query = AdminEndpoints.QueryPosts(db.Drafts, db.Users, db.Comments,
            "needle", "all", "title", "asc");
        var total = await query.CountAsync();
        var page = await query
            .Skip(0)
            .Take(Consts.Admin.PostPageSize)
            .ToListAsync();

        Assert.Equal(1, total);
        Assert.Collection(page, post => Assert.Equal(Id(Consts.Admin.PostPageSize + 1), post.Id));
    }

    [Fact]
    public async Task Admin_payments_filter_the_full_query_before_the_page_limit()
    {
        using var fixture = new CanvasFixture();
        var ownerId = fixture.User("admin-payment-owner");
        await using var db = fixture.Platform();
        db.Payments.AddRange(Enumerable.Range(1, Consts.Admin.PaymentPageSize + 1)
            .Select(i => new Payment
            {
                Id = Id(i),
                OwnerId = ownerId,
                Provider = i == Consts.Admin.PaymentPageSize + 1 ? "Needle Pay" : "Stripe",
                Plan = "Pro",
                Status = "Completed",
            }));
        await db.SaveChangesAsync();

        var query = AdminEndpoints.QueryPayments(db.Payments, db.Users,
            "needle", "all", "created", "desc");
        var total = await query.CountAsync();
        var page = await query
            .Skip(0)
            .Take(Consts.Admin.PaymentPageSize)
            .ToListAsync();

        Assert.Equal(1, total);
        Assert.Collection(page, payment => Assert.Equal(Id(Consts.Admin.PaymentPageSize + 1), payment.Id));
    }

    [Fact]
    public void Admin_collection_query_validation_is_strict_and_payment_statuses_come_from_data()
    {
        Assert.Null(AdminEndpoints.ValidatePostCollectionQuery("PUBLISHED", "activity", "DESC"));
        Assert.Equal(ErrorMessages.UnknownAdminPostStateFilter,
            AdminEndpoints.ValidatePostCollectionQuery("deleted", "title", "asc"));
        Assert.Equal(ErrorMessages.UnknownAdminPostSortKey,
            AdminEndpoints.ValidatePostCollectionQuery("all", "created", "asc"));
        Assert.Equal(ErrorMessages.UnknownAdminSortDirection,
            AdminEndpoints.ValidatePostCollectionQuery("all", "title", "sideways"));

        var paymentStatuses = new[] { "Completed", "review-required" };
        Assert.Null(AdminEndpoints.ValidatePaymentCollectionQuery("completed", paymentStatuses, "amount", "ASC"));
        Assert.Null(AdminEndpoints.ValidatePaymentCollectionQuery("review-required", paymentStatuses, "created", "desc"));
        Assert.Equal(ErrorMessages.UnknownAdminPaymentStatusFilter,
            AdminEndpoints.ValidatePaymentCollectionQuery("refunded", paymentStatuses, "created", "desc"));
        Assert.Equal(ErrorMessages.UnknownAdminPaymentSortKey,
            AdminEndpoints.ValidatePaymentCollectionQuery("all", paymentStatuses, "currency", "asc"));
        Assert.Equal(ErrorMessages.UnknownAdminSortDirection,
            AdminEndpoints.ValidatePaymentCollectionQuery("all", paymentStatuses, "created", "sideways"));
    }

    [Fact]
    public void Bare_admin_collection_queries_keep_their_newest_first_order()
    {
        var older = new DateTime(2026, 8, 1);
        var newer = new DateTime(2026, 8, 2);
        var users = Array.Empty<ApplicationUser>().AsQueryable();

        var payments = AdminEndpoints.QueryPayments(new[]
        {
            new Payment { Id = Id(1), CreatedAt = older, Provider = "Stripe", Plan = "Pro" },
            new Payment { Id = Id(2), CreatedAt = newer, Provider = "Stripe", Plan = "Pro" },
        }.AsQueryable(), users, null, null, null, null).Select(payment => payment.Id).ToArray();
        Assert.Equal(new[] { Id(2), Id(1) }, payments);

        var posts = AdminEndpoints.QueryPosts(new[]
        {
            new Draft { Id = Id(1), OwnerId = "owner", Title = "Older", UpdatedAt = older },
            new Draft { Id = Id(2), OwnerId = "owner", Title = "Newer", UpdatedAt = newer },
        }.AsQueryable(), users, Array.Empty<Comment>().AsQueryable(), null, null, null, null)
            .Select(post => post.Id).ToArray();
        Assert.Equal(new[] { Id(2), Id(1) }, posts);
    }

    [Fact]
    public void Admin_usage_includes_an_owner_with_ai_calls_and_no_files()
    {
        var rows = AdminEndpoints.MergeUsage(
            new Dictionary<string, string?> { ["storage"] = "storage@example.test", ["ai"] = "ai@example.test" },
            new Dictionary<string, long> { ["storage"] = 4096 },
            new Dictionary<string, int> { ["storage"] = 2 },
            new Dictionary<string, int> { ["ai"] = 7 });

        Assert.Collection(rows,
            storage =>
            {
                Assert.Equal("storage", storage.OwnerId);
                Assert.Equal(4096, storage.Bytes);
                Assert.Equal(2, storage.Files);
                Assert.Equal(0, storage.AiToday);
            },
            ai =>
            {
                Assert.Equal("ai", ai.OwnerId);
                Assert.Equal("ai@example.test", ai.OwnerEmail);
                Assert.Equal(0, ai.Bytes);
                Assert.Equal(0, ai.Files);
                Assert.Equal(7, ai.AiToday);
            });
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}");
}
