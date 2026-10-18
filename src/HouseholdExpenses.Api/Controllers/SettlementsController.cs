using HouseholdExpenses.Api.Data;
using HouseholdExpenses.Api.Infrastructure;
using HouseholdExpenses.Core.Contracts;
using HouseholdExpenses.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HouseholdExpenses.Api.Controllers;

[Route("api/households/{householdId:guid}")]
public class SettlementsController(
    AppDbContext db,
    HouseholdAccess access,
    BalanceService balances,
    ILogger<SettlementsController> logger) : ApiControllerBase
{
    /// <summary>Each member's net balance plus the fewest payments that would settle everyone up.</summary>
    [HttpGet("balances")]
    public async Task<ActionResult<BalanceSummaryDto>> GetBalances(Guid householdId, CancellationToken ct)
    {
        if (await access.GetMembershipAsync(householdId, UserId, ct) is null) return HouseholdNotFound();
        return await balances.GetSummaryAsync(householdId, ct);
    }

    [HttpGet("settlements")]
    public async Task<ActionResult<List<SettlementDto>>> List(Guid householdId, CancellationToken ct)
    {
        if (await access.GetMembershipAsync(householdId, UserId, ct) is null) return HouseholdNotFound();

        var items = await db.Settlements.AsNoTracking()
            .Where(s => s.HouseholdId == householdId)
            .Include(s => s.FromUser).Include(s => s.ToUser)
            .OrderByDescending(s => s.Date).ThenByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct);
        return items.Select(s => s.ToDto()).ToList();
    }

    /// <summary>Record that one member paid another back.</summary>
    [HttpPost("settlements")]
    [ProducesResponseType<SettlementDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SettlementDto>> Create(Guid householdId, SaveSettlementRequest request, CancellationToken ct)
    {
        if (await access.GetMembershipAsync(householdId, UserId, ct) is null) return HouseholdNotFound();

        if (request.FromUserId == request.ToUserId)
            throw new DomainValidationException("You can't settle up with yourself.");
        if (Math.Round(request.Amount, 2) != request.Amount)
            throw new DomainValidationException("Amount can have at most 2 decimal places.");

        var memberIds = await access.GetMemberIdsAsync(householdId, ct);
        if (!memberIds.Contains(request.FromUserId) || !memberIds.Contains(request.ToUserId))
            throw new DomainValidationException("Both people must be members of the household.");

        var settlement = new Settlement
        {
            HouseholdId = householdId,
            FromUserId = request.FromUserId,
            ToUserId = request.ToUserId,
            Amount = request.Amount,
            Date = request.Date,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };
        db.Settlements.Add(settlement);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Settlement {SettlementId} ({Amount}) recorded in household {HouseholdId} by {UserId}",
            settlement.Id, settlement.Amount, householdId, UserId);

        var saved = await db.Settlements.AsNoTracking()
            .Include(s => s.FromUser).Include(s => s.ToUser)
            .FirstAsync(s => s.Id == settlement.Id, ct);
        return CreatedAtAction(nameof(List), new { householdId }, saved.ToDto());
    }

    [HttpDelete("settlements/{id:guid}")]
    public async Task<IActionResult> Delete(Guid householdId, Guid id, CancellationToken ct)
    {
        if (await access.GetMembershipAsync(householdId, UserId, ct) is null) return HouseholdNotFound();

        var deleted = await db.Settlements.Where(s => s.HouseholdId == householdId && s.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? NotFound() : NoContent();
    }
}
