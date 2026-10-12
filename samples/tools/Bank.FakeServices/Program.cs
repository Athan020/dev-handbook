// Stand-ins for the services a transfer talks to in journey B2: an email provider and a
// fraud-scoring service. Each call waits a while and can fail at random, so you can see what
// slow or unreliable dependencies do to the API. Change behaviour while it runs:
//
//   curl -X POST localhost:5090/settings -H "Content-Type: application/json" \
//        -d '{"emailDelayMs": 300, "fraudDelayMs": 500, "failureRate": 0.2}'
//
// GET /stats counts emails per transfer, so duplicates show up.
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
var app = builder.Build();

var settings = new FakeSettings(
    app.Configuration.GetValue("emailDelayMs", 300),
    app.Configuration.GetValue("fraudDelayMs", 500),
    app.Configuration.GetValue("failureRate", 0.0));
var emailsPerTransfer = new ConcurrentDictionary<Guid, int>();
var emailLagsMs = new ConcurrentQueue<double>(); // how long after the transfer each email went out
var fraudChecks = 0;

app.MapPost("/email", async (EmailRequest email, ILogger<Program> log, CancellationToken ct) =>
{
    var current = settings;
    await Task.Delay(current.EmailDelayMs, ct);
    if (Random.Shared.NextDouble() < current.FailureRate) return Results.StatusCode(503);

    var count = emailsPerTransfer.AddOrUpdate(email.TransferId, 1, (_, n) => n + 1);
    if (email.CompletedAt is { } completedAt) emailLagsMs.Enqueue((DateTimeOffset.UtcNow - completedAt).TotalMilliseconds);
    log.LogInformation("Email for transfer {TransferId} to {To}{Duplicate}", email.TransferId, email.To,
        count > 1 ? $" (copy {count}!)" : "");
    return Results.Ok();
});

app.MapPost("/fraud-check", async (CancellationToken ct) =>
{
    var current = settings;
    await Task.Delay(current.FraudDelayMs, ct);
    if (Random.Shared.NextDouble() < current.FailureRate) return Results.StatusCode(503);

    Interlocked.Increment(ref fraudChecks);
    return Results.Ok(new { score = Random.Shared.Next(0, 100) });
});

app.MapGet("/stats", () => new
{
    emails = emailsPerTransfer.Values.Sum(),
    transfersEmailed = emailsPerTransfer.Count,
    transfersEmailedMoreThanOnce = emailsPerTransfer.Count(e => e.Value > 1),
    fraudChecks,
    emailLagMs = Lags(emailLagsMs),
    settings,
});

app.MapPost("/settings", (FakeSettings next) =>
{
    settings = next;
    return Results.Ok(settings);
});

app.MapPost("/reset", () =>
{
    emailsPerTransfer.Clear();
    emailLagsMs.Clear();
    fraudChecks = 0;
    return Results.Ok();
});

app.Run();

static object Lags(IEnumerable<double> values)
{
    var sorted = values.Order().ToArray();
    double At(double p) => sorted.Length == 0 ? 0 : Math.Round(sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)]);
    return new { p50 = At(0.5), p99 = At(0.99), max = sorted.Length == 0 ? 0 : Math.Round(sorted[^1]) };
}

sealed record FakeSettings(int EmailDelayMs, int FraudDelayMs, double FailureRate);
sealed record EmailRequest(Guid TransferId, string To, string Subject, DateTimeOffset? CompletedAt);
