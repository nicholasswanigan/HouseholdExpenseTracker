using System.Globalization;
using HouseholdExpenses.Api.Data;
using HouseholdExpenses.Api.Infrastructure;
using HouseholdExpenses.Core.Contracts;
using HouseholdExpenses.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HouseholdExpenses.Api.Controllers;

[Route("api/households/{householdId:guid}/expenses")]
public class ExpensesController(AppDbContext db, HouseholdAccess access, ILogger<ExpensesController> logger)
    : ApiControllerBase
{
    /// <summary>List expenses, newest first, with optional filters.</summary>
    /// <param name="householdId">Household id.</param>
    /// <param name="month">Only expenses in this month, formatted yyyy-MM.</param>
    /// <param name="category">Only this category.</param>
    /// <param name="userId">Only expenses this person paid or owes a share of.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    public async Task<ActionResult<List<ExpenseDto>>> List(
        Guid householdId, string? month, ExpenseCategory? category, string? userId, CancellationToken ct)
    {
        if (await access.GetMembershipAsync(householdId, UserId, ct) is null) return HouseholdNotFound();

        var query = db.Expenses.AsNoTracking().Where(e => e.HouseholdId == householdId);

        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid month", Detail = "Use the format yyyy-MM, e.g. 2026-03." });

            var start = DateOnly.FromDateTime(parsed);
            var end = start.AddMonths(1);
            query = query.Where(e => e.Date >= start && e.Date < end);
        }

        if (category is not null) query = query.Where(e => e.Category == category);
        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(e => e.PayerId == userId || e.Splits.Any(s => s.UserId == userId));

        var expenses = await query
            .Include(e => e.Payer)
            .Include(e => e.Splits).ThenInclude(s => s.User)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAtUtc)
            .AsSplitQuery()
            .ToListAsync(ct);

        return expenses.Select(e => e.ToDto()).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExpenseDto>> Get(Guid householdId, Guid id, CancellationToken ct)
    {
        if (await access.GetMembershipAsync(householdId, UserId, ct) is null) return HouseholdNotFound();

        var expense = await LoadAsync(householdId, id, tracking: false, ct);
        return expense is null ? NotFound() : expense.ToDto();
    }

    /// <summary>Log a shared expense. Payer and participants must all be household members.</summary>
    [HttpPost]
    [ProducesResponseType<ExpenseDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ExpenseDto>> Create(Guid householdId, SaveExpenseRequest request, CancellationToken ct)
    {
        if (await access.GetMembershipAsync(householdId, UserId, ct) is null) return HouseholdNotFound();

        var expense = new Expense { HouseholdId = householdId, CreatedById = UserId };
        await ApplyAsync(expense, request, ct);

        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Expense {ExpenseId} ({Amount}) added to household {HouseholdId} by {UserId}",
            expense.Id, expense.Amount, householdId, UserId);

        var saved = await LoadAsync(householdId, expense.Id, tracking: false, ct);
        return CreatedAtAction(nameof(Get), new { householdId, id = expense.Id }, saved!.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExpenseDto>> Update(Guid householdId, Guid id, SaveExpenseRequest request, CancellationToken ct)
    {
        if (await access.GetMembershipAsync(householdId, UserId, ct) is null) return HouseholdNotFound();

        var expense = await LoadAsync(householdId, id, tracking: true, ct);
        if (expense is null) return NotFound();

        db.ExpenseSplits.RemoveRange(expense.Splits);
        expense.Splits = [];
        await ApplyAsync(expense, request, ct);
        // The new splits have client-generated Guid keys, so EF would assume they already exist; mark them Added.
        db.ExpenseSplits.AddRange(expense.Splits);
        await db.SaveChangesAsync(ct);

        var saved = await LoadAsync(householdId, id, tracking: false, ct);
        return saved!.ToDto();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid householdId, Guid id, CancellationToken ct)
    {
        if (await access.GetMembershipAsync(householdId, UserId, ct) is null) return HouseholdNotFound();

        var deleted = await db.Expenses.Where(e => e.HouseholdId == householdId && e.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0) return NotFound();

        logger.LogInformation("Expense {ExpenseId} deleted from household {HouseholdId} by {UserId}", id, householdId, UserId);
        return NoContent();
    }

    private async Task ApplyAsync(Expense expense, SaveExpenseRequest request, CancellationToken ct)
    {
        var memberIds = await access.GetMemberIdsAsync(expense.HouseholdId, ct);
        if (!memberIds.Contains(request.PayerId))
            throw new DomainValidationException("The payer must be a member of the household.");
        if (request.Participants.Any(p => !memberIds.Contains(p.UserId)))
            throw new DomainValidationException("Everyone in the split must be a member of the household.");

        var shares = SplitCalculator.Calculate(
            request.Amount, request.SplitType,
            request.Participants.Select(p => new SplitInput(p.UserId, p.Value)).ToList());

        expense.Description = request.Description.Trim();
        expense.Amount = request.Amount;
        expense.Date = request.Date;
        expense.Category = request.Category;
        expense.PayerId = request.PayerId;
        expense.SplitType = request.SplitType;
        expense.Splits = shares.Select(s => new ExpenseSplit { UserId = s.UserId, Amount = s.Amount }).ToList();
    }

    private Task<Expense?> LoadAsync(Guid householdId, Guid id, bool tracking, CancellationToken ct)
    {
        var query = db.Expenses
            .Include(e => e.Payer)
            .Include(e => e.Splits).ThenInclude(s => s.User)
            .AsSplitQuery();
        if (!tracking) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(e => e.HouseholdId == householdId && e.Id == id, ct);
    }
}
