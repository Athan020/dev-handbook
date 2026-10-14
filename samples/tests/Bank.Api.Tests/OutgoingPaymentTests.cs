using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Bank.Api.Endpoints;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Api.Tests;

/// <summary>B3 stage 6: a payment to another bank whose outcome isn't known straight away.</summary>
public sealed class OutgoingPaymentTests(BankApiFixture fixture) : IClassFixture<BankApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A stand-in payment network that settles payments, and can lose its reply or fail first.</summary>
    private sealed class StubNetwork(string behaviour) : HttpMessageHandler
    {
        public ConcurrentDictionary<string, int> Settled { get; } = new();
        private int _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
            {
                var reference = request.RequestUri!.Segments[^1];
                return new HttpResponseMessage(Settled.ContainsKey(reference) ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            }

            var body = await request.Content!.ReadFromJsonAsync<Dictionary<string, object>>(ct);
            var key = body!["reference"].ToString()!;
            var first = Interlocked.Increment(ref _calls) == 1;
            switch (behaviour)
            {
                case "reject":
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                case "fail-first" when first:
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); // nothing settled
            }
            Settled.AddOrUpdate(key, 1, (_, n) => n + 1);
            if (behaviour == "lose-first-reply" && first)
            {
                throw new TaskCanceledException("The reply never arrived."); // settled, but the caller times out
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    // Each test gets its own API, disposed at the end, so only its own resolver is running:
    // the resolver works on every pending payment in the shared database.
    private (Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Api, HttpClient Client, StubNetwork Network) Api(string mode, string behaviour)
    {
        var network = new StubNetwork(behaviour);
        var api = fixture.App.WithWebHostBuilder(host => host
            .UseSetting("Lab:Outgoing", mode)
            .UseSetting("Network:ResolveAfterSeconds", "0")
            .ConfigureTestServices(services =>
                services.AddHttpClient<NetworkClient>().ConfigurePrimaryHttpMessageHandler(() => network)));
        return (api, api.CreateClient(), network);
    }

    private static async Task<int> OpenWithAsync(HttpClient client, decimal deposit)
    {
        var opened = await client.PostAsJsonAsync("/accounts", new OpenAccountRequest("Pays other banks"), Ct);
        var id = (await opened.Content.ReadFromJsonAsync<AccountResponse>(Ct))!.Id;
        (await client.PostAsJsonAsync($"/accounts/{id}/transactions", new NewTransaction(deposit, "Deposit"), Ct)).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<decimal> BalanceAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<AccountSummary>($"/accounts/{id}/summary", Ct))!.Balance;

    private static async Task<string> WaitUntilResolvedAsync(HttpClient client, Uri location)
    {
        for (var i = 0; i < 40; i++)
        {
            var payment = await client.GetFromJsonAsync<OutgoingPaymentResponse>(location, Ct);
            if (payment!.Status != "Pending")
            {
                return payment.Status;
            }
            await Task.Delay(250, Ct);
        }
        return "Pending";
    }

    [Fact]
    public async Task Naive_refunds_a_payment_the_network_actually_made()
    {
        var (api, client, network) = Api("Naive", "lose-first-reply");
        await using var _ = api;
        var payer = await OpenWithAsync(client, 100m);

        var response = await client.PostAsJsonAsync("/outgoing-payments", new OutgoingPaymentRequest(payer, 30m, "Other Bank", "ACC1"), Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(100m, await BalanceAsync(client, payer)); // the customer got the money back...
        Assert.Single(network.Settled);                         // ...and the network paid it anyway
    }

    [Fact]
    public async Task Pending_resolves_a_lost_reply_by_asking_the_network()
    {
        var (api, client, network) = Api("Pending", "lose-first-reply");
        await using var _ = api;
        var payer = await OpenWithAsync(client, 100m);

        var response = await client.PostAsJsonAsync("/outgoing-payments", new OutgoingPaymentRequest(payer, 30m, "Other Bank", "ACC1"), Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Settled", await WaitUntilResolvedAsync(client, response.Headers.Location!));
        Assert.Equal(70m, await BalanceAsync(client, payer));
        Assert.Equal(1, Assert.Single(network.Settled).Value);
    }

    [Fact]
    public async Task Pending_sends_again_with_the_same_reference_when_the_network_has_no_record()
    {
        var (api, client, network) = Api("Pending", "fail-first");
        await using var _ = api;
        var payer = await OpenWithAsync(client, 100m);

        var response = await client.PostAsJsonAsync("/outgoing-payments", new OutgoingPaymentRequest(payer, 30m, "Other Bank", "ACC1"), Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Settled", await WaitUntilResolvedAsync(client, response.Headers.Location!));
        Assert.Equal(70m, await BalanceAsync(client, payer));
        Assert.Equal(1, Assert.Single(network.Settled).Value);
    }

    [Fact]
    public async Task Pending_refunds_a_payment_the_network_rejects()
    {
        var (api, client, network) = Api("Pending", "reject");
        await using var _ = api;
        var payer = await OpenWithAsync(client, 100m);

        var response = await client.PostAsJsonAsync("/outgoing-payments", new OutgoingPaymentRequest(payer, 30m, "Other Bank", "ACC1"), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(100m, await BalanceAsync(client, payer));
        Assert.Empty(network.Settled);
    }
}
