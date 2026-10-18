namespace HouseholdExpenses.Core.Contracts;

public enum SplitType
{
    Equal,
    Percentage,
    Exact
}

public enum ExpenseCategory
{
    Groceries,
    Rent,
    Utilities,
    Internet,
    Household,
    Dining,
    Entertainment,
    Transport,
    Other
}

public enum HouseholdRole
{
    Member,
    Owner
}
