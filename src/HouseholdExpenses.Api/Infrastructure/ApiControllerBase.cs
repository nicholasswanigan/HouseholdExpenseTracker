using HouseholdExpenses.Api.Data;
using HouseholdExpenses.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HouseholdExpenses.Api.Infrastructure;

[ApiController]
[Authorize]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The authenticated user's id (the JWT "sub" claim).</summary>
    protected string UserId =>
        User.FindFirst("sub")?.Value ?? throw new InvalidOperationException("Authenticated request has no 'sub' claim.");

    protected NotFoundObjectResult HouseholdNotFound() =>
        NotFound(new ProblemDetails { Status = 404, Title = "Household not found" });
}

public static class Mappers
{
    public static ExpenseDto ToDto(this Expense e) => new(
        e.Id, e.Description, e.Amount, e.Date, e.Category, e.PayerId, e.Payer.DisplayName, e.SplitType,
        e.Splits.OrderBy(s => s.User.DisplayName)
            .Select(s => new ExpenseSplitDto(s.UserId, s.User.DisplayName, s.Amount)).ToList());

    public static SettlementDto ToDto(this Settlement s) => new(
        s.Id, s.FromUserId, s.FromUser.DisplayName, s.ToUserId, s.ToUser.DisplayName, s.Amount, s.Date, s.Note);
}
