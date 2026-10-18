using HouseholdExpenses.Core.Contracts;
using Microsoft.AspNetCore.Identity;

namespace HouseholdExpenses.Api.Data;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public List<HouseholdMember> Memberships { get; set; } = [];
}

public class Household
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string InviteCode { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<HouseholdMember> Members { get; set; } = [];
    public List<Expense> Expenses { get; set; } = [];
    public List<Settlement> Settlements { get; set; } = [];
}

/// <summary>Join entity for the User to Household many-to-many relationship.</summary>
public class HouseholdMember
{
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public HouseholdRole Role { get; set; } = HouseholdRole.Member;
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public string PayerId { get; set; } = string.Empty;
    public ApplicationUser Payer { get; set; } = null!;

    public string CreatedById { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public ExpenseCategory Category { get; set; }
    public SplitType SplitType { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<ExpenseSplit> Splits { get; set; } = [];
}

/// <summary>How much of one expense a particular user owes.</summary>
public class ExpenseSplit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ExpenseId { get; set; }
    public Expense Expense { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public decimal Amount { get; set; }
}

/// <summary>A real-world payment from one member to another.</summary>
public class Settlement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    public string FromUserId { get; set; } = string.Empty;
    public ApplicationUser FromUser { get; set; } = null!;

    public string ToUserId { get; set; } = string.Empty;
    public ApplicationUser ToUser { get; set; } = null!;

    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
