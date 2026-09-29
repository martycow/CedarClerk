using System.Net;
using CedarClerk.Server;

namespace CedarClerk.Tests;

public class AccountSecurityTests
{
    [Theory]
    [InlineData("{\"data\":[],\"has_more\":false}", true)]
    [InlineData("{\"data\":[{\"status\":\"active\"}],\"has_more\":false}", false)]
    [InlineData("{\"data\":[],\"has_more\":true}", false)]
    [InlineData("{}", false)]
    [InlineData("invalid", false)]
    public async Task Deletion_requires_confirmed_closed_billing(string json, bool expected)
    {
        using var client = new HttpClient(new Reply(json));
        Assert.Equal(expected, await AccountSecurityEndpoints.BillingClosedAsync("customer", "test-key", client));
    }

    [Fact]
    public async Task Billing_failure_cannot_authorize_deletion()
    {
        using var client = new HttpClient(new Reply("{}", HttpStatusCode.ServiceUnavailable));
        Assert.False(await AccountSecurityEndpoints.BillingClosedAsync("customer", "test-key", client));
        Assert.False(await AccountSecurityEndpoints.BillingClosedAsync("customer", null, client));
        Assert.True(await AccountSecurityEndpoints.BillingClosedAsync(null, null, client));
    }

    private sealed class Reply(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.stripe.com", request.RequestUri!.Host);
            Assert.Contains("customer=customer", request.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
        }
    }
}
