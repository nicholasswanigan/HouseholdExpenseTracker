using HouseholdExpenses.Core.Services;

namespace HouseholdExpenses.Tests;

public class DebtSimplifierTests
{
    private static Dictionary<string, decimal> B(params (string, decimal)[] items) =>
        items.ToDictionary(i => i.Item1, i => i.Item2);

    [Fact]
    public void EmptyOrAllZero_ReturnsNoTransfers()
    {
        Assert.Empty(DebtSimplifier.Simplify(B()));
        Assert.Empty(DebtSimplifier.Simplify(B(("A", 0), ("B", 0))));
    }

    [Fact]
    public void ChainOfDebts_IsCollapsedToOneDirectPayment()
    {
        // A owes B 10, B owes C 10  =>  net: A -10, B 0, C +10  =>  A pays C 10
        var result = DebtSimplifier.Simplify(B(("A", -10m), ("B", 0m), ("C", 10m)));

        var t = Assert.Single(result);
        Assert.Equal(new Transfer("A", "C", 10m), t);
    }

    [Fact]
    public void SingleDebtorSingleCreditor_OnePayment()
    {
        var result = DebtSimplifier.Simplify(B(("A", -25.50m), ("B", 25.50m)));
        Assert.Equal(new Transfer("A", "B", 25.50m), Assert.Single(result));
    }

    [Fact]
    public void GreedyMatchesLargestDebtorWithLargestCreditor()
    {
        var result = DebtSimplifier.Simplify(B(("A", -50m), ("B", -30m), ("C", 60m), ("D", 20m)));

        Assert.Equal(new Transfer("A", "C", 50m), result[0]);
        // C has 10 left, D has 20: the largest remaining creditor is now D.
        Assert.Equal(new Transfer("B", "D", 20m), result[1]);
        Assert.Equal(new Transfer("B", "C", 10m), result[2]);
    }

    [Fact]
    public void PaymentCountNeverExceedsPeopleWithBalancesMinusOne()
    {
        var balances = B(("A", -40m), ("B", -35m), ("C", -25m), ("D", 50m), ("E", 30m), ("F", 20m));
        var result = DebtSimplifier.Simplify(balances);
        Assert.True(result.Count <= balances.Count - 1);
    }

    [Fact]
    public void ApplyingTransfersLeavesEveryoneAtZero()
    {
        var balances = B(("A", -33.34m), ("B", -33.33m), ("C", 12.01m), ("D", 54.66m));
        var result = DebtSimplifier.Simplify(balances);

        var after = new Dictionary<string, decimal>(balances);
        foreach (var t in result)
        {
            after[t.FromUserId] += t.Amount;
            after[t.ToUserId] -= t.Amount;
        }
        Assert.All(after.Values, v => Assert.Equal(0m, v));
    }

    [Fact]
    public void AllTransferAmountsArePositive_AndNeverToSelf()
    {
        var result = DebtSimplifier.Simplify(B(("A", -5m), ("B", -5m), ("C", 4m), ("D", 6m)));
        Assert.All(result, t =>
        {
            Assert.True(t.Amount > 0);
            Assert.NotEqual(t.FromUserId, t.ToUserId);
        });
    }

    [Fact]
    public void IsDeterministicOnTies()
    {
        var a = DebtSimplifier.Simplify(B(("A", -10m), ("B", -10m), ("C", 10m), ("D", 10m)));
        var b = DebtSimplifier.Simplify(B(("D", 10m), ("C", 10m), ("B", -10m), ("A", -10m)));
        Assert.Equal(a, b);
    }
}
