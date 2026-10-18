namespace HouseholdExpenses.Core.Services;

/// <summary>A suggested payment from one member to another.</summary>
public sealed record Transfer(string FromUserId, string ToUserId, decimal Amount);

/// <summary>
/// Turns net balances into the smallest practical set of payments.
/// Pure logic: no database, web, or framework dependencies.
/// </summary>
/// <remarks>
/// Balance convention: positive = the member is owed money (creditor),
/// negative = the member owes money (debtor). Balances should sum to zero.
/// Greedy algorithm: repeatedly match the largest debtor with the largest creditor and settle
/// min(debt, credit). Each payment fully clears at least one person, so a group of n people
/// with non-zero balances needs at most n - 1 payments.
/// </remarks>
public static class DebtSimplifier
{
    public static IReadOnlyList<Transfer> Simplify(IReadOnlyDictionary<string, decimal> balances)
    {
        ArgumentNullException.ThrowIfNull(balances);

        var debtors = new List<(string Id, decimal Amount)>();
        var creditors = new List<(string Id, decimal Amount)>();

        foreach (var (id, raw) in balances)
        {
            var balance = Math.Round(raw, 2, MidpointRounding.AwayFromZero);
            if (balance < 0) debtors.Add((id, -balance));
            else if (balance > 0) creditors.Add((id, balance));
        }

        var transfers = new List<Transfer>();

        while (debtors.Count > 0 && creditors.Count > 0)
        {
            // Ties broken by id so output is deterministic.
            var d = IndexOfLargest(debtors);
            var c = IndexOfLargest(creditors);

            var amount = Math.Min(debtors[d].Amount, creditors[c].Amount);
            transfers.Add(new Transfer(debtors[d].Id, creditors[c].Id, amount));

            var remainingDebt = debtors[d].Amount - amount;
            var remainingCredit = creditors[c].Amount - amount;

            if (remainingDebt == 0) debtors.RemoveAt(d); else debtors[d] = (debtors[d].Id, remainingDebt);
            if (remainingCredit == 0) creditors.RemoveAt(c); else creditors[c] = (creditors[c].Id, remainingCredit);
        }

        // Any leftover would only be a rounding residue in unbalanced input; it is intentionally ignored.
        return transfers;
    }

    private static int IndexOfLargest(List<(string Id, decimal Amount)> list)
    {
        var best = 0;
        for (var i = 1; i < list.Count; i++)
        {
            if (list[i].Amount > list[best].Amount ||
                (list[i].Amount == list[best].Amount &&
                 string.CompareOrdinal(list[i].Id, list[best].Id) < 0))
            {
                best = i;
            }
        }
        return best;
    }
}
