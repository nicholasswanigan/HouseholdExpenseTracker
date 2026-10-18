using HouseholdExpenses.Api.Data;
using HouseholdExpenses.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HouseholdExpenses.Api.Infrastructure;

/// <summary>
/// Centralised "is this caller allowed to see this household?" check.
/// Every household-scoped endpoint goes through this so users can only see their own households.
/// </summary>
public class HouseholdAccess(AppDbContext db)
{
    /// <summary>Returns the caller's membership, or null if they aren't in the household (or it doesn't exist).</summary>
    public Task<HouseholdMember?> GetMembershipAsync(Guid householdId, string userId, CancellationToken ct = default) =>
        db.HouseholdMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.HouseholdId == householdId && m.UserId == userId, ct);

    public Task<bool> IsOwnerAsync(Guid householdId, string userId, CancellationToken ct = default) =>
        db.HouseholdMembers.AnyAsync(
            m => m.HouseholdId == householdId && m.UserId == userId && m.Role == HouseholdRole.Owner, ct);

    public async Task<HashSet<string>> GetMemberIdsAsync(Guid householdId, CancellationToken ct = default) =>
        (await db.HouseholdMembers.Where(m => m.HouseholdId == householdId)
            .Select(m => m.UserId).ToListAsync(ct)).ToHashSet();
}
