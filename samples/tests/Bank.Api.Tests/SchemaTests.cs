using Bank.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bank.Api.Tests;

/// <summary>
/// The lab database comes from EF migrations; the handbook's in-browser SQL playground
/// builds its database from Data/Seed/schema.sql. These tests keep all three in step
/// with the model.
/// </summary>
public sealed class SchemaTests(BankApiFixture fixture) : IClassFixture<BankApiFixture>
{
    [Fact]
    public void Migrations_are_up_to_date_with_the_model()
    {
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();

        Assert.False(db.Database.HasPendingModelChanges(),
            "The model has changed since the last migration. Run `dotnet ef migrations add <Name> --project samples/src/Bank.Api --output-dir Data/Migrations`.");
    }

    [Fact]
    public void Playground_schema_matches_the_model()
    {
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDb>();

        using var stream = typeof(BankDb).Assembly.GetManifestResourceStream("Bank.Api.Data.Seed.schema.sql")!;
        var committed = new StreamReader(stream).ReadToEnd();

        Assert.True(Normalise(db.Database.GenerateCreateScript()) == Normalise(committed),
            "Data/Seed/schema.sql is out of date. Run `dotnet run --project samples/src/Bank.Api -- schema`.");
    }

    private static string Normalise(string sql) => sql.ReplaceLineEndings("\n").Trim();
}
