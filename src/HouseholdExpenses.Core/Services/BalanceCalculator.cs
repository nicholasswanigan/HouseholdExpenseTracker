namespace HouseholdExpenses.Core.Services;

/// <summary>One person's share of an expense.</summary>
public sealed record ShareEntry(string UserId, decimal Amount);

/// <summary>An expense reduced to what the balance calculation needs.</summary>
public sealed record ExpenseEntry(string PayerId, decimal Amount, IReadOnlyCollection<ShareEntry> Shares);

/// <summary>A payment from one member to another settling up debt.</summary>
public sealed record SettlementEntry(string FromUserId, string ToUserId, decimal Amount);

/// <summary>
/// Computes each member's net balance (positive = owed money, negative = owes money).
/// </summary>
public static class BalanceCalculator
{
    public static Dictionary<string, decimal> Calculate(
        IEnumerable<string> memberIds,
        IEnumerable<ExpenseEntry> expenses,
        IEnumerable<SettlementEntry> settlements)
    {
        ArgumentNullException.ThrowIfNull(memberIds);
        ArgumentNullException.ThrowIfNull(expenses);
        ArgumentNullException.ThrowIfNull(settlements);

        var balances = memberIds.Distinct().ToDictionary(id => id, _ => 0m);

        void Add(string id, decimal amount) =>
            balances[id] = (balances.TryGetValue(id, out var current) ? current : 0m) + amount;

        foreach (var expense in expenses)
        {
            // The payer fronted the whole amount...
            Add(expense.PayerId, expense.Amount);
            // ...and everyone (possibly including the payer) owes their share.
            foreach (var share in expense.Shares)
                Add(share.UserId, -share.Amount);
        }

        foreach (var s in settlements)
        {
            // Paying someone back reduces what you owe and what they are owed.
            Add(s.FromUserId, s.Amount);
            Add(s.ToUserId, -s.Amount);
        }

        foreach (var key in balances.Keys.ToList())
            balances[key] = Math.Round(balances[key], 2, MidpointRounding.AwayFromZero);

        return balances;
    }
}
