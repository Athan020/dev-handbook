using Bank.Api.Data;
using Bank.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BankDb>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Bank"))
    // snake_case tables and columns (account_id, not "AccountId"), as is usual in Postgres.
    .UseSnakeCaseNamingConvention());
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();
builder.Services.AddHybridCache();

if (args is ["seed", ..])
{
    // On a fresh database EF logs a failed query while checking for migration history,
    // then carries on. Keep seeding output readable; real failures still throw.
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
}

var app = builder.Build();

// `dotnet run -- seed [--accounts N] [--transactions N]` creates the schema and fills it.
if (args is ["seed", ..])
{
    var defaults = new Seeder.Options();
    var options = defaults with
    {
        Accounts = app.Configuration.GetValue("accounts", defaults.Accounts),
        Transactions = app.Configuration.GetValue("transactions", defaults.Transactions),
    };

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<BankDb>();
    db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
    await Seeder.SeedAsync(db, options, Console.Out);
    return;
}

// `dotnet run -- schema` writes the schema as plain SQL to Data/Seed/schema.sql. The
// handbook's in-browser SQL playground builds its database from that file, and a test
// checks it still matches the model.
if (args is ["schema"])
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<BankDb>();
    var path = Path.Combine(app.Environment.ContentRootPath, "Data", "Seed", "schema.sql");
    await File.WriteAllTextAsync(path, db.Database.GenerateCreateScript());
    Console.WriteLine($"Wrote {path}");
    return;
}

app.UseExceptionHandler();
app.MapGet("/health", () => Results.Ok("healthy"));
app.MapAccountEndpoints();
app.MapSummaryEndpoints();

app.Run();

// Lets the integration tests start the app with WebApplicationFactory<Program>.
public partial class Program;
