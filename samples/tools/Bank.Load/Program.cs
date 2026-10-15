// A small load generator for the labs: N concurrent workers request random accounts for a
// fixed time, then it reports throughput and latency percentiles. It's deliberately simple
// so you can read all of it. For real load tests, use a dedicated tool such as k6.
//
//   dotnet run --project tools/Bank.Load -- [--url http://localhost:5080] [--path "/accounts/{id}/transactions"]
//                                           [--concurrency 8] [--seconds 20] [--accounts 100000]
//                                           [--hot 0.8 --hot-accounts 100]
//
// --hot sends that share of requests to the busiest accounts (ids 1 to --hot-accounts),
// the way real traffic concentrates on a few popular keys.
//
// --transfers posts transfers between random accounts instead (journey B2).
//
// --ordering-burst N opens N accounts, and right after opening each one sends three transfers
// involving it, in parallel: the tightly packed events that B2 stage 5 needs. Then it exits.
//
// --payroll N posts exactly N salary transfers, --concurrency at a time, as fast as the API
// accepts them: the burst that B2 stage 7 needs. Then it exits. Add --employer to send them
// all from one newly opened account (B3 stage 2).
//
// --crossfire N sends N transfers between two new accounts, alternating direction (B3 stage 2).
//
// --outgoing N sends N payments to another bank and compares the two sides (B3 stage 6).
//
// --outage runs transfers through a fraud-service outage and prints a timeline (B4).
//
// --race N runs rounds of a race (B3 stage 1): open an account, deposit --deposit (default 100),
// then send N transfers of --amount (default 60) from it, all at once. It reports how the API
// answered, and in how many of --rounds rounds (default 50) the account ended up overdrawn.
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;

var options = Options.Parse(args);
using var http = new HttpClient { BaseAddress = new Uri(options.Url), Timeout = TimeSpan.FromSeconds(30) };

if (Array.IndexOf(args, "--ordering-burst") is var burstAt and >= 0)
{
    var count = int.Parse(args[burstAt + 1], CultureInfo.InvariantCulture);
    int? previous = null;
    for (var i = 0; i < count; i++)
    {
        var opened = await http.PostAsJsonAsync("/accounts", new { holderName = $"Ordering burst {i}" });
        var account = (await opened.Content.ReadFromJsonAsync<OpenedAccount>())!.Id;
        if (previous is int other)
        {
            // A salary lands, and the customer immediately pays two people.
            await Task.WhenAll(
                http.PostAsJsonAsync("/transfers", new { fromAccountId = other, toAccountId = account, amount = 500m, reference = "Salary" }),
                http.PostAsJsonAsync("/transfers", new { fromAccountId = account, toAccountId = other, amount = 20m, reference = "Airtime" }),
                http.PostAsJsonAsync("/transfers", new { fromAccountId = account, toAccountId = other, amount = 35m, reference = "Groceries" }));
        }
        previous = account;
    }
    Console.WriteLine($"Opened {count} accounts, with three transfers each after the first.");
    return;
}

if (Array.IndexOf(args, "--race") is var raceAt and >= 0)
{
    var racers = int.Parse(args[raceAt + 1], CultureInfo.InvariantCulture);
    var rounds = Arg("--rounds", 50);
    var deposit = Arg("--deposit", 100);
    var amount = Arg("--amount", 60);
    var statuses = new Dictionary<int, int>();
    var overdrawn = 0;
    var lowest = decimal.MaxValue;
    for (var round = 0; round < rounds; round++)
    {
        var from = await OpenAsync($"Race {round} payer");
        var to = await OpenAsync($"Race {round} payee");
        (await http.PostAsJsonAsync($"/accounts/{from}/transactions", new { amount = (decimal)deposit, description = "Deposit" })).EnsureSuccessStatusCode();

        // All transfers wait at the gate, then go together.
        using var gate = new SemaphoreSlim(0);
        var sends = Enumerable.Range(0, racers).Select(async _ =>
        {
            await gate.WaitAsync();
            using var response = await http.PostAsJsonAsync("/transfers", new { fromAccountId = from, toAccountId = to, amount = (decimal)amount, reference = "Race" });
            return (int)response.StatusCode;
        }).ToList();
        gate.Release(racers);
        foreach (var status in await Task.WhenAll(sends))
        {
            statuses[status] = statuses.GetValueOrDefault(status) + 1;
        }

        var balance = (await http.GetFromJsonAsync<Summary>($"/accounts/{from}/summary"))!.Balance;
        lowest = Math.Min(lowest, balance);
        if (balance < 0)
        {
            overdrawn++;
        }
    }

    Console.WriteLine($"{rounds} rounds: deposit {deposit}, then {racers} transfers of {amount} at once.");
    Console.WriteLine($"Responses: {string.Join(", ", statuses.OrderBy(s => s.Key).Select(s => $"{(s.Key == 0 ? "timed out" : s.Key)} x {s.Value}"))}");
    Console.WriteLine($"Overdrawn after {overdrawn} of {rounds} rounds; lowest balance {lowest}.");
    return;

    async Task<int> OpenAsync(string name)
    {
        var opened = await http.PostAsJsonAsync("/accounts", new { holderName = name });
        return (await opened.Content.ReadFromJsonAsync<OpenedAccount>())!.Id;
    }
}

