namespace Assessments.Set1;

// Worked solution. O(n) time: one dictionary lookup per payment, instead of comparing each
// payment with every earlier one. Payments arrive in time order, so the most recent earlier
// payment with the same payer, payee and amount is the only one that needs checking.
public static class DuplicatePayments
{
    public const long WindowSeconds = 60;

    public static IReadOnlyList<string> Find(IReadOnlyList<Payment> payments)
    {
        var lastSeen = new Dictionary<(string Payer, string Payee, long Amount), long>();
        var duplicates = new List<string>();

        foreach (var payment in payments)
        {
            var key = (payment.Payer, payment.Payee, payment.AmountCents);
            if (lastSeen.TryGetValue(key, out var previous) && payment.Timestamp - previous <= WindowSeconds)
            {
                duplicates.Add(payment.Id);
            }
            lastSeen[key] = payment.Timestamp;
        }

        return duplicates;
    }
}
