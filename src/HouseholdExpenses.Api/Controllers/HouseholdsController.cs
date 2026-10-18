using System.Security.Cryptography;
using HouseholdExpenses.Api.Data;
using HouseholdExpenses.Api.Infrastructure;
using HouseholdExpenses.Core.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HouseholdExpenses.Api.Controllers;

[Route("api/households")]
public class HouseholdsController(
    AppDbContext db,
    HouseholdAccess access,
    BalanceService balances,
    ILogger<HouseholdsController> logger) : ApiControllerBase
{
    // No 0/O/1/I so codes are easy to read out loud.
    private const string InviteAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>List the households the caller belongs to.</summary>
    [HttpGet]
    public async Task<ActionResult<List<HouseholdSummaryDto>>> List(CancellationToken ct)
    {
        var userId = UserId;
        var items = await db.HouseholdMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Household.Name)
            .Select(m => new HouseholdSummaryDto(m.HouseholdId, m.Household.Name, m.Household.Members.Count, m.Role))
            .ToListAsync(ct);
        return items;
    }

    /// <summary>Create a household; the caller becomes its owner.</summary>
    [HttpPost]
    [ProducesResponseType<HouseholdDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<HouseholdDetailDto>> Create(CreateHouseholdRequest request, CancellationToken ct)
    {
        var household = new Household
        {
            Name = request.Name.Trim(),
            InviteCode = await GenerateUniqueInviteCodeAsync(ct)
        };
        household.Members.Add(new HouseholdMember { UserId = UserId, Role = HouseholdRole.Owner });

        db.Households.Add(household);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Household {HouseholdId} created by {UserId}", household.Id, UserId);

        var dto = await LoadDetailAsync(household.Id, UserId, ct);
        return CreatedAtAction(nameof(Get), new { id = household.Id }, dto);
    }

    /// <summary>Join a household using its invite code.</summary>
    [HttpPost("join")]
    [ProducesResponseType<HouseholdDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HouseholdDetailDto>> Join(JoinHouseholdRequest request, CancellationToken ct)
    {
        var code = request.InviteCode.Trim().ToUpperInvariant();
        var household = await db.Households.Include(h => h.Members).FirstOrDefaultAsync(h => h.InviteCode == code, ct);
        if (household is null)
            return NotFound(new ProblemDetails { Status = 404, Title = "That invite code doesn't match any household" });

        if (household.Members.All(m => m.UserId != UserId))
        {
            household.Members.Add(new HouseholdMember { UserId = UserId, Role = HouseholdRole.Member });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("User {UserId} joined household {HouseholdId}", UserId, household.Id);
        }

        return (await LoadDetailAsync(household.Id, UserId, ct))!;
    }

    /// <summary>Get a household, including members and the invite code.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<HouseholdDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var dto = await LoadDetailAsync(id, UserId, ct);
        return dto is null ? HouseholdNotFound() : dto;
    }

    /// <summary>Rename a household (owner only).</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, CreateHouseholdRequest request, CancellationToken ct)
    {
        var membership = await access.GetMembershipAsync(id, UserId, ct);
        if (membership is null) return HouseholdNotFound();
        if (membership.Role != HouseholdRole.Owner) return Forbid();

        var household = await db.Households.FindAsync([id], ct);
        household!.Name = request.Name.Trim();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Delete a household and all its data (owner only).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var membership = await access.GetMembershipAsync(id, UserId, ct);
        if (membership is null) return HouseholdNotFound();
        if (membership.Role != HouseholdRole.Owner) return Forbid();

        await db.Households.Where(h => h.Id == id).ExecuteDeleteAsync(ct);
        logger.LogInformation("Household {HouseholdId} deleted by {UserId}", id, UserId);
        return NoContent();
    }

    /// <summary>Leave a household. Only allowed once the caller's balance is settled.</summary>
    [HttpDelete("{id:guid}/members/me")]
    public async Task<IActionResult> Leave(Guid id, CancellationToken ct)
    {
        var membership = await access.GetMembershipAsync(id, UserId, ct);
        if (membership is null) return HouseholdNotFound();

        var net = (await balances.GetNetBalancesAsync(id, ct)).GetValueOrDefault(UserId);
        if (net != 0)
            return BadRequest(new ProblemDetails
            {
                Status = 400,
                Title = "Settle up before leaving",
                Detail = $"Your balance is {net:0.00}. Record settlements until it is 0.00, then leave."
            });

        var others = await db.HouseholdMembers
            .Where(m => m.HouseholdId == id && m.UserId != UserId)
            .OrderBy(m => m.JoinedAtUtc).ToListAsync(ct);

        if (others.Count == 0)
        {
            await db.Households.Where(h => h.Id == id).ExecuteDeleteAsync(ct);
        }
        else
        {
            // Never leave a household without an owner.
            if (membership.Role == HouseholdRole.Owner && others.All(m => m.Role != HouseholdRole.Owner))
                others[0].Role = HouseholdRole.Owner;

            await db.HouseholdMembers.Where(m => m.HouseholdId == id && m.UserId == UserId).ExecuteDeleteAsync(ct);
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("User {UserId} left household {HouseholdId}", UserId, id);
        return NoContent();
    }

    private async Task<HouseholdDetailDto?> LoadDetailAsync(Guid id, string userId, CancellationToken ct)
    {
        var household = await db.Households.AsNoTracking()
            .Include(h => h.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(h => h.Id == id && h.Members.Any(m => m.UserId == userId), ct);
        if (household is null) return null;

        var me = household.Members.First(m => m.UserId == userId);
        return new HouseholdDetailDto(
            household.Id, household.Name, household.InviteCode, me.Role,
            household.Members.OrderBy(m => m.User.DisplayName)
                .Select(m => new MemberDto(m.UserId, m.User.DisplayName, m.User.Email ?? "", m.Role)).ToList());
    }

    private async Task<string> GenerateUniqueInviteCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = string.Create(8, 0, (span, _) =>
            {
                for (var i = 0; i < span.Length; i++)
                    span[i] = InviteAlphabet[RandomNumberGenerator.GetInt32(InviteAlphabet.Length)];
            });
            if (!await db.Households.AnyAsync(h => h.InviteCode == code, ct)) return code;
        }
        throw new InvalidOperationException("Could not generate a unique invite code.");
    }
}