int Arg(string name, int fallback) =>
    Array.IndexOf(args, name) is var i and >= 0 ? int.Parse(args[i + 1], CultureInfo.InvariantCulture) : fallback;

if (Array.IndexOf(args, "--payroll") is var payrollAt and >= 0)
{
    var count = int.Parse(args[payrollAt + 1], CultureInfo.InvariantCulture);
    // --employer: every salary comes from one newly opened, well-funded account (B3 stage 2's hot account).
    int? employer = args.Contains("--employer") ? await OpenFundedAsync("Employer", count * 1000m * 2) : null;
    var result = await BurstAsync(count, i => new
    {
        fromAccountId = employer ?? 22 + i % 50, toAccountId = 80 + i % 50, amount = 1000m, reference = "Payroll",
    });
    Console.WriteLine($"Posted {count} salary transfers{(employer is null ? "" : $" from one account ({employer})")} in {result.Seconds:F1}s: {result}");
    return;
}

// --double-pay N (B3 stage 3): N payments of 1.00 from one busy account, made the way a phone
// makes them. If no answer arrives within --timeout-ms (default 1000), it sends the payment
// again, up to three attempts, while the first request carries on at the server. Every tenth
// payment is also double-tapped: sent twice at once. --keys sends one Idempotency-Key per payment.
if (Array.IndexOf(args, "--double-pay") is var doubleAt and >= 0)
{
    var count = int.Parse(args[doubleAt + 1], CultureInfo.InvariantCulture);
    var timeout = TimeSpan.FromMilliseconds(Arg("--timeout-ms", 1000));
    var keys = args.Contains("--keys");
    var payer = await OpenFundedAsync("Busy payer", count * 10m);
    var payee = await OpenFundedAsync("Payee", 0m);
    var statuses = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
    var allRequests = new System.Collections.Concurrent.ConcurrentBag<Task>();
    var next = 0;
    var unanswered = 0;

    async Task SendAsync(string? key)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/transfers")
        {
            Content = JsonContent.Create(new { fromAccountId = payer, toAccountId = payee, amount = 1m, reference = "Coffee" }),
        };
        if (key is not null)
        {
            message.Headers.Add("Idempotency-Key", key);
        }
        try
        {
            using var response = await http.SendAsync(message);
            var label = response.Headers.Contains("Idempotent-Replayed") ? $"{(int)response.StatusCode} replayed" : $"{(int)response.StatusCode}";
            statuses.AddOrUpdate(label, 1, (_, n) => n + 1);
        }
        catch (TaskCanceledException)
        {
            statuses.AddOrUpdate("no answer in 30 s", 1, (_, n) => n + 1);
        }
    }

    var timer = Stopwatch.StartNew();
    await Task.WhenAll(Enumerable.Range(0, options.Concurrency).Select(async _ =>
    {
        while (Interlocked.Increment(ref next) is var i && i <= count)
        {
            var key = keys ? Guid.NewGuid().ToString() : null;
            var attempts = new List<Task> { SendAsync(key) };
            if (i % 10 == 0)
            {
                attempts.Add(SendAsync(key)); // a double tap
            }
            foreach (var attempt in attempts)
            {
                allRequests.Add(attempt);
            }

            // Wait for an answer; after each timeout, try again, leaving the earlier request running.
            for (var tries = 1; ; tries++)
            {
                var answered = Task.WhenAny(attempts);
                if (await Task.WhenAny(answered, Task.Delay(timeout)) == answered)
                {
                    break;
                }
                if (tries == 3)
                {
                    Interlocked.Increment(ref unanswered);
                    break;
                }
                var retry = SendAsync(key);
                attempts.Add(retry);
                allRequests.Add(retry);
            }
        }
    }));
    await Task.WhenAll(allRequests); // let every request the server is still working on finish
    var received = (await http.GetFromJsonAsync<Summary>($"/accounts/{payee}/summary"))!.Balance;

    Console.WriteLine($"{count} payments of 1.00 {(keys ? "with" : "without")} idempotency keys, client timeout {timeout.TotalMilliseconds:F0} ms, in {timer.Elapsed.TotalSeconds:F1}s.");
    Console.WriteLine($"Requests sent: {allRequests.Count}; responses {string.Join(", ", statuses.OrderBy(s => s.Key).Select(s => $"{s.Key} x {s.Value}"))}.");
    Console.WriteLine($"The payee received {received:F2} for {count} payments: {received - count:F0} extra payments. The client gave up waiting on {unanswered} payments.");
    return;
}

