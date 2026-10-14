using Bank.Money;
using MoneyAmount = Bank.Money.Money;

namespace Bank.Api.Tests;

/// <summary>B3 stage 5: a journal entry always balances, and is corrected by reversal.</summary>
public sealed class JournalEntryTests
{
    private static Posting P(string account, decimal amount) => new(account, new MoneyAmount(amount, Currency.ZAR));

    [Fact]
    public void A_payment_with_a_fee_split_three_ways_balances()
    {
        var fee = new MoneyAmount(0.40m, Currency.ZAR).Allocate(50, 30, 20);
        var entry = new JournalEntry("Card payment: coffee, with fee",
        [
            P("customer:thandi", -80.40m), P("merchant:coffee", 80.00m),
            new("bank:fees", fee[0]), new("partner:fees", fee[1]), new("tax:fees", fee[2]),
        ]);

        Assert.Equal(0m, entry.Postings.Sum(p => p.Amount.Amount));
    }

    [Fact]
    public void An_unbalanced_entry_cannot_be_created() =>
        Assert.Throws<ArgumentException>(() => new JournalEntry("Goodwill", [P("customer:thandi", 0.40m), P("bank:fees", -0.39m)]));

    [Fact]
    public void A_one_sided_entry_cannot_be_created() =>
        Assert.Throws<ArgumentException>(() => new JournalEntry("Goodwill", [P("customer:thandi", 0.40m)]));

    [Fact]
    public void Mixed_currencies_cannot_share_an_entry() =>
        Assert.Throws<ArgumentException>(() => new JournalEntry("Conversion",
            [P("customer:thandi", -100m), new("customer:thandi:yen", new MoneyAmount(100m, Currency.JPY))]));

    [Fact]
    public void A_reversal_cancels_the_original_and_points_back_to_it()
    {
        var original = new JournalEntry("Transfer: rent share", [P("customer:thandi", -250m), P("customer:johan", 250m)]);

        var reversal = original.Reverse("Sent to the wrong account");

        Assert.Equal(original.Id, reversal.Corrects);
        var net = original.Postings.Concat(reversal.Postings)
            .GroupBy(p => p.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount.Amount));
        Assert.All(net.Values, total => Assert.Equal(0m, total));
    }
}
