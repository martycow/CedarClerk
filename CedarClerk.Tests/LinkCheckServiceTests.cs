using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using CedarClerk.Server;

namespace CedarClerk.Tests;

// T-238. The probe is best-effort by contract: a dead site produces a warning, a hanging site
// produces a warning within the time cap, and neither is ever allowed to become an exception.
// The service runs on the droplet, whose loopback holds the whole app — so hosts that resolve
// into private space are refused before any request exists.
public class LinkCheckServiceTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add(request);
            return respond(request, ct);
        }
    }

    // Test hostnames don't exist in real DNS, so every fixture resolves them to a public address
    // unless the test is about the guard itself.
    private static LinkCheckService Service(StubHandler handler, string resolvedIp = "93.184.216.34") =>
        new(new HttpClient(handler), (_, _) => Task.FromResult(new[] { IPAddress.Parse(resolvedIp) }));

    private static StubHandler Answering(HttpStatusCode status) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)));

    [Fact]
    public async Task A_healthy_link_is_omitted()
    {
        var service = Service(Answering(HttpStatusCode.OK));

        Assert.Empty(await service.CheckAsync(["https://ok.example/"]));
    }

    [Fact]
    public async Task A_404_link_is_reported_with_its_status()
    {
        var service = Service(Answering(HttpStatusCode.NotFound));

        var dead = Assert.Single(await service.CheckAsync(["https://gone.example/"]));
        Assert.Equal("https://gone.example/", dead.Url);
        Assert.Equal("404", dead.Status);
    }

    [Fact]
    public async Task Head_refused_with_405_falls_back_to_get()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(new HttpResponseMessage(
            request.Method == HttpMethod.Head ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.OK)));

        Assert.Empty(await Service(handler).CheckAsync(["https://headless.example/"]));
        Assert.Equal([HttpMethod.Head, HttpMethod.Get], handler.Requests.Select(r => r.Method));
    }

    [Fact]
    public async Task A_transport_failure_reports_unreachable_rather_than_throwing()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));

        var dead = Assert.Single(await Service(handler).CheckAsync(["https://down.example/"]));
        Assert.Equal(LinkCheckService.Unreachable, dead.Status);
    }

    [Fact]
    public async Task A_hanging_link_times_out_within_the_cap_and_reports_unreachable()
    {
        var watch = Stopwatch.StartNew();
        var cancelledAt = new ConcurrentBag<TimeSpan>();
        var handler = new StubHandler((_, ct) =>
        {
            var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() =>
            {
                cancelledAt.Add(watch.Elapsed);
                completion.TrySetCanceled(ct);
            });
            return completion.Task;
        });

        var dead = await Service(handler)
            .CheckAsync(["https://tarpit.example/a", "https://tarpit.example/b"])
            .WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(2, dead.Count);
        Assert.All(dead, d => Assert.Equal(LinkCheckService.Unreachable, d.Status));
        Assert.Equal(2, cancelledAt.Count);
        // Measure the timer callback, not a continuation that a loaded test runner may schedule late.
        Assert.All(cancelledAt, elapsed =>
            Assert.True(elapsed < TimeSpan.FromSeconds(8), $"cancelled after {elapsed}"));
    }

    [Fact]
    public async Task Probes_at_most_ten_distinct_links()
    {
        var handler = Answering(HttpStatusCode.NotFound);
        var urls = Enumerable.Range(0, 15).Select(i => $"https://dead.example/{i}")
            .Concat(["https://dead.example/0"]).ToList();

        var dead = await Service(handler).CheckAsync(urls);

        Assert.Equal(LinkCheckService.MaxLinks, dead.Count);
        Assert.Equal(LinkCheckService.MaxLinks, handler.Requests.Count);
    }

    [Fact]
    public async Task A_url_no_request_can_be_built_from_is_unreachable()
    {
        var dead = Assert.Single(await Service(Answering(HttpStatusCode.OK)).CheckAsync(["http://"]));
        Assert.Equal(LinkCheckService.Unreachable, dead.Status);
    }

    [Theory]
    [InlineData("http://127.0.0.1/admin")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://172.16.8.1/")]
    [InlineData("http://192.168.1.10/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://0.0.0.0:8080/")]
    [InlineData("http://[::1]:8080/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://[fe80::1]/")]
    public async Task A_literal_internal_address_is_blocked_without_any_network_call(string url)
    {
        var handler = Answering(HttpStatusCode.OK);
        var resolverCalls = 0;
        var service = new LinkCheckService(new HttpClient(handler), (_, _) =>
        {
            resolverCalls++;
            return Task.FromResult(Array.Empty<IPAddress>());
        });

        var dead = Assert.Single(await service.CheckAsync([url]));

        Assert.Equal(LinkCheckService.Blocked, dead.Status);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, resolverCalls);
    }

    [Fact]
    public async Task A_hostname_resolving_into_private_space_is_blocked_before_the_request()
    {
        var handler = Answering(HttpStatusCode.OK);

        var dead = Assert.Single(await Service(handler, resolvedIp: "10.20.30.40")
            .CheckAsync(["https://internal.example/"]));

        Assert.Equal(LinkCheckService.Blocked, dead.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_hostname_resolving_to_a_public_address_is_probed_normally()
    {
        var handler = Answering(HttpStatusCode.NotFound);

        var dead = Assert.Single(await Service(handler).CheckAsync(["https://public.example/"]));

        Assert.Equal("404", dead.Status);
        Assert.Single(handler.Requests);
    }

    private sealed class TrackingContent : HttpContent
    {
        public bool Disposed;
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = 0; return true; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    [Fact]
    public async Task Both_responses_of_the_head_to_get_fallback_are_disposed()
    {
        var headContent = new TrackingContent();
        var getContent = new TrackingContent();
        var handler = new StubHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Head
            ? new HttpResponseMessage(HttpStatusCode.MethodNotAllowed) { Content = headContent }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = getContent }));

        Assert.Empty(await Service(handler).CheckAsync(["https://headless.example/"]));

        Assert.True(headContent.Disposed);
        Assert.True(getContent.Disposed);
    }
}