// --outgoing N (B3 stage 6): N payments of 10.00 to another bank, from one new account. Then it
// waits for the API to resolve any still pending, and compares what left the customer's account
// with what the payment network says it paid. --network is the fake network's address.
if (Array.IndexOf(args, "--outgoing") is var outgoingAt and >= 0)
{
    var count = int.Parse(args[outgoingAt + 1], CultureInfo.InvariantCulture);
    var networkUrl = Array.IndexOf(args, "--network") is var n and >= 0 ? args[n + 1] : "http://localhost:5090";
    using var networkHttp = new HttpClient { BaseAddress = new Uri(networkUrl) };
    (await networkHttp.PostAsync("/network/reset", null)).EnsureSuccessStatusCode();

    const decimal deposit = 1_000_000m;
    var payer = await OpenFundedAsync("Pays other banks", deposit);
    Console.WriteLine($"Paying from account {payer}.");
    var result = await BurstToAsync(count, "/outgoing-payments", i => new
    {
        fromAccountId = payer, amount = 10m, toBank = "Other Bank", toAccount = $"ACC{i:D6}",
    });
    Console.WriteLine($"Sent {count} payments of 10.00 in {result.Seconds:F1}s: {result}");

    // Wait for payments the API couldn't confirm straight away.
    for (var waited = 0; waited < 60; waited++)
    {
        var summary = await http.GetFromJsonAsync<List<StatusCount>>($"/outgoing-payments/summary?fromAccountId={payer}");
        if (summary is null || !summary.Any(s => s.Status == "Pending")) break;
        await Task.Delay(1000);
    }

    var network = (await networkHttp.GetFromJsonAsync<NetworkStats>("/network/stats"))!;
    var balance = (await http.GetFromJsonAsync<Summary>($"/accounts/{payer}/summary"))!.Balance;
    var debited = deposit - balance;
    var statuses = await http.GetFromJsonAsync<List<StatusCount>>($"/outgoing-payments/summary?fromAccountId={payer}");
    Console.WriteLine($"Recorded by the API: {string.Join(", ", (statuses ?? []).Select(s => $"{s.Status} x {s.Count}"))}");
    Console.WriteLine($"Taken from the customer: {debited:F2}. Paid by the network: {network.AmountSettled:F2} ({network.Settlements} settlements, {network.DistinctReferences} distinct references).");
    Console.WriteLine($"Difference: {network.AmountSettled - debited:F2} paid out that the customer wasn't charged for.");
    return;
}

