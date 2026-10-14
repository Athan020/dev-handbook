using Bank.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Endpoints;

/// <summary>One kind of difference between our records and the network's, with examples.</summary>
public sealed record ReconciliationLine(string Category, bool IsBreak, int Count, decimal OurAmount, decimal TheirAmount, IReadOnlyList<string> Examples);

public sealed record ReconciliationReport(
    decimal OurSettledTotal, decimal TheirTotal, decimal Difference, decimal ExplainedByBreaksAndTiming,
    IReadOnlyList<ReconciliationLine> Lines);

/// <summary>
/// Journey B3 stage 7. POST /reconciliations compares every outgoing payment with the payment
/// network's settlement record (optionally for one payer, ?fromAccountId=) and classifies every
/// difference, then checks that the classified differences explain the whole gap between totals.
/// </summary>
public static class Reconciliation
{
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/reconciliations", async (int? fromAccountId, BankDb db, NetworkClient network, CancellationToken ct) =>
        {
            var ours = await db.OutgoingPayments.AsNoTracking()
                .Where(p => fromAccountId == null || p.FromAccountId == fromAccountId)
                .ToListAsync(ct);
            var theirs = await network.SettlementsAsync(ct);
            return Results.Ok(Reconcile(ours, theirs, scopedToOurs: fromAccountId is not null));
        });
        return app;
    }

    // #region reconcile
    // Match on our reference, which the network echoes back, then sort every difference into a
    // category. Timing differences resolve themselves; breaks need someone to investigate.
    public static ReconciliationReport Reconcile(IReadOnlyList<OutgoingPayment> ours, IReadOnlyList<NetworkSettlement> theirs, bool scopedToOurs)
    {
        var byReference = theirs.ToLookup(t => t.Reference);
        var ourReferences = ours.Select(o => o.Id.ToString()).ToHashSet();
        var lines = new List<(string Category, bool IsBreak, string Reference, decimal Ours, decimal Theirs)>();

        foreach (var payment in ours)
        {
            var reference = payment.Id.ToString();
            var settlements = byReference[reference].ToList();
            var theirAmount = settlements.Sum(s => s.Amount);
            var ourAmount = payment.Status == "Settled" ? payment.Amount : 0m;

            var (category, isBreak) = (payment.Status, settlements.Count) switch
            {
                ("Pending", 0) => ("In flight: not yet in their records", false),
                ("Pending", _) => ("Timing: settled there, still pending here", false),
                ("Failed", 0) => ("Matched: failed on both sides", false),
                ("Failed", _) => ("Refunded here, but paid there", true),
                ("Settled", 0) => ("Settled here, missing from their records", true),
                ("Settled", > 1) => ("Paid more than once there", true),
                ("Settled", 1) when theirAmount != payment.Amount => ("Amounts differ", true),
                _ => ("Matched", false),
            };
            lines.Add((category, isBreak, reference, ourAmount, theirAmount));
        }

        // Settlements with a reference we've never issued. (When reconciling one payer's
        // payments, other payers' settlements are expected here, so skip this check.)
        if (!scopedToOurs)
        {
            foreach (var unknown in theirs.Where(t => !ourReferences.Contains(t.Reference)))
            {
                lines.Add(("Paid there, unknown here", true, unknown.Reference, 0m, unknown.Amount));
            }
        }

        var ourTotal = lines.Sum(l => l.Ours);
        var theirTotal = lines.Sum(l => l.Theirs);
        var report = lines
            .GroupBy(l => (l.Category, l.IsBreak))
            .Select(g => new ReconciliationLine(g.Key.Category, g.Key.IsBreak, g.Count(), g.Sum(l => l.Ours), g.Sum(l => l.Theirs),
                [.. g.Select(l => l.Reference).Take(3)]))
            .OrderBy(l => l.IsBreak).ThenByDescending(l => l.Count)
            .ToList();

        // Every rand of difference must belong to a category; anything left over is a bug in
        // the reconciliation itself.
        var explained = report.Where(l => l.Category != "Matched").Sum(l => l.TheirAmount - l.OurAmount);
        return new ReconciliationReport(ourTotal, theirTotal, theirTotal - ourTotal, explained, report);
    }
    // #endregion reconcile
}
