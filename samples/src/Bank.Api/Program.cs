using Polly;
using Bank.Api.Data;
using Bank.Api.Endpoints;
using Bank.Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BankDb>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Bank"))
    // snake_case tables and columns (account_id, not "AccountId"), as is usual in Postgres.
    .UseSnakeCaseNamingConvention());

// The read replica (B1 stage 6). Only used when Lab:Reads asks for it.
builder.Services.AddSingleton(new ReplicaOptions(new DbContextOptionsBuilder<BankDb>()
    .UseNpgsql(builder.Configuration.GetConnectionString("BankReplica"))
    .UseSnakeCaseNamingConvention()
    .Options));
builder.Services.AddScoped<ReadRouting>();

// Journey B2: follow-up work after a transfer (email, fraud score), and where it can run.
builder.Services.AddHttpClient<FollowUp>(http =>
    http.BaseAddress = new Uri(builder.Configuration["FakeServices:Url"] ?? "http://localhost:5090"));
builder.Services.AddSingleton<BackgroundFollowUpQueue>();
builder.Services.AddHostedService<BackgroundFollowUpService>();
builder.Services.AddSingleton<RabbitPublisher>();

// Journey B3 stage 6: the payment network, and the resolver for payments with unknown outcomes.
builder.Services.AddHttpClient<NetworkClient>(http =>
{
    http.BaseAddress = new Uri(builder.Configuration["FakeServices:Url"] ?? "http://localhost:5090");
    http.Timeout = TimeSpan.FromMilliseconds(builder.Configuration.GetValue("Network:TimeoutMs", 1000));
});
builder.Services.AddHostedService<PendingPaymentResolver>();

// Journey B4: the fraud gate on transfers, and an optional deadline for every request.
builder.Services.AddFraudGate(builder.Configuration);

// #region shared-pool
// B4 stage 4: Lab:SharedOutboundPool sends email and fraud calls through one connection pool
// with a fixed number of connections, as a shared client or an egress gateway would. Slow
// emails can then hold every connection while fraud checks wait for one.
var sharedPool = builder.Configuration.GetValue("Lab:SharedOutboundPool", 0);
if (sharedPool > 0)
{
    var shared = new SocketsHttpHandler { MaxConnectionsPerServer = sharedPool };
    builder.Services.AddHttpClient<FollowUp>().ConfigurePrimaryHttpMessageHandler(() => shared).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
    builder.Services.AddHttpClient<FraudClient>().ConfigurePrimaryHttpMessageHandler(() => shared).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
}
// #endregion shared-pool

// #region follow-up-bulkhead
// B4 stage 4: a bulkhead for the background follow-up work. At most Lab:FollowUpMaxConcurrent
// email and scoring calls at once; the rest wait their turn, and can't take the connections
// a transfer's fraud check needs.
var followUpLimit = builder.Configuration.GetValue("Lab:FollowUpMaxConcurrent", 0);
if (followUpLimit > 0)
{
    builder.Services.AddHttpClient<FollowUp>().AddResilienceHandler("follow-up-bulkhead", pipeline =>
        pipeline.AddConcurrencyLimiter(followUpLimit, queueLimit: 100_000));
}
// #endregion follow-up-bulkhead
var requestTimeoutMs = builder.Configuration.GetValue("Lab:RequestTimeoutMs", 0);
if (requestTimeoutMs > 0)
{
    // #region request-timeouts
    builder.Services.AddRequestTimeouts(options => options.DefaultPolicy =
        new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy { Timeout = TimeSpan.FromMilliseconds(requestTimeoutMs) });
    // #endregion request-timeouts
}
builder.Services.AddHostedService<OutboxRelay>();
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
// #region request-deadline
// B4 stage 1: with Lab:RequestTimeoutMs, every request has a deadline. When it passes, the
// request's cancellation token fires, and every call made with that token stops waiting.
if (requestTimeoutMs > 0)
{
    app.UseRequestTimeouts();
}
// #endregion request-deadline

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapGet("/resilience/fraud", (FraudStats stats) => stats.Snapshot());
app.MapAccountEndpoints();
app.MapSummaryEndpoints();
app.MapTransferEndpoints();
app.MapOutgoingPaymentEndpoints();
app.MapReconciliationEndpoints();

app.Run();

// Lets the integration tests start the app with WebApplicationFactory<Program>.
public partial class Program;
