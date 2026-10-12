namespace Assessments.Set1;

// Input for problem 2. Timestamp is in seconds; payments arrive in time order.
public sealed record Payment(string Id, string Payer, string Payee, long AmountCents, long Timestamp);
