using Bank.Api.Data;
using Bank.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BankDb>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Bank")));
builder.Services.AddProblemDetails();

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

app.UseExceptionHandler();
app.MapGet("/health", () => Results.Ok("healthy"));
app.MapAccountEndpoints();

app.Run();

// Lets the integration tests start the app with WebApplicationFactory<Program>.
public partial class Program;