// --outage (B4): transfers from --concurrency clients for --seconds (default 40), while a
// bystander reads account summaries every 250 ms, to see whether the API still answers anyone.
// --blip-at T --blip-seconds D takes the fraud service down for D seconds, T seconds in. At the
// end it prints a timeline, second by second, alongside what the fraud service saw.
if (args.Contains("--outage"))
{
    var seconds = Arg("--seconds", 40);
    var fraudUrl = Array.IndexOf(args, "--fraud") is var f and >= 0 ? args[f + 1] : "http://localhost:5090";
    using var fraudHttp = new HttpClient { BaseAddress = new Uri(fraudUrl) };
    (await fraudHttp.PostAsync("/fraud/reset", null)).EnsureSuccessStatusCode();

    // Warm up first, so start-up costs (compilation, connection pools) don't look like an outage.
    for (var i = 0; i < 50; i++)
    {
        try { using var _ = await http.PostAsJsonAsync("/transfers", options.NextTransfer()); } catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
    }
    (await fraudHttp.PostAsync("/fraud/reset", null)).EnsureSuccessStatusCode();

    var timeline = new System.Collections.Concurrent.ConcurrentDictionary<int, OutageSecond>();
    OutageSecond At(int second) => timeline.GetOrAdd(second, _ => new OutageSecond());
    var clock = Stopwatch.StartNew();
    int Now() => (int)clock.Elapsed.TotalSeconds;

    var blip = Array.IndexOf(args, "--blip-at") >= 0
        ? Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(Arg("--blip-at", 10)));
            var kind = Array.IndexOf(args, "--blip-kind") is var k and >= 0 ? args[k + 1] : "down";
            await fraudHttp.PostAsync($"/fraud/blip?seconds={Arg("--blip-seconds", 5)}&kind={kind}", null);
        })
        : Task.CompletedTask;

    async Task SendOneAsync()
    {
        var started = Now();
        var timer = Stopwatch.StartNew();
        string outcome;
        try
        {
            using var response = await http.PostAsJsonAsync("/transfers", options.NextTransfer());
            outcome = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        }
        catch (TaskCanceledException)
        {
            outcome = "no answer";
        }
        catch (HttpRequestException)
        {
            outcome = "refused";
        }
        At(started).Record(outcome, timer.Elapsed.TotalMilliseconds);
    }

    // --rate R sends R transfers a second whether or not earlier ones have been answered, as
    // real customers do. Without it, --concurrency clients each wait for their answer first.
    var rate = Arg("--rate", 0);
    var clients = rate > 0
        ? [Task.Run(async () =>
        {
            var inFlight = new List<Task>();
            var sent = 0;
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                while (sent < clock.Elapsed.TotalSeconds * rate)
                {
                    inFlight.Add(SendOneAsync());
                    sent++;
                }
                await Task.Delay(5);
            }
            await Task.WhenAll(inFlight);
        })]
        : Enumerable.Range(0, options.Concurrency).Select(async _ =>
        {
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                await SendOneAsync();
            }
        }).ToList();

    var bystander = Task.Run(async () =>
    {
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            var started = Now();
            var timer = Stopwatch.StartNew();
            try
            {
                using var response = await http.GetAsync($"/accounts/{options.NextAccount()}/summary");
                At(started).Bystander(timer.Elapsed.TotalMilliseconds, response.IsSuccessStatusCode);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                At(started).Bystander(timer.Elapsed.TotalMilliseconds, ok: false);
            }
            await Task.Delay(250);
        }
    });

    await Task.WhenAll([.. clients, bystander, blip]);
    var fraudStats = await fraudHttp.GetFromJsonAsync<FraudStatsBody>("/fraud/stats");
    var fraudBySecond = (fraudStats?.Seconds ?? []).ToDictionary(s => s.Second);

    Console.WriteLine(rate > 0 ? $"{rate} transfers a second for {seconds}s." : $"{options.Concurrency} clients sending transfers for {seconds}s.");
    Console.WriteLine("  s | transfers: ok   other  p50 ms | bystander: worst ms  failed | fraud service: calls  refused  failed");
    foreach (var (second, data) in timeline.OrderBy(t => t.Key))
    {
        var fs = fraudBySecond.GetValueOrDefault(second);
        Console.WriteLine($"{second,3} | {data.Ok,14} {data.Other,7} {data.P50,7:F0} | {data.BystanderWorst,19:F0} {data.BystanderFailed,7} | {fs?.Received ?? 0,20} {fs?.Refused ?? 0,8} {fs?.Failed ?? 0,7}");
    }
    var everySecond = timeline.Values;
    Console.WriteLine($"Totals: {everySecond.Sum(d => d.Ok)} transfers succeeded; others: {string.Join(", ", everySecond.SelectMany(d => d.Outcomes).GroupBy(o => o.Key).Where(g => g.Key != "201").Select(g => $"{g.Key} x {g.Sum(o => o.Value)}"))}; " +
        $"bystander failed {everySecond.Sum(d => d.BystanderFailed)} times, worst {everySecond.Max(d => d.BystanderWorst):F0} ms; fraud service received {fraudStats?.Seconds.Sum(s => s.Received) ?? 0} calls.");
    return;
}

