using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;

/// <summary>
/// Journey B5's lab modes, against the modular monolith (src/Bank.Modular, http://localhost:5100).
/// </summary>
static class ModularLabs
{
    static string Get(string[] args, string name, string fallback) =>
        Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;

    static int Int(string[] args, string name, int fallback) => int.Parse(Get(args, name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);

    static double Double(string[] args, string name, double fallback) => double.Parse(Get(args, name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);

    static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    // --split entity|capability (B5 stage 2): --concurrency callers send transfers for --seconds,
    // either through the ledger split by table (the caller composes six calls) or by capability
    // (one call). --crash-rate 0.01 makes the caller stop after the debit, as if it crashed, in
    // that share of entity-split transfers. Checks the books before and after.
    public static async Task SplitAsync(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var split = Get(args, "--split", "capability");
        var seconds = Int(args, "--seconds", 20);
        var concurrency = Int(args, "--concurrency", 16);
        var accounts = Int(args, "--accounts", 1000);
        var crashRate = Double(args, "--crash-rate", 0);
        using var http = new HttpClient { BaseAddress = new Uri(Get(args, "--url", "http://localhost:5100")), Timeout = TimeSpan.FromSeconds(30) };

        var before = await http.GetFromJsonAsync<Books>("/ledger/check");
        var payees = new ConcurrentDictionary<int, PayeeRef[]>();
        var outcomes = new ConcurrentDictionary<string, int>();
        var latencies = new ConcurrentBag<double>();
        var calls = 0;
        var clock = Stopwatch.StartNew();

        async Task<HttpResponseMessage> Call(Func<Task<HttpResponseMessage>> send)
        {
            Interlocked.Increment(ref calls);
            return await send();
        }

        // #region entity-transfer
        // The transfer, composed by the caller from the entity services' operations.
        async Task<string> EntityTransferAsync(int from, int to, decimal amount)
        {
            var payer = await (await Call(() => http.GetAsync($"/entity/accounts/{from}"))).Content.ReadFromJsonAsync<EntityAccount>();
            var payee = await (await Call(() => http.GetAsync($"/entity/accounts/{to}"))).Content.ReadFromJsonAsync<EntityAccount>();
            if (payer!.Balance < amount) return "insufficient funds";

            (await Call(() => http.PutAsJsonAsync($"/entity/accounts/{from}/balance", new { balance = payer.Balance - amount }))).EnsureSuccessStatusCode();
            if (Random.Shared.NextDouble() < crashRate) return "caller crashed after the debit";
            (await Call(() => http.PutAsJsonAsync($"/entity/accounts/{to}/balance", new { balance = payee!.Balance + amount }))).EnsureSuccessStatusCode();

            var transferId = Guid.CreateVersion7();
            (await Call(() => http.PostAsJsonAsync("/entity/entries", new { transferId, accountId = from, amount = -amount, reference = "Load test" }))).EnsureSuccessStatusCode();
            (await Call(() => http.PostAsJsonAsync("/entity/entries", new { transferId, accountId = to, amount, reference = "Load test" }))).EnsureSuccessStatusCode();
            return "completed";
        }
        // #endregion entity-transfer

        async Task<string> CapabilityTransferAsync(int from, Guid payeeId, decimal amount)
        {
            using var response = await Call(() => http.PostAsJsonAsync("/transfers", new { fromAccountId = from, payeeId, amount }));
            return response.StatusCode switch
            {
                HttpStatusCode.Created => "completed",
                HttpStatusCode.UnprocessableEntity => "insufficient funds",
                var other => ((int)other).ToString(CultureInfo.InvariantCulture),
            };
        }

        var workers = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                var from = Random.Shared.Next(1, accounts + 1);
                if (!payees.TryGetValue(from, out var options))
                    payees[from] = options = (await http.GetFromJsonAsync<PayeeRef[]>($"/accounts/{from}/payees"))!;
                var payee = options[Random.Shared.Next(options.Length)];
                var amount = Random.Shared.Next(100, 5_000) / 100m;

                var timer = Stopwatch.StartNew();
                var outcome = split == "entity"
                    ? await EntityTransferAsync(from, payee.PayeeAccountId, amount)
                    : await CapabilityTransferAsync(from, payee.PayeeId, amount);
                latencies.Add(timer.Elapsed.TotalMilliseconds);
                outcomes.AddOrUpdate(outcome, 1, (_, n) => n + 1);
            }
        });
        await Task.WhenAll(workers);
        var after = await http.GetFromJsonAsync<Books>("/ledger/check");

