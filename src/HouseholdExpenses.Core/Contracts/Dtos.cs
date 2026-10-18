using System.ComponentModel.DataAnnotations;

namespace HouseholdExpenses.Core.Contracts;

// ---- Auth ----
public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password,
    [Required, StringLength(60, MinimumLength = 1)] string DisplayName);

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public sealed record UserDto(string Id, string Email, string DisplayName);

public sealed record AuthResponse(string Token, DateTime ExpiresAtUtc, UserDto User);

// ---- Households ----
public sealed record CreateHouseholdRequest(
    [Required, StringLength(80, MinimumLength = 1)] string Name);

public sealed record JoinHouseholdRequest(
    [Required, StringLength(32, MinimumLength = 4)] string InviteCode);

public sealed record HouseholdSummaryDto(Guid Id, string Name, int MemberCount, HouseholdRole MyRole);

public sealed record MemberDto(string UserId, string DisplayName, string Email, HouseholdRole Role);

public sealed record HouseholdDetailDto(
    Guid Id, string Name, string InviteCode, HouseholdRole MyRole, IReadOnlyList<MemberDto> Members);

// ---- Expenses ----
public sealed record SplitParticipantRequest(
    [Required] string UserId,
    // Percentage (0-100) for Percentage splits, an amount for Exact splits, ignored for Equal.
    decimal? Value);

public sealed record SaveExpenseRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Description,
    [Range(0.01, 1_000_000)] decimal Amount,
    DateOnly Date,
    ExpenseCategory Category,
    [Required] string PayerId,
    SplitType SplitType,
    [Required, MinLength(1)] List<SplitParticipantRequest> Participants);

public sealed record ExpenseSplitDto(string UserId, string DisplayName, decimal Amount);

public sealed record ExpenseDto(
    Guid Id,
    string Description,
    decimal Amount,
    DateOnly Date,
    ExpenseCategory Category,
    string PayerId,
    string PayerName,
    SplitType SplitType,
    IReadOnlyList<ExpenseSplitDto> Splits);

// ---- Settlements ----
public sealed record SaveSettlementRequest(
    [Required] string FromUserId,
    [Required] string ToUserId,
    [Range(0.01, 1_000_000)] decimal Amount,
    DateOnly Date,
    [StringLength(200)] string? Note);

public sealed record SettlementDto(
    Guid Id, string FromUserId, string FromName, string ToUserId, string ToName,
    decimal Amount, DateOnly Date, string? Note);

// ---- Balances ----
public sealed record BalanceDto(string UserId, string DisplayName, decimal Net);

public sealed record SuggestedPaymentDto(
    string FromUserId, string FromName, string ToUserId, string ToName, decimal Amount);

public sealed record BalanceSummaryDto(
    IReadOnlyList<BalanceDto> Balances, IReadOnlyList<SuggestedPaymentDto> SuggestedPayments);