// --crossfire N (B3 stage 2): two accounts paying each other, A to B and B to A, N transfers in all.
if (Array.IndexOf(args, "--crossfire") is var crossAt and >= 0)
{
    var count = int.Parse(args[crossAt + 1], CultureInfo.InvariantCulture);
    var a = await OpenFundedAsync("Crossfire A", 1_000_000m);
    var b = await OpenFundedAsync("Crossfire B", 1_000_000m);
    var result = await BurstAsync(count, i => i % 2 == 0
        ? new { fromAccountId = a, toAccountId = b, amount = 10m, reference = "Crossfire" }
        : new { fromAccountId = b, toAccountId = a, amount = 10m, reference = "Crossfire" });
    Console.WriteLine($"Posted {count} transfers between accounts {a} and {b} in {result.Seconds:F1}s: {result}");
    return;
}

async Task<int> OpenFundedAsync(string name, decimal deposit)
{
    var opened = await http.PostAsJsonAsync("/accounts", new { holderName = name });
    var id = (await opened.Content.ReadFromJsonAsync<OpenedAccount>())!.Id;
    if (deposit > 0)
    {
        (await http.PostAsJsonAsync($"/accounts/{id}/transactions", new { amount = deposit, description = "Deposit" })).EnsureSuccessStatusCode();
    }
    return id;
}

// Sends `count` transfers, --concurrency at a time, and reports the responses and latencies.
async Task<BurstResult> BurstAsync(int count, Func<int, object> transfer) => await BurstToAsync(count, "/transfers", transfer);

async Task<BurstResult> BurstToAsync(int count, string path, Func<int, object> transfer)
{
    var next = 0;
    var statuses = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();
    var latencies = new System.Collections.Concurrent.ConcurrentBag<double>();
    var timer = Stopwatch.StartNew();
    await Task.WhenAll(Enumerable.Range(0, options.Concurrency).Select(async _ =>
    {
        while (Interlocked.Increment(ref next) is var i && i <= count)
        {
            var started = Stopwatch.GetTimestamp();
            int status;
            try
            {
                using var response = await http.PostAsJsonAsync(path, transfer(i));
                status = (int)response.StatusCode;
            }
            catch (TaskCanceledException)
            {
                status = 0; // no answer within the client's timeout
            }
            latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            statuses.AddOrUpdate(status, 1, (_, n) => n + 1);
        }
    }));
    var sorted = latencies.Order().ToArray();
    double At(double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
    return new BurstResult(timer.Elapsed.TotalSeconds, count / timer.Elapsed.TotalSeconds,
        string.Join(", ", statuses.OrderBy(s => s.Key).Select(s => $"{(s.Key == 0 ? "timed out" : s.Key)} x {s.Value}")), At(0.5), At(0.99), sorted[^1]);
}

Console.WriteLine($"{options.Concurrency} workers for {options.Seconds}s against {options.Url}{(options.Transfers ? "/transfers (POST)" : options.Path)}");

// Warm up first: the first requests pay for JIT compilation and opening connections.
// A struggling server can fail these too; that's what the measurement is for, so carry on.
for (var i = 0; i < 5; i++)
{
    try
    {
        using var _ = await http.GetAsync(options.PathFor(options.NextAccount()));
    }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
    {
        Console.WriteLine($"Warm-up request failed: {e.Message}");
    }
}

var latencies = new List<double>[options.Concurrency];
var errors = 0;
var deadline = Stopwatch.StartNew();

var workers = Enumerable.Range(0, options.Concurrency).Select(async worker =>
{
    var mine = latencies[worker] = [];
    while (deadline.Elapsed.TotalSeconds < options.Seconds)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            using var response = options.Transfers
                ? await http.PostAsJsonAsync("/transfers", options.NextTransfer())
                : await http.GetAsync(options.PathFor(options.NextAccount()));
            await response.Content.LoadIntoBufferAsync();
            if (!response.IsSuccessStatusCode) Interlocked.Increment(ref errors);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Interlocked.Increment(ref errors);
        }
        mine.Add(timer.Elapsed.TotalMilliseconds);
    }
}).ToArray();
await Task.WhenAll(workers);

