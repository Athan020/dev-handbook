using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Bank.Gateway.Tests;

/// <summary>
/// One small web server stands in for the ledger and payees services, recording every request
/// it receives. The statements service points at a closed port, so it's always down.
/// </summary>
public sealed class GatewayFixture : IAsyncLifetime
{
    private WebApplication? _services;

    public ConcurrentQueue<(string Path, string? Customer)> Received { get; } = new();
    public WebApplicationFactory<Program> Gateway { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _services = builder.Build();
        _services.Use(async (context, next) =>
        {
            Received.Enqueue((context.Request.Path, context.Request.Headers["X-Customer-Account"].FirstOrDefault()));
            await next();
        });
        _services.MapGet("/accounts/{id}", (int id) => new { accountId = id, balance = 123.45m });
        _services.MapGet("/accounts/{id}/payees", (int id) => new[] { new { payeeId = Guid.NewGuid(), name = "Landlord" } });
        _services.MapGet("/accounts/{id}/card-top-ups", (int id) => Array.Empty<object>());
        await _services.StartAsync();
        var address = _services.Urls.Single();

        Gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("ReverseProxy:Clusters:ledger:Destinations:main:Address", address)
            .UseSetting("ReverseProxy:Clusters:payees:Destinations:main:Address", address)
            .UseSetting("ReverseProxy:Clusters:statements:Destinations:main:Address", "http://127.0.0.1:9"));
    }

    public async Task<HttpClient> ClientForAsync(int accountId)
    {
        var http = Gateway.CreateClient();
        using var issued = await http.PostAsync($"/lab/token?accountId={accountId}", null, TestContext.Current.CancellationToken);
        var token = (await issued.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Trim('"');
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    public async ValueTask DisposeAsync()
    {
        await Gateway.DisposeAsync();
        if (_services is not null) await _services.DisposeAsync();
    }
}

public class GatewayTests(GatewayFixture fixture) : IClassFixture<GatewayFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_request_without_a_token_never_reaches_a_service()
    {
        using var http = fixture.Gateway.CreateClient();
        var before = fixture.Received.Count;

        var response = await http.GetAsync("/accounts/1/payees", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, fixture.Received.Count);
    }

    [Fact]
    public async Task The_service_receives_the_verified_identity_not_the_one_the_client_sent()
    {
        using var http = await fixture.ClientForAsync(7);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/accounts/7/payees");
        request.Headers.Add("X-Customer-Account", "8"); // an attempt to pass as someone else

        var response = await http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(fixture.Received, r => r.Path == "/accounts/7/payees" && r.Customer == "7");
        Assert.DoesNotContain(fixture.Received, r => r.Customer == "8");
    }

    [Fact]
    public async Task A_customer_cannot_reach_another_customers_account()
    {
        using var http = await fixture.ClientForAsync(7);

        var response = await http.GetAsync("/accounts/9", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(fixture.Received, r => r.Path == "/accounts/9");
    }

    private sealed record HomeScreen(decimal? Balance, List<string> Unavailable);

    [Fact]
    public async Task The_home_screen_shows_what_it_can_when_a_service_is_down()
    {
        using var http = await fixture.ClientForAsync(7);

        var home = await http.GetFromJsonAsync<HomeScreen>("/app/home", Ct);

        Assert.Equal(123.45m, home!.Balance);
        Assert.Equal(["recent"], home.Unavailable);
    }
}
