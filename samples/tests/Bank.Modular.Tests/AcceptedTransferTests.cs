using System.Net;
using System.Net.Http.Json;

namespace Bank.Modular.Tests;

public class AcceptedTransferTests(ModularFixture fixture) : IClassFixture<ModularFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Accepted(Guid TransferId, string Status);
    private sealed record RequestStatus(Guid TransferId, string Status, string? Reason);

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> AcceptingApp() =>
        fixture.App.WithWebHostBuilder(host => host.UseSetting("Lab:Transfers", "Accept"));

    private static async Task<RequestStatus> FinishedAsync(HttpClient http, Guid transferId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var status = await http.GetFromJsonAsync<RequestStatus>($"/transfers/{transferId}", Ct);
            if (status!.Status != "Pending") return status;
            await Task.Delay(50, Ct);
        }
        throw new TimeoutException("The transfer was still pending after five seconds.");
    }

    [Fact]
    public async Task An_accepted_transfer_is_made_once_afterwards()
    {
        await using var app = AcceptingApp();
        using var http = app.CreateClient();
        var payee = await http.FirstPayeeAsync(33);
        var (from, to) = (await http.BalanceAsync(33), await http.BalanceAsync(payee.PayeeAccountId));

        var response = await http.PayAsync(33, payee.PayeeId, 9m);
        var accepted = await response.Content.ReadFromJsonAsync<Accepted>(Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Pending", accepted!.Status);
        Assert.Equal("Completed", (await FinishedAsync(http, accepted.TransferId)).Status);
        Assert.Equal(from - 9m, await http.BalanceAsync(33));
        Assert.Equal(to + 9m, await http.BalanceAsync(payee.PayeeAccountId));
    }

    [Fact]
    public async Task An_accepted_transfer_to_someone_who_isnt_a_payee_is_rejected()
    {
        await using var app = AcceptingApp();
        using var http = app.CreateClient();
        var someoneElses = await http.FirstPayeeAsync(35);

        var response = await http.PayAsync(36, someoneElses.PayeeId, 1m);
        var accepted = await response.Content.ReadFromJsonAsync<Accepted>(Ct);
        var finished = await FinishedAsync(http, accepted!.TransferId);

        Assert.Equal("Rejected", finished.Status);
        Assert.Equal("No such payee for this account.", finished.Reason);
    }
}