var all = latencies.SelectMany(l => l).Order().ToArray();
Console.WriteLine();
Console.WriteLine($"requests  {all.Length,8:N0}   ({all.Length / deadline.Elapsed.TotalSeconds:N1}/s), errors {errors:N0}");
foreach (var (label, p) in new[] { ("p50", 0.50), ("p90", 0.90), ("p95", 0.95), ("p99", 0.99) })
{
    Console.WriteLine($"{label}       {Percentile(all, p),8:N1} ms");
}
Console.WriteLine($"max       {all[^1],8:N1} ms");

// #region percentile
// Nearest-rank percentile: the smallest sample with at least p of the samples at or below it.
// The input must already be sorted (see `.Order()` above).
static double Percentile(double[] sorted, double p) =>
    sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
// #endregion percentile

sealed record OpenedAccount(int Id);

sealed record Summary(decimal Balance);

sealed record StatusCount(string Status, int Count, decimal Amount);

sealed record FraudStatsBody(List<FraudStatsSecond> Seconds);

sealed record FraudStatsSecond(int Second, int Received, int Served, int Refused, int Failed);

sealed class OutageSecond
{
    private readonly object _gate = new();
    private readonly List<double> _latencies = [];

    public Dictionary<string, int> Outcomes { get; } = [];
    public int Ok => Outcomes.GetValueOrDefault("201");
    public int Other => Outcomes.Where(o => o.Key != "201").Sum(o => o.Value);
    public double P50 { get { lock (_gate) { return _latencies.Count == 0 ? 0 : _latencies.Order().ElementAt(_latencies.Count / 2); } } }
    public double BystanderWorst { get; private set; }
    public int BystanderFailed { get; private set; }

    public void Record(string outcome, double ms)
    {
        lock (_gate)
        {
            Outcomes[outcome] = Outcomes.GetValueOrDefault(outcome) + 1;
            _latencies.Add(ms);
        }
    }

    public void Bystander(double ms, bool ok)
    {
        lock (_gate)
        {
            BystanderWorst = Math.Max(BystanderWorst, ms);
            if (!ok) BystanderFailed++;
        }
    }
}

sealed record NetworkStats(int Settlements, int DistinctReferences, decimal AmountSettled);

sealed record BurstResult(double Seconds, double PerSecond, string Statuses, double P50, double P99, double Max)
{
    public override string ToString() =>
        $"{PerSecond:F0} transfers/s; responses {Statuses}; latency p50 {P50:F0} ms, p99 {P99:F0} ms, max {Max:F0} ms";
}

sealed record Options(string Url, string Path, int Concurrency, int Seconds, int Accounts, double Hot, int HotAccounts, bool Transfers)
{
    public object NextTransfer()
    {
        var from = NextAccount();
        var to = from % Accounts + 1; // a different account
        return new { fromAccountId = from, toAccountId = to, amount = Math.Round(Random.Shared.Next(1000, 500_000) / 100m, 2), reference = "Load test" };
    }

    public int NextAccount() => Random.Shared.NextDouble() < Hot
        ? Random.Shared.Next(1, HotAccounts + 1)
        : Random.Shared.Next(1, Accounts + 1);

    public string PathFor(int accountId) => Path.Replace("{id}", accountId.ToString(CultureInfo.InvariantCulture));

    public static Options Parse(string[] args)
    {
        string Get(string name, string fallback)
        {
            var i = Array.IndexOf(args, $"--{name}");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        return new Options(
            Get("url", "http://localhost:5080"),
            Get("path", "/accounts/{id}/transactions"),
            int.Parse(Get("concurrency", "8"), CultureInfo.InvariantCulture),
            int.Parse(Get("seconds", "20"), CultureInfo.InvariantCulture),
            int.Parse(Get("accounts", "100000"), CultureInfo.InvariantCulture),
            double.Parse(Get("hot", "0"), CultureInfo.InvariantCulture),
            int.Parse(Get("hot-accounts", "100"), CultureInfo.InvariantCulture),
            args.Contains("--transfers"));
    }
}
