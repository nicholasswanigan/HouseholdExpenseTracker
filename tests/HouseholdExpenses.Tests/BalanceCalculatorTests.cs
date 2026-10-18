using HouseholdExpenses.Core.Services;

namespace HouseholdExpenses.Tests;

public class BalanceCalculatorTests
{
    private static readonly string[] Members = ["A", "B", "C"];

    [Fact]
    public void NoActivity_EveryoneIsZero()
    {
        var result = BalanceCalculator.Calculate(Members, [], []);
        Assert.All(result.Values, v => Assert.Equal(0m, v));
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void PayerIsOwedTheOthersShares()
    {
        // A pays $30 for all three equally.
        var expense = new ExpenseEntry("A", 30m, [new("A", 10m), new("B", 10m), new("C", 10m)]);

        var result = BalanceCalculator.Calculate(Members, [expense], []);

        Assert.Equal(20m, result["A"]);
        Assert.Equal(-10m, result["B"]);
        Assert.Equal(-10m, result["C"]);
    }

    [Fact]
    public void PayerNotInSplit_IsOwedEverything()
    {
        var expense = new ExpenseEntry("A", 20m, [new("B", 12m), new("C", 8m)]);
        var result = BalanceCalculator.Calculate(Members, [expense], []);

        Assert.Equal(20m, result["A"]);
        Assert.Equal(-12m, result["B"]);
        Assert.Equal(-8m, result["C"]);
    }

    [Fact]
    public void Settlement_ReducesDebtAndCredit()
    {
        var expense = new ExpenseEntry("A", 30m, [new("A", 10m), new("B", 10m), new("C", 10m)]);
        var settlement = new SettlementEntry("B", "A", 10m);

        var result = BalanceCalculator.Calculate(Members, [expense], [settlement]);

        Assert.Equal(10m, result["A"]);
        Assert.Equal(0m, result["B"]);
        Assert.Equal(-10m, result["C"]);
    }

    [Fact]
    public void BalancesAlwaysSumToZero()
    {
        var expenses = new[]
        {
            new ExpenseEntry("A", 33.33m, [new("A", 11.11m), new("B", 11.11m), new("C", 11.11m)]),
            new ExpenseEntry("B", 80m, [new("A", 20m), new("C", 60m)]),
        };
        var settlements = new[] { new SettlementEntry("C", "B", 15.25m) };

        var result = BalanceCalculator.Calculate(Members, expenses, settlements);

        Assert.Equal(0m, result.Values.Sum());
    }

    [Fact]
    public void FullFlow_BalancesFeedSimplifier()
    {
        // A pays rent 90 split evenly; B pays groceries 30 split evenly.
        var expenses = new[]
        {
            new ExpenseEntry("A", 90m, [new("A", 30m), new("B", 30m), new("C", 30m)]),
            new ExpenseEntry("B", 30m, [new("A", 10m), new("B", 10m), new("C", 10m)]),
        };

        var balances = BalanceCalculator.Calculate(Members, expenses, []);
        var transfers = DebtSimplifier.Simplify(balances);

        // A: +90-40 = +50 ; B: +30-40 = -10 ; C: -40
        Assert.Equal(50m, balances["A"]);
        Assert.Equal(-10m, balances["B"]);
        Assert.Equal(-40m, balances["C"]);
        Assert.Equal(2, transfers.Count);
        Assert.Contains(new Transfer("C", "A", 40m), transfers);
        Assert.Contains(new Transfer("B", "A", 10m), transfers);
    }
}
