namespace Assessments.Toolkit;

public sealed record Account(int Id, string Holder, long Balance);

public static class Sorting
{
    // #region sort-keys
    // Highest balance first; ties by holder name, then by id. Tuples compare item by item.
    public static void SortForReport(List<Account> accounts) =>
        accounts.Sort((a, b) => (b.Balance, a.Holder, a.Id).CompareTo((a.Balance, b.Holder, b.Id)));
    // #endregion

    // #region stable-sort
    // List.Sort and Array.Sort are not stable: items that compare equal can change order.
    // LINQ's OrderBy is stable, so equal items keep their original order.
    public static List<Account> ByBalanceKeepingOrder(IEnumerable<Account> accounts) =>
        accounts.OrderByDescending(a => a.Balance).ToList();
    // #endregion
}
