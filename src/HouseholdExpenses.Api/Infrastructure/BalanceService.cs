using HouseholdExpenses.Api.Data;
using HouseholdExpenses.Core.Contracts;
using HouseholdExpenses.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace HouseholdExpenses.Api.Infrastructure;

/// <summary>Loads household data and feeds it to the pure calculators in the Core project.</summary>
public class BalanceService(AppDbContext db)
{
    public async Task<Dictionary<string, decimal>> GetNetBalancesAsync(Guid householdId, CancellationToken ct = default)
    {
        var memberIds = await db.HouseholdMembers.Where(m => m.HouseholdId == householdId)
            .Select(m => m.UserId).ToListAsync(ct);

        var expenses = (await db.Expenses.AsNoTracking()
                .Where(e => e.HouseholdId == householdId)
                .Select(e => new { e.PayerId, e.Amount, Splits = e.Splits.Select(s => new { s.UserId, s.Amount }).ToList() })
                .ToListAsync(ct))
            .Select(e => new ExpenseEntry(e.PayerId, e.Amount, e.Splits.Select(s => new ShareEntry(s.UserId, s.Amount)).ToList()))
            .ToList();

        var settlements = (await db.Settlements.AsNoTracking()
                .Where(s => s.HouseholdId == householdId)
                .Select(s => new { s.FromUserId, s.ToUserId, s.Amount })
                .ToListAsync(ct))
            .Select(s => new SettlementEntry(s.FromUserId, s.ToUserId, s.Amount))
            .ToList();

        return BalanceCalculator.Calculate(memberIds, expenses, settlements);
    }

    public async Task<BalanceSummaryDto> GetSummaryAsync(Guid householdId, CancellationToken ct = default)
    {
        var balances = await GetNetBalancesAsync(householdId, ct);
        var transfers = DebtSimplifier.Simplify(balances);

        var names = await db.Users.AsNoTracking()
            .Where(u => balances.Keys.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        string Name(string id) => names.GetValueOrDefault(id, "Unknown");

        return new BalanceSummaryDto(
            balances.Select(b => new BalanceDto(b.Key, Name(b.Key), b.Value))
                .OrderByDescending(b => b.Net).ThenBy(b => b.DisplayName).ToList(),
            transfers.Select(t => new SuggestedPaymentDto(t.FromUserId, Name(t.FromUserId), t.ToUserId, Name(t.ToUserId), t.Amount)).ToList());
    }
}
