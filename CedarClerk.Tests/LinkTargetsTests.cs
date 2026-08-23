using CedarClerk.Core;

namespace CedarClerk.Tests;

// T-141. The unique index on EntityLink can only do its job if a pair always lands the same way
// round — otherwise "this asset and that document" is two rows describing one fact.
public class LinkTargetsTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void A_pair_orders_the_same_way_whichever_end_it_came_from()
    {
        var fromAsset = LinkTargets.Order(LinkTargets.Asset, A, LinkTargets.Document, B);
        var fromDocument = LinkTargets.Order(LinkTargets.Document, B, LinkTargets.Asset, A);
        Assert.Equal(fromAsset, fromDocument);
    }

    [Fact]
    public void Two_things_of_the_same_kind_order_by_id()
    {
        // Task-to-task will exist (T-123); the ordering must already be total, or the first such
        // link would be the one that discovers this is undefined.
        var one = LinkTargets.Order(LinkTargets.Task, A, LinkTargets.Task, B);
        var other = LinkTargets.Order(LinkTargets.Task, B, LinkTargets.Task, A);
        Assert.Equal(one, other);
        Assert.Equal(A, one.FromId);
    }

    [Fact]
    public void Ordering_is_stable_and_names_both_sides()
    {
        var (fromType, fromId, toType, toId) = LinkTargets.Order(LinkTargets.Document, A, LinkTargets.Asset, B);
        // asset sorts before document, so the asset side comes first whatever was passed in.
        Assert.Equal(LinkTargets.Asset, fromType);
        Assert.Equal(B, fromId);
        Assert.Equal(LinkTargets.Document, toType);
        Assert.Equal(A, toId);
    }

    [Fact]
    public void Known_types_are_the_ones_the_endpoints_accept()
    {
        Assert.True(LinkTargets.IsKnown(LinkTargets.Asset));
        Assert.True(LinkTargets.IsKnown(LinkTargets.Attachment));
        Assert.True(LinkTargets.IsKnown(LinkTargets.Document));
        Assert.True(LinkTargets.IsKnown(LinkTargets.Task));
        Assert.False(LinkTargets.IsKnown("sprint"));
        Assert.False(LinkTargets.IsKnown(null));
    }
}
