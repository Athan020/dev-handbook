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
using System.Diagnostics;
using System.Globalization;

var options = Options.Parse(args);
using var http = new HttpClient { BaseAddress = new Uri(options.Url), Timeout = TimeSpan.FromSeconds(30) };

Console.WriteLine($"{options.Concurrency} workers for {options.Seconds}s against {options.Url}{options.Path}");

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
        var path = options.PathFor(options.NextAccount());
        var timer = Stopwatch.StartNew();
        try
        {
            using var response = await http.GetAsync(path);
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

sealed record Options(string Url, string Path, int Concurrency, int Seconds, int Accounts, double Hot, int HotAccounts)
{
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
            int.Parse(Get("hot-accounts", "100"), CultureInfo.InvariantCulture));
    }
}
