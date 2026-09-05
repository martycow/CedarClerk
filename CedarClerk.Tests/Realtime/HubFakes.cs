using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace CedarClerk.Tests.Realtime;

// Hand-written rather than a mocking library: a hub method touches three abstractions and the
// tests need to know what was sent where, which a recording fake states in one line each.

internal sealed class FakeHubCallerContext(string connectionId, string userId) : HubCallerContext
{
    public override string ConnectionId => connectionId;
    public override string? UserIdentifier => userId;
    public override ClaimsPrincipal? User { get; } =
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted => CancellationToken.None;
    public override void Abort() { }
}

internal sealed class FakeGroupManager : IGroupManager
{
    public List<(string ConnectionId, string Group)> Added { get; } = [];
    public List<(string ConnectionId, string Group)> Removed { get; } = [];

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
    {
        Added.Add((connectionId, groupName));
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
    {
        Removed.Add((connectionId, groupName));
        return Task.CompletedTask;
    }
}

/// <summary>One message the hub sent: to whom (as the hub named it), which client method, with what.</summary>
internal sealed record Sent(string To, string Method, object?[] Args);

internal sealed class FakeHubCallerClients : IHubCallerClients
{
    public List<Sent> Messages { get; } = [];

    public IEnumerable<Sent> Of(string method) => Messages.Where(m => m.Method == method);

    private Proxy To(string target) => new(target, Messages);

    public ISingleClientProxy Caller => To("caller");
    public IClientProxy Others => To("others");
    public IClientProxy OthersInGroup(string groupName) => To("others-in-group:" + groupName);
    public IClientProxy All => To("all");
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => To("all-except");
    public ISingleClientProxy Client(string connectionId) => To("client:" + connectionId);
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => To("clients:" + string.Join(",", connectionIds));
    public IClientProxy Group(string groupName) => To("group:" + groupName);
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => To("groups:" + string.Join(",", groupNames));
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => To("group-except:" + groupName);
    public IClientProxy User(string userId) => To("user:" + userId);
    public IClientProxy Users(IReadOnlyList<string> userIds) => To("users:" + string.Join(",", userIds));

    IClientProxy IHubCallerClients<IClientProxy>.Caller => Caller;
    IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => Client(connectionId);

    private sealed class Proxy(string target, List<Sent> sink) : ISingleClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default)
        {
            sink.Add(new Sent(target, method, args));
            return Task.CompletedTask;
        }

        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken ct) =>
            throw new NotSupportedException("The canvas hub never asks a client for a result.");
    }
}
