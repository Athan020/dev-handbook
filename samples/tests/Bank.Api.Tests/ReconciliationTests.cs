using Bank.Api.Data;
using Bank.Api.Endpoints;

namespace Bank.Api.Tests;

/// <summary>B3 stage 7: every difference between our records and the network's is classified and explained.</summary>
public sealed class ReconciliationTests
{
    private static OutgoingPayment Ours(string status, decimal amount = 10m) => new()
    {
        Id = Guid.NewGuid(), FromAccountId = 1, Amount = amount, ToBank = "Other Bank", ToAccount = "ACC1",
        Status = status, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static NetworkSettlement Theirs(OutgoingPayment payment, decimal? amount = null) =>
        new(payment.Id.ToString(), amount ?? payment.Amount, DateTimeOffset.UtcNow);

    [Fact]
    public void Each_kind_of_difference_gets_its_own_category_and_the_total_gap_is_explained()
    {
        var matched = Ours("Settled");
        var timing = Ours("Pending");
        var inFlight = Ours("Pending");
        var missing = Ours("Settled");
        var paidTwice = Ours("Settled");
        var wrongAmount = Ours("Settled");
        var refundedButPaid = Ours("Failed");
        var failedBoth = Ours("Failed");
        OutgoingPayment[] ours = [matched, timing, inFlight, missing, paidTwice, wrongAmount, refundedButPaid, failedBoth];
        NetworkSettlement[] theirs =
        [
            Theirs(matched), Theirs(timing), Theirs(paidTwice), Theirs(paidTwice), Theirs(wrongAmount, 11m),
            Theirs(refundedButPaid), new("SOMEONE-ELSES", 99m, DateTimeOffset.UtcNow),
        ];

        var report = Reconciliation.Reconcile(ours, theirs, scopedToOurs: false);

        var counts = report.Lines.ToDictionary(l => l.Category, l => l.Count);
        Assert.Equal(1, counts["Matched"]);
        Assert.Equal(1, counts["Timing: settled there, still pending here"]);
        Assert.Equal(1, counts["In flight: not yet in their records"]);
        Assert.Equal(1, counts["Settled here, missing from their records"]);
        Assert.Equal(1, counts["Paid more than once there"]);
        Assert.Equal(1, counts["Amounts differ"]);
        Assert.Equal(1, counts["Refunded here, but paid there"]);
        Assert.Equal(1, counts["Matched: failed on both sides"]);
        Assert.Equal(1, counts["Paid there, unknown here"]);
        Assert.Equal(report.Difference, report.ExplainedByBreaksAndTiming);
        Assert.Equal(5, report.Lines.Where(l => l.IsBreak).Sum(l => l.Count));
    }

    [Fact]
    public void Matching_records_reconcile_cleanly()
    {
        OutgoingPayment[] ours = [Ours("Settled"), Ours("Settled", 25m)];

        var report = Reconciliation.Reconcile(ours, [.. ours.Select(o => Theirs(o))], scopedToOurs: false);

        Assert.Equal(0m, report.Difference);
        Assert.All(report.Lines, l => Assert.False(l.IsBreak));
    }
}
