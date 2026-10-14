// Stand-ins for the services a transfer talks to in journey B2: an email provider and a
// fraud-scoring service. Each call waits a while and can fail at random, so you can see what
// slow or unreliable dependencies do to the API. Change behaviour while it runs:
//
//   curl -X POST localhost:5090/settings -H "Content-Type: application/json" \
//        -d '{"emailDelayMs": 300, "fraudDelayMs": 500, "failureRate": 0.2}'
//
// GET /stats counts emails per transfer, so duplicates show up.
//
// Journey B3 adds a payment network under /network: payments to other banks. It settles each
// payment it accepts, and can fail before doing anything, or settle and then answer too late
// for the caller to wait (a lost reply). Change its behaviour while it runs:
//
//   curl -X POST localhost:5090/network/settings -H "Content-Type: application/json" \
//        -d '{"lostReplyRate": 0.1, "failureRate": 0.05, "deduplicates": true}'
//
// GET /network/payments is its settlement record: every payment it made, which stage 7
// reconciles. amountErrorRate and missingFromRecordsRate add the network's own mistakes.
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
var app = builder.Build();

var settings = new FakeSettings(
    app.Configuration.GetValue("emailDelayMs", 300),
    app.Configuration.GetValue("fraudDelayMs", 500),
    app.Configuration.GetValue("failureRate", 0.0),
    app.Configuration.GetValue("maxConcurrentEmails", 0));
var emailsInFlight = 0;
var rateLimited = 0; // calls refused with 429 because too many were already in flight
var emailsPerTransfer = new ConcurrentDictionary<Guid, int>();
var emailLagsMs = new ConcurrentQueue<double>(); // how long after the transfer each email went out
var idempotencyKeys = new ConcurrentDictionary<string, bool>();
var duplicatesSuppressed = 0;
var fraudChecks = 0;
var failedCalls = 0; // calls answered with 503, to show how hard retries hit a struggling service

app.MapPost("/email", async (EmailRequest email, HttpRequest http, ILogger<Program> log, CancellationToken ct) =>
{
    var current = settings;
    // Like a real provider's rate limit: past a number of calls in flight, refuse with 429.
    if (Interlocked.Increment(ref emailsInFlight) > current.MaxConcurrentEmails && current.MaxConcurrentEmails > 0)
    {
        Interlocked.Decrement(ref emailsInFlight);
        Interlocked.Increment(ref rateLimited);
        return Results.StatusCode(429);
    }
    try
    {
        return await SendEmailAsync(email, http, log, current, ct);
    }
    finally
    {
        Interlocked.Decrement(ref emailsInFlight);
    }
});

async Task<IResult> SendEmailAsync(EmailRequest email, HttpRequest http, ILogger log, FakeSettings current, CancellationToken ct)
{
    await Task.Delay(current.EmailDelayMs, ct);
    if (Random.Shared.NextDouble() < current.FailureRate)
    {
        Interlocked.Increment(ref failedCalls);
        return Results.StatusCode(503);
    }

    // Like real email and payment APIs: a repeated Idempotency-Key gets the original result,
    // and no second email.
    if (http.Headers["Idempotency-Key"].ToString() is { Length: > 0 } key && !idempotencyKeys.TryAdd(key, true))
    {
        Interlocked.Increment(ref duplicatesSuppressed);
        return Results.Ok();
    }

    var count = emailsPerTransfer.AddOrUpdate(email.TransferId, 1, (_, n) => n + 1);
    if (email.CompletedAt is { } completedAt) emailLagsMs.Enqueue((DateTimeOffset.UtcNow - completedAt).TotalMilliseconds);
    log.LogInformation("Email for transfer {TransferId} to {To}{Duplicate}", email.TransferId, email.To,
        count > 1 ? $" (copy {count}!)" : "");
    return Results.Ok();
}

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
    duplicatesSuppressed,
    failedCalls,
    rateLimited,
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
    idempotencyKeys.Clear();
    duplicatesSuppressed = 0;
    failedCalls = 0;
    rateLimited = 0;
    fraudChecks = 0;
    return Results.Ok();
});

// #region network
var network = new NetworkSettings();
var settled = new ConcurrentDictionary<string, NetworkPayment>(); // by the sender's reference
var settlements = new ConcurrentQueue<NetworkPayment>();         // every settlement, duplicates included

app.MapPost("/network/payments", async (NetworkPaymentRequest request) =>
{
    var current = network;
    await Task.Delay(current.DelayMs);
    if (Random.Shared.NextDouble() < current.FailureRate)
    {
        return Results.StatusCode(503); // failed before doing anything
    }

    // A real network identifies a payment by the sender's reference, and won't pay it twice.
    if (current.Deduplicates && settled.TryGetValue(request.Reference, out var existing))
    {
        return Results.Ok(existing);
    }
    // B3 stage 7: the network's own mistakes. Sometimes it pays a different amount from the one
    // instructed, and sometimes a settlement never makes it into its records.
    var amount = Random.Shared.NextDouble() < current.AmountErrorRate ? request.Amount + 1.00m : request.Amount;
    var payment = new NetworkPayment(request.Reference, amount, request.ToBank, request.ToAccount, DateTimeOffset.UtcNow);
    settled[request.Reference] = payment;
    if (Random.Shared.NextDouble() >= current.MissingFromRecordsRate)
    {
        settlements.Enqueue(payment);
    }

    if (Random.Shared.NextDouble() < current.LostReplyRate)
    {
        await Task.Delay(current.LateReplyMs); // settled, but the answer arrives after the caller gave up
    }
    return Results.Ok(payment);
});

app.MapGet("/network/payments/{reference}", (string reference) =>
    settled.TryGetValue(reference, out var payment) ? Results.Ok(payment) : Results.NotFound());

app.MapGet("/network/payments", () => settlements.OrderBy(p => p.SettledAt));

app.MapGet("/network/stats", () => new
{
    settlements = settlements.Count,
    distinctReferences = settled.Count,
    amountSettled = settlements.Sum(p => p.Amount),
    settings = network,
});

app.MapPost("/network/settings", (NetworkSettings next) => network = next);

app.MapPost("/network/reset", () =>
{
    settled.Clear();
    settlements.Clear();
    return Results.Ok();
});
// #endregion network

app.Run();

static object Lags(IEnumerable<double> values)
{
    var sorted = values.Order().ToArray();
    double At(double p) => sorted.Length == 0 ? 0 : Math.Round(sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)]);
    return new { p50 = At(0.5), p99 = At(0.99), max = sorted.Length == 0 ? 0 : Math.Round(sorted[^1]) };
}

sealed record FakeSettings(int EmailDelayMs, int FraudDelayMs, double FailureRate, int MaxConcurrentEmails = 0);
sealed record EmailRequest(Guid TransferId, string To, string Subject, DateTimeOffset? CompletedAt);

sealed record NetworkSettings(
    int DelayMs = 50, double FailureRate = 0, double LostReplyRate = 0, int LateReplyMs = 3000, bool Deduplicates = true,
    double AmountErrorRate = 0, double MissingFromRecordsRate = 0);
sealed record NetworkPaymentRequest(string Reference, decimal Amount, string ToBank, string ToAccount);
sealed record NetworkPayment(string Reference, decimal Amount, string ToBank, string ToAccount, DateTimeOffset SettledAt);
