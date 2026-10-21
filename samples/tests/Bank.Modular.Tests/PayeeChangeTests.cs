using System.Net.Http.Json;

namespace Bank.Modular.Tests;

public class PayeeChangeTests(ModularFixture fixture) : IClassFixture<ModularFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Change(long Position, string Kind, Guid PayeeId, int CustomerAccountId, int PayeeAccountId);
    private sealed record Head(long Position);

    [Fact]
    public async Task Every_seeded_payee_is_in_the_change_feed()
    {
        using var http = fixture.App.CreateClient();

        var feed = await http.GetFromJsonAsync<List<Change>>("/payee-changes?after=0&max=5000", Ct);

        Assert.True(feed!.Count(c => c.Kind == "Added") >= ModularFixture.Accounts * 3);
    }

    [Fact]
    public async Task Adding_and_removing_a_payee_are_both_in_the_feed_in_order()
    {
        using var http = fixture.App.CreateClient();
        var head = (await http.GetFromJsonAsync<Head>("/payee-changes/head", Ct))!.Position;

        var added = await (await http.PostAsJsonAsync("/accounts/44/payees", new { payeeAccountId = 2, name = "Plumber" }, Ct))
            .Content.ReadFromJsonAsync<PayeeDto>(Ct);
        await http.DeleteAsync($"/accounts/44/payees/{added!.PayeeId}", Ct);
        await Task.Delay(1100, Ct); // past any gap left by other tests' failed adds

        var feed = await http.GetFromJsonAsync<List<Change>>($"/payee-changes?after={head}", Ct);
        var ours = feed!.Where(c => c.PayeeId == added.PayeeId).Select(c => c.Kind);
        Assert.Equal(["Added", "Removed"], ours);
    }
}
