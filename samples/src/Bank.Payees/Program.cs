using Bank.Payees;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// B5 stage 7: payees, rewritten as a service of its own, with its own database. It offers the
// same API as the monolith's payees module. While it's introduced, it follows the module's
// change feed (Payees:FollowUrl) to build its copy and keep it current; once it owns the
// writes, it publishes its own changes, which the module can follow back.

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Payees")!;
builder.Services.AddDbContext<PayeesServiceDb>(options => options.UseNpgsql(connection).UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PayeeStore>();
builder.Services.AddSingleton(new ListOrder(builder.Configuration["Payees:ListOrder"] ?? "AddedAt"));
builder.Services.AddHttpClient(PayeeFollower.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(5));
if (builder.Configuration["Payees:FollowUrl"] is { } follow)
{
    builder.Services.AddSingleton(new FollowOptions(new Uri(follow), TimeSpan.FromMilliseconds(builder.Configuration.GetValue("Payees:FollowIntervalMs", 200))));
    builder.Services.AddHostedService<PayeeFollower>();
}

var app = builder.Build();
await PrepareAsync(app.Services, connection);

app.MapGet("/accounts/{accountId:int}/payees", async (int accountId, PayeeStore store, CancellationToken ct) =>
    Results.Ok(await store.ListAsync(accountId, ct)));

app.MapGet("/accounts/{accountId:int}/payees/{payeeId:guid}", async (int accountId, Guid payeeId, PayeeStore store, CancellationToken ct) =>
    await store.GetAsync(accountId, payeeId, ct) is { } payee ? Results.Ok(payee) : Results.NotFound());

app.MapPost("/accounts/{accountId:int}/payees", async (int accountId, AddPayeeRequest request, PayeeStore store, CancellationToken ct) =>
{
    if (request.PayeeAccountId == accountId) return Results.Problem(statusCode: 400, title: "You can't add yourself as a payee.");
    var payee = await store.AddAsync(accountId, request, ct);
    return payee is null
        ? Results.Problem(statusCode: 409, title: "That account is already one of your payees.")
        : Results.Created($"/accounts/{accountId}/payees/{payee.PayeeId}", payee);
});

app.MapDelete("/accounts/{accountId:int}/payees/{payeeId:guid}", async (int accountId, Guid payeeId, PayeeStore store, CancellationToken ct) =>
    await store.RemoveAsync(accountId, payeeId, ct) ? Results.NoContent() : Results.NotFound());

app.MapGet("/payee-changes", async (long after, int? max, PayeesServiceDb db, TimeProvider time, CancellationToken ct) =>
    Results.Ok(await PayeeFeed.ReadAsync(db, after, Math.Clamp(max ?? 500, 1, 5000), time.GetUtcNow(), ct)));
app.MapGet("/payee-changes/head", async (PayeesServiceDb db, CancellationToken ct) =>
    Results.Ok(new { position = await db.PayeeChanges.MaxAsync(c => (long?)c.Position, ct) ?? 0 }));
app.MapGet("/payees/following", async (PayeesServiceDb db, CancellationToken ct) =>
    Results.Ok(await db.FollowPositions.AsNoTracking().ToListAsync(ct)));

// #region big-bang-import
// For the big-bang lab: copy every payee the monolith has, as of now, in one go.
app.MapPost("/admin/import", async (string from, IHttpClientFactory clients, PayeesServiceDb db, CancellationToken ct) =>
{
    var started = System.Diagnostics.Stopwatch.StartNew();
    var all = await clients.CreateClient(PayeeFollower.HttpClientName).GetFromJsonAsync<List<Payee>>(new Uri(new Uri(from), "/payees/export"), ct) ?? [];
    foreach (var batch in all.Chunk(500))
    {
        foreach (var p in batch)
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO payees (payee_id, customer_account_id, payee_account_id, name, reference, added_at)
                VALUES ({p.PayeeId}, {p.CustomerAccountId}, {p.PayeeAccountId}, {p.Name}, {p.Reference}, {p.AddedAt})
                ON CONFLICT DO NOTHING
                """, ct);
    }
    return Results.Ok(new { imported = all.Count, ms = started.ElapsedMilliseconds });
});
// #endregion big-bang-import

app.MapGet("/health", () => Results.Ok());

app.Run();

/// <summary>Creates the service's own database if it doesn't exist, then applies its migrations.</summary>
static async Task PrepareAsync(IServiceProvider services, string connection)
{
    var target = new NpgsqlConnectionStringBuilder(connection);
    var admin = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres" };
    await using (var server = new NpgsqlConnection(admin.ConnectionString))
    {
        await server.OpenAsync();
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", server);
        exists.Parameters.AddWithValue("name", target.Database!);
        if (await exists.ExecuteScalarAsync() is null)
        {
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{target.Database}\"", server);
            await create.ExecuteNonQueryAsync();
        }
    }
    await using var scope = services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PayeesServiceDb>().Database.MigrateAsync();
}

public partial class Program;
