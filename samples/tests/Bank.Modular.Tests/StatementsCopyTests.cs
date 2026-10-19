using System.Net.Http.Json;

namespace Bank.Modular.Tests;

public class StatementsCopyTests(ModularFixture fixture) : IClassFixture<ModularFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Line(Guid TransferId, decimal Amount);
    private sealed record CopiedStatement(DateTimeOffset AsOf, List<Line> Lines);
    private sealed record Created(Guid TransferId);

    [Fact]
    public async Task A_transfer_reaches_the_copy_and_the_statement_says_how_fresh_it_is()
    {
        await using var app = fixture.App.WithWebHostBuilder(host => host
            .UseSetting("Lab:Statements", "Copy")
            .UseSetting("Statements:FeedIntervalMs", "50"));
        using var http = app.CreateClient();
        var payee = await http.FirstPayeeAsync(12);
        var sentAt = DateTimeOffset.UtcNow;

        var created = await (await http.PayAsync(12, payee.PayeeId, 3m)).Content.ReadFromJsonAsync<Created>(Ct);

        CopiedStatement? statement = null;
        for (var attempt = 0; attempt < 100 && statement?.Lines.Any(l => l.TransferId == created!.TransferId) != true; attempt++)
        {
            await Task.Delay(50, Ct);
            statement = await http.GetFromJsonAsync<CopiedStatement>($"/accounts/12/statements/{sentAt.Year}/{sentAt.Month}", Ct);
        }

        Assert.Contains(statement!.Lines, l => l.TransferId == created!.TransferId && l.Amount == -3m);
        Assert.True(statement.AsOf >= sentAt);
    }
}
