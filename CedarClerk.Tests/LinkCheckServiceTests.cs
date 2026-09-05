using System.Collections.Concurrent;
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

    // The handler never completes on its own, so the only way the probe returns is the per-link
    // cap firing; that the cancellation reached the handler is the proof, not a stopwatch — the
    // wall-clock version of this test raced the cap under a loaded runner (T-364).
    [Fact]
    public async Task A_hanging_link_is_cut_off_by_the_cap_and_reports_unreachable()
    {
        var cancelled = 0;
        var handler = new StubHandler((_, ct) =>
        {
            var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() =>
            {
                Interlocked.Increment(ref cancelled);
                completion.TrySetCanceled(ct);
            });
            return completion.Task;
        });
        var service = new LinkCheckService(new HttpClient(handler), PublicResolver, TimeSpan.FromMilliseconds(200));

        var dead = await service
            .CheckAsync(["https://tarpit.example/a", "https://tarpit.example/b"])
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(2, dead.Count);
        Assert.All(dead, d => Assert.Equal(LinkCheckService.Unreachable, d.Status));
        Assert.Equal(2, cancelled);
    }

    private static Task<IPAddress[]> PublicResolver(string host, CancellationToken ct) =>
        Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });

    private static HttpResponseMessage RedirectTo(string location, HttpStatusCode status = HttpStatusCode.Found) =>
        new(status) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    [Fact]
    public async Task A_redirect_into_private_space_is_blocked_and_not_followed()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(
            request.RequestUri!.Host == "public.example"
                ? RedirectTo("http://10.0.0.5/admin")
                : new HttpResponseMessage(HttpStatusCode.OK)));

        var dead = Assert.Single(await Service(handler).CheckAsync(["https://public.example/"]));

        Assert.Equal(LinkCheckService.Blocked, dead.Status);
        Assert.Equal("https://public.example/", dead.Url);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_redirect_hop_whose_name_resolves_inside_is_blocked()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(
            request.RequestUri!.Host == "public.example"
                ? RedirectTo("https://intranet.example/")
                : new HttpResponseMessage(HttpStatusCode.OK)));
        var service = new LinkCheckService(new HttpClient(handler), (host, _) => Task.FromResult(new[]
        {
            IPAddress.Parse(host == "intranet.example" ? "192.168.1.10" : "93.184.216.34"),
        }));

        var dead = Assert.Single(await service.CheckAsync(["https://public.example/"]));

        Assert.Equal(LinkCheckService.Blocked, dead.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_redirect_chain_is_followed_to_its_verdict()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(request.RequestUri!.ToString() switch
        {
            "https://a.example/" => RedirectTo("https://b.example/moved", HttpStatusCode.MovedPermanently),
            "https://b.example/moved" => RedirectTo("/final", HttpStatusCode.PermanentRedirect),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }));

        var dead = Assert.Single(await Service(handler).CheckAsync(["https://a.example/"]));

        Assert.Equal("404", dead.Status);
        Assert.Equal("https://a.example/", dead.Url);
        Assert.Equal(
            ["https://a.example/", "https://b.example/moved", "https://b.example/final"],
            handler.Requests.Select(r => r.RequestUri!.ToString()));
    }

    [Fact]
    public async Task A_redirect_loop_stops_at_the_hop_cap_and_reports_unreachable()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(RedirectTo(request.RequestUri!.ToString() + "x")));

        var dead = Assert.Single(await Service(handler).CheckAsync(["https://loop.example/"]));

        Assert.Equal(LinkCheckService.Unreachable, dead.Status);
        Assert.Equal(LinkCheckService.MaxRedirects + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task A_redirect_off_http_is_unreachable_rather_than_followed()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(RedirectTo("file:///etc/passwd")));

        var dead = Assert.Single(await Service(handler).CheckAsync(["https://odd.example/"]));

        Assert.Equal(LinkCheckService.Unreachable, dead.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_3xx_without_a_location_is_a_healthy_answer()
    {
        var service = Service(Answering(HttpStatusCode.NotModified));

        Assert.Empty(await service.CheckAsync(["https://cached.example/"]));
    }

    [Fact]
    public async Task Every_request_is_pinned_to_the_address_that_passed_the_check()
    {
        var resolved = new ConcurrentDictionary<string, int>();
        var handler = new StubHandler((request, _) => Task.FromResult(
            request.RequestUri!.Host == "a.example"
                ? RedirectTo("https://b.example/")
                : new HttpResponseMessage(HttpStatusCode.OK)));
        var service = new LinkCheckService(new HttpClient(handler), (host, _) =>
        {
            resolved.AddOrUpdate(host, 1, (_, n) => n + 1);
            return Task.FromResult(new[] { IPAddress.Parse(host == "a.example" ? "93.184.216.34" : "203.0.113.7") });
        });

        Assert.Empty(await service.CheckAsync(["https://a.example/"]));

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(handler.Requests[0].Options.TryGetValue(LinkCheckService.PinnedAddress, out var first));
        Assert.True(handler.Requests[1].Options.TryGetValue(LinkCheckService.PinnedAddress, out var second));
        Assert.Equal(IPAddress.Parse("93.184.216.34"), first);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), second);
        Assert.Equal(1, resolved["a.example"]);
        Assert.Equal(1, resolved["b.example"]);
    }

    [Fact]
    public async Task A_literal_address_is_pinned_as_spelled()
    {
        var handler = Answering(HttpStatusCode.OK);

        Assert.Empty(await Service(handler).CheckAsync(["http://93.184.216.34/"]));

        Assert.True(handler.Requests.Single().Options.TryGetValue(LinkCheckService.PinnedAddress, out var pinned));
        Assert.Equal(IPAddress.Parse("93.184.216.34"), pinned);
    }

    [Fact]
    public void The_production_handler_never_follows_redirects_on_its_own()
    {
        using var handler = LinkCheckService.CreateHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.NotNull(handler.ConnectCallback);
    }

    // The request names a host that no resolver answers for; the only way it can reach the
    // listener is through the pinned address, which is the whole point of the connect callback.
    [Fact]
    public async Task The_production_handler_dials_the_pinned_address_not_the_host_name()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync();
            var buffer = new byte[4096];
            var read = await socket.ReceiveAsync(buffer, System.Net.Sockets.SocketFlags.None);
            var requestLine = System.Text.Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0];
            await socket.SendAsync(System.Text.Encoding.ASCII.GetBytes(
                "HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n"), System.Net.Sockets.SocketFlags.None);
            return requestLine;
        });

        using var http = new HttpClient(LinkCheckService.CreateHandler());
        var request = new HttpRequestMessage(HttpMethod.Head, $"http://pinned.invalid:{port}/probe");
        request.Options.Set(LinkCheckService.PinnedAddress, IPAddress.Loopback);
        using var response = await http.SendAsync(request).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("HEAD /probe HTTP/1.1", await serve.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task The_production_handler_refuses_a_request_without_a_pin()
    {
        using var http = new HttpClient(LinkCheckService.CreateHandler());

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            http.SendAsync(new HttpRequestMessage(HttpMethod.Head, "http://unpinned.invalid/")));
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
