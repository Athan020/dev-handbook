using Bank.Modular.Contracts.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Modular.Statements;

/// <summary>The statements module: an account's monthly statement. Its only public type.</summary>
public static class StatementsModule
{
    public const string Schema = "statements";

    public static IServiceCollection AddStatementsModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<StatementBuilder>();
        return services;
    }

    public static IEndpointRouteBuilder MapStatements(this IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{accountId:int}/statements/{year:int}/{month:int}",
            async (int accountId, int year, int month, StatementBuilder builder, CancellationToken ct) =>
                month is < 1 or > 12
                    ? Results.Problem(statusCode: 400, title: "The month must be 1 to 12.")
                    : Results.Ok(await builder.BuildAsync(accountId, year, month, ct)));
        return app;
    }

    public static Task PrepareAsync(IServiceProvider services, CancellationToken ct) => Task.CompletedTask;
}

internal sealed record StatementLine(DateTimeOffset PostedAt, string Reference, decimal Amount);

internal sealed record Statement(int AccountId, int Year, int Month, decimal MoneyIn, decimal MoneyOut, IReadOnlyList<StatementLine> Lines);

/// <summary>Builds a statement from the ledger's entries, asked for through its contract.</summary>
internal sealed class StatementBuilder(ILedger ledger)
{
    public async Task<Statement> BuildAsync(int accountId, int year, int month, CancellationToken ct)
    {
        var from = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var entries = await ledger.EntriesAsync(accountId, from, from.AddMonths(1), ct);
        var lines = entries.Select(e => new StatementLine(e.PostedAt, e.Reference, e.Amount)).ToList();
        return new Statement(accountId, year, month,
            MoneyIn: lines.Where(l => l.Amount > 0).Sum(l => l.Amount),
            MoneyOut: -lines.Where(l => l.Amount < 0).Sum(l => l.Amount),
            lines);
    }
}