        var sorted = latencies.Order().ToArray();
        var total = outcomes.Values.Sum();
        Console.WriteLine($"{split} split, {concurrency} callers, {seconds} s: {total:N0} transfers ({total / (double)seconds:F0}/s), " +
            $"{calls / (double)total:F1} calls each; p50 {sorted[sorted.Length / 2]:F0} ms, p99 {sorted[(int)(sorted.Length * 0.99)]:F0} ms");
        Console.WriteLine($"  outcomes: {string.Join(", ", outcomes.OrderByDescending(o => o.Value).Select(o => $"{o.Key} x {o.Value:N0}"))}");
        Console.WriteLine($"  books before: {Describe(before!)}");
        Console.WriteLine($"  books after:  {Describe(after!)}");
    }

    // #region co-change
    // --co-change <path> [--top 12] [--min 3] (B5 stage 2): which files change together. Reads the
    // git history of <path>, counts how often each pair of files appears in the same commit, and
    // lists the pairs that most often do, with the share of the rarer file's commits that also
    // touched the other. Files that nearly always change together belong inside one boundary.
    public static void CoChange(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var path = Get(args, "--co-change", ".");
        var top = Int(args, "--top", 12);
        var min = Int(args, "--min", 3);

        var git = Process.Start(new ProcessStartInfo("git", ["log", "--name-only", "--format=tformat:@@commit", "--", path])
        {
            RedirectStandardOutput = true,
        })!;
        var commits = new List<HashSet<string>>();
        while (git.StandardOutput.ReadLine() is { } line)
        {
            if (line == "@@commit") commits.Add([]);
            else if (line.Length > 0 && line.EndsWith(".cs", StringComparison.Ordinal) && !line.Contains("/Migrations/", StringComparison.Ordinal))
                commits[^1].Add(Path.GetFileName(line));
        }
        git.WaitForExit();

        var changes = commits.SelectMany(c => c).GroupBy(f => f).ToDictionary(g => g.Key, g => g.Count());
        var pairs = commits
            .SelectMany(c => c.SelectMany(a => c.Where(b => string.CompareOrdinal(a, b) < 0).Select(b => (a, b))))
            .GroupBy(p => p)
            .Select(g => (Pair: g.Key, Together: g.Count(), Share: g.Count() / (double)Math.Min(changes[g.Key.a], changes[g.Key.b])))
            .Where(p => p.Together >= min)
            .OrderByDescending(p => p.Share).ThenByDescending(p => p.Together)
            .Take(top);

        Console.WriteLine($"{commits.Count} commits touched {path}.");
        foreach (var (pair, together, share) in pairs)
            Console.WriteLine($"  {share,4:P0}  {together,3} together  {pair.a} ({changes[pair.a]}) + {pair.b} ({changes[pair.b]})");
    }
    // #endregion co-change

    // --staleness (B5 stage 3): sends --count transfers (default 200), --rate a second (default
    // 10), through --url. After each, polls the payer's statement at --statements-url (default
    // the same) until the transfer is on it, and reports how long that took.
    public static async Task StalenessAsync(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var count = Int(args, "--count", 200);
        var rate = Int(args, "--rate", 10);
        var accounts = Int(args, "--accounts", 1000);
        using var http = new HttpClient { BaseAddress = new Uri(Get(args, "--url", "http://localhost:5100")), Timeout = TimeSpan.FromSeconds(10) };
        using var statements = new HttpClient { BaseAddress = new Uri(Get(args, "--statements-url", Get(args, "--url", "http://localhost:5100"))), Timeout = TimeSpan.FromSeconds(10) };
        var lags = new ConcurrentBag<double>();
        var outcomes = new ConcurrentDictionary<string, int>();

        async Task OneAsync()
        {
            var from = Random.Shared.Next(1, accounts + 1);
            var payee = (await http.GetFromJsonAsync<PayeeRef[]>($"/accounts/{from}/payees"))![0];
            using var sent = await http.PostAsJsonAsync("/transfers", new { fromAccountId = from, payeeId = payee.PayeeId, amount = 1m });
            if (sent.StatusCode != HttpStatusCode.Created)
            {
                outcomes.AddOrUpdate($"transfer {(int)sent.StatusCode}", 1, (_, n) => n + 1);
                return;
            }
            var transferId = (await sent.Content.ReadFromJsonAsync<Created>())!.TransferId;
            var now = DateTimeOffset.UtcNow;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(10))
            {
                try
                {
                    var statement = await statements.GetFromJsonAsync<StatementLines>($"/accounts/{from}/statements/{now.Year}/{now.Month}");
                    if (statement!.Lines.Any(l => l.TransferId == transferId))
                    {
                        lags.Add(timer.Elapsed.TotalMilliseconds);
                        outcomes.AddOrUpdate("on the statement", 1, (_, n) => n + 1);
                        return;
                    }
                }
                catch (HttpRequestException e)
                {
                    outcomes.AddOrUpdate($"statement {(int?)e.StatusCode ?? 0}", 1, (_, n) => n + 1);
                    return;
                }
                await Task.Delay(20);
            }
            outcomes.AddOrUpdate("not on the statement after 10 s", 1, (_, n) => n + 1);
        }

        var all = new List<Task>();
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var due = TimeSpan.FromSeconds(i / (double)rate);
            if (due > clock.Elapsed) await Task.Delay(due - clock.Elapsed);
            all.Add(OneAsync());
        }
        await Task.WhenAll(all);

        var sorted = lags.Order().ToArray();
        Console.WriteLine($"{count} transfers at {rate}/s: {string.Join(", ", outcomes.Select(o => $"{o.Key} x {o.Value}"))}");
        if (sorted.Length > 0)
            Console.WriteLine($"  on the payer's statement after: p50 {sorted[sorted.Length / 2]:F0} ms, p99 {sorted[(int)(sorted.Length * 0.99)]:F0} ms, max {sorted[^1]:F0} ms");
    }

    sealed record Created(Guid TransferId);

    sealed record StatementLines(List<StatementLine> Lines);

    sealed record StatementLine(Guid TransferId);

    // --chain (B5 stage 4): sends transfers at --rate a second (default 20) for --seconds
    // (default 60) through --url, whether or not earlier ones have been answered, and reports how
    // many were made and how fast. Accepted transfers (202) are followed until they're made or
    // rejected. --blip fraud --blip-at 20 --blip-seconds 10 takes one of the fake services'
    // checks (at --fake-url) down for a while. --payees-url if payees run elsewhere.
    public static async Task ChainAsync(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var seconds = Int(args, "--seconds", 60);
        var rate = Int(args, "--rate", 20);
        var accounts = Int(args, "--accounts", 1000);
        var blip = Get(args, "--blip", "");
        var blipAt = Int(args, "--blip-at", 20);
        var blipSeconds = Int(args, "--blip-seconds", 10);
        using var http = new HttpClient { BaseAddress = new Uri(Get(args, "--url", "http://localhost:5100")), Timeout = TimeSpan.FromSeconds(30) };
        using var fake = new HttpClient { BaseAddress = new Uri(Get(args, "--fake-url", "http://localhost:5090")) };
        using var payeesHttp = new HttpClient { BaseAddress = new Uri(Get(args, "--payees-url", Get(args, "--url", "http://localhost:5100"))) };
        (await fake.PostAsync("/checks/reset", null)).EnsureSuccessStatusCode();

        var payees = new ConcurrentDictionary<int, PayeeRef[]>();
        for (var account = 1; account <= accounts; account += 100)
        {
            // Warm up: look up some payees, and make a few transfers, before measuring.
            payees[account] = (await payeesHttp.GetFromJsonAsync<PayeeRef[]>($"/accounts/{account}/payees"))!;
            using var _ = await http.PostAsJsonAsync("/transfers", new { fromAccountId = account, payeeId = payees[account][0].PayeeId, amount = 0.01m });
        }

        var responses = new ConcurrentDictionary<string, int>();
        var latencies = new ConcurrentBag<double>();
        var madeAfter = new ConcurrentBag<double>();
        var final = new ConcurrentDictionary<string, int>();
        var duringBlip = new ConcurrentDictionary<string, int>();
        var clock = Stopwatch.StartNew();

        async Task OneAsync(int i)
        {
            var from = Random.Shared.Next(1, accounts + 1);
            if (!payees.TryGetValue(from, out var options))
                payees[from] = options = (await payeesHttp.GetFromJsonAsync<PayeeRef[]>($"/accounts/{from}/payees"))!;
            var sentAt = clock.Elapsed;
            var inBlip = blip != "" && sentAt.TotalSeconds >= blipAt && sentAt.TotalSeconds < blipAt + blipSeconds;
            var timer = Stopwatch.StartNew();
            string outcome;
            Guid? accepted = null;
            try
            {
                using var response = await http.PostAsJsonAsync("/transfers", new { fromAccountId = from, payeeId = options[Random.Shared.Next(options.Length)].PayeeId, amount = 1m });
                outcome = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
                if (response.StatusCode == HttpStatusCode.Accepted) accepted = (await response.Content.ReadFromJsonAsync<Created>())!.TransferId;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                outcome = "no answer";
            }
            latencies.Add(timer.Elapsed.TotalMilliseconds);
            responses.AddOrUpdate(outcome, 1, (_, n) => n + 1);

            var result = outcome == "201" ? "made" : "not made";
            if (accepted is { } id)
            {
                result = "still pending after 120 s";
                while (timer.Elapsed < TimeSpan.FromSeconds(120))
                {
                    await Task.Delay(500);
                    RequestStatus? status;
                    try { status = await http.GetFromJsonAsync<RequestStatus>($"/transfers/{id}"); }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { continue; }
                    if (status!.Status == "Pending") continue;
                    result = status.Status == "Completed" ? "made" : $"rejected: {status.Reason}";
                    if (result == "made") madeAfter.Add(timer.Elapsed.TotalMilliseconds);
                    break;
                }
            }
            final.AddOrUpdate(result, 1, (_, n) => n + 1);
            if (inBlip) duringBlip.AddOrUpdate(result, 1, (_, n) => n + 1);
        }

        var all = new List<Task>();
        var blipped = false;
        for (var i = 0; i < seconds * rate; i++)
        {
            var due = TimeSpan.FromSeconds(i / (double)rate);
            if (due > clock.Elapsed) await Task.Delay(due - clock.Elapsed);
            if (blip != "" && !blipped && clock.Elapsed.TotalSeconds >= blipAt)
            {
                (await fake.PostAsync($"/checks/{blip}/blip?seconds={blipSeconds}", null)).EnsureSuccessStatusCode();
                blipped = true;
            }
            all.Add(OneAsync(i));
        }
        await Task.WhenAll(all);

        static double At(double[] sorted, double p) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * p))];
        var lat = latencies.Order().ToArray();
        var made = final.GetValueOrDefault("made");
        var total = seconds * rate;
        Console.WriteLine($"{seconds} s at {rate}/s: {total:N0} transfers");
        Console.WriteLine($"  responses: {string.Join(", ", responses.OrderByDescending(r => r.Value).Select(r => $"{r.Key} x {r.Value:N0}"))}; " +
            $"p50 {At(lat, 0.5):F0} ms, p95 {At(lat, 0.95):F0} ms, p99 {At(lat, 0.99):F0} ms");
        Console.WriteLine($"  made: {made:N0} of {total:N0} ({100.0 * made / total:F1}%); {string.Join(", ", final.Where(f => f.Key != "made").Select(f => $"{f.Key} x {f.Value:N0}"))}");
        if (!madeAfter.IsEmpty)
        {
            var after = madeAfter.Order().ToArray();
            Console.WriteLine($"  accepted transfers, from request to made: p50 {At(after, 0.5):F0} ms, p99 {At(after, 0.99):F0} ms, max {after[^1]:F0} ms");
        }
        if (blip != "")
            Console.WriteLine($"  sent while {blip} was down ({blipAt}-{blipAt + blipSeconds} s): {string.Join(", ", duringBlip.Select(d => $"{d.Key} x {d.Value:N0}"))}");
        var stats = await fake.GetStringAsync("/checks/stats");
        Console.WriteLine($"  checks: {stats}");
    }

    sealed record RequestStatus(string Status, string? Reason);

    static string Describe(Books books)
    {
        var missing = books.ExpectedTotal - books.TotalBalance;
        return $"total R{Money(books.TotalBalance)}" +
            (missing == 0 ? " (nothing missing)" : $" (R{Money(Math.Abs(missing))} {(missing > 0 ? "missing" : "created from nothing")})") +
            $", entries net R{Money(books.EntriesNet)}, {books.AccountsDisagreeing} accounts disagree with their entries";
    }

    sealed record Books(int Accounts, decimal TotalBalance, decimal ExpectedTotal, decimal EntriesNet, int AccountsDisagreeing);

    sealed record EntityAccount(int AccountId, decimal Balance);

    sealed record PayeeRef(Guid PayeeId, int PayeeAccountId);
}
