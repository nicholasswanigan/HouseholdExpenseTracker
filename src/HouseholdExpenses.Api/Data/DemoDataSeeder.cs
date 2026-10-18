using HouseholdExpenses.Core.Contracts;
using HouseholdExpenses.Core.Services;
using Microsoft.AspNetCore.Identity;

namespace HouseholdExpenses.Api.Data;

/// <summary>
/// Creates three fake users and a household with realistic history so the app can be explored right away.
/// Only runs when "Seed:DemoData" is true (it is in appsettings.Development.json). Safe to run repeatedly.
/// </summary>
public static class DemoDataSeeder
{
    public const string Password = "Demo1234!";
    public const string InviteCode = "DEMO2345";

    // example.com is reserved for documentation, so these can never belong to a real person.
    public static readonly (string Email, string Name)[] Users =
    [
        ("alex.rivera@example.com", "Alex Rivera"),
        ("jordan.lee@example.com", "Jordan Lee"),
        ("sam.patel@example.com", "Sam Patel")
    ];

    public static async Task SeedAsync(AppDbContext db, UserManager<ApplicationUser> userManager, ILogger logger)
    {
        if (await userManager.FindByEmailAsync(Users[0].Email) is not null) return;

        var people = new List<ApplicationUser>();
        foreach (var (email, name) in Users)
        {
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = name };
            var result = await userManager.CreateAsync(user, Password);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not create demo user {email}: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            people.Add(user);
        }
        var (alex, jordan, sam) = (people[0], people[1], people[2]);

        var household = new Household { Name = "Maple Street Apartment", InviteCode = InviteCode };
        household.Members.Add(new HouseholdMember { UserId = alex.Id, Role = HouseholdRole.Owner });
        household.Members.Add(new HouseholdMember { UserId = jordan.Id });
        household.Members.Add(new HouseholdMember { UserId = sam.Id });
        db.Households.Add(household);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var lastMonth = thisMonth.AddMonths(-1);

        void Add(string description, decimal amount, DateOnly date, ExpenseCategory category,
            ApplicationUser payer, SplitType type, params (ApplicationUser User, decimal? Value)[] participants)
        {
            var shares = SplitCalculator.Calculate(amount, type,
                participants.Select(p => new SplitInput(p.User.Id, p.Value)).ToList());

            db.Expenses.Add(new Expense
            {
                Household = household,
                Description = description,
                Amount = amount,
                Date = date > today ? today : date, // don't create expenses dated in the future
                Category = category,
                PayerId = payer.Id,
                CreatedById = payer.Id,
                SplitType = type,
                Splits = shares.Select(s => new ExpenseSplit { UserId = s.UserId, Amount = s.Amount }).ToList()
            });
        }

        // Last month
        Add("Rent", 1800m, lastMonth, ExpenseCategory.Rent, alex, SplitType.Exact,
            (alex, 700m), (jordan, 600m), (sam, 500m));
        Add("Weekly groceries", 96.40m, lastMonth.AddDays(6), ExpenseCategory.Groceries, jordan, SplitType.Equal,
            (alex, null), (jordan, null), (sam, null));
        Add("Electric bill", 142.75m, lastMonth.AddDays(14), ExpenseCategory.Utilities, sam, SplitType.Equal,
            (alex, null), (jordan, null), (sam, null));

        // This month
        Add("Rent", 1800m, thisMonth, ExpenseCategory.Rent, alex, SplitType.Exact,
            (alex, 700m), (jordan, 600m), (sam, 500m));
        Add("Internet", 60m, thisMonth.AddDays(2), ExpenseCategory.Internet, alex, SplitType.Percentage,
            (alex, 50m), (jordan, 25m), (sam, 25m));
        Add("Groceries (Alex was away)", 84.20m, thisMonth.AddDays(4), ExpenseCategory.Groceries, sam, SplitType.Equal,
            (jordan, null), (sam, null));
        Add("Pizza night", 63m, thisMonth.AddDays(8), ExpenseCategory.Dining, jordan, SplitType.Equal,
            (alex, null), (jordan, null), (sam, null));

        db.Settlements.Add(new Settlement
        {
            Household = household,
            FromUserId = sam.Id,
            ToUserId = alex.Id,
            Amount = 300m,
            Date = lastMonth.AddDays(20),
            Note = "Cash"
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded demo data: {Count} users, household invite code {Code}", Users.Length, InviteCode);
    }
}
