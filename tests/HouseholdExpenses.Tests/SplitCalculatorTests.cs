using HouseholdExpenses.Core.Contracts;
using HouseholdExpenses.Core.Services;

namespace HouseholdExpenses.Tests;

public class SplitCalculatorTests
{
    private static SplitInput[] People(params string[] ids) => ids.Select(i => new SplitInput(i, null)).ToArray();

    [Fact]
    public void Equal_DividesEvenly()
    {
        var shares = SplitCalculator.Calculate(30m, SplitType.Equal, People("A", "B", "C"));
        Assert.All(shares, s => Assert.Equal(10m, s.Amount));
    }

    [Fact]
    public void Equal_DistributesRemainderCentsAndSumsToTotal()
    {
        var shares = SplitCalculator.Calculate(10m, SplitType.Equal, People("A", "B", "C"));

        Assert.Equal(10m, shares.Sum(s => s.Amount));
        Assert.Equal([3.34m, 3.33m, 3.33m], shares.Select(s => s.Amount));
    }

    [Theory]
    [InlineData(0.01, 3)]
    [InlineData(99.99, 7)]
    [InlineData(100, 3)]
    [InlineData(1234.56, 11)]
    public void Equal_AlwaysSumsToTotal(double total, int people)
    {
        var ids = Enumerable.Range(0, people).Select(i => $"U{i}").ToArray();
        var shares = SplitCalculator.Calculate((decimal)total, SplitType.Equal, People(ids));
        Assert.Equal((decimal)total, shares.Sum(s => s.Amount));
    }

    [Fact]
    public void Percentage_SplitsByWeightAndSumsToTotal()
    {
        var inputs = new[] { new SplitInput("A", 50m), new SplitInput("B", 30m), new SplitInput("C", 20m) };
        var shares = SplitCalculator.Calculate(200m, SplitType.Percentage, inputs);

        Assert.Equal([100m, 60m, 40m], shares.Select(s => s.Amount));
    }

    [Fact]
    public void Percentage_WithAwkwardNumbers_StillSumsToTotal()
    {
        var inputs = new[] { new SplitInput("A", 33.33m), new SplitInput("B", 33.33m), new SplitInput("C", 33.34m) };
        var shares = SplitCalculator.Calculate(99.99m, SplitType.Percentage, inputs);
        Assert.Equal(99.99m, shares.Sum(s => s.Amount));
    }

    [Fact]
    public void Percentage_NotSummingTo100_Throws()
    {
        var inputs = new[] { new SplitInput("A", 50m), new SplitInput("B", 40m) };
        Assert.Throws<DomainValidationException>(() => SplitCalculator.Calculate(100m, SplitType.Percentage, inputs));
    }

    [Fact]
    public void Exact_UsesGivenAmounts()
    {
        var inputs = new[] { new SplitInput("A", 70m), new SplitInput("B", 30m) };
        var shares = SplitCalculator.Calculate(100m, SplitType.Exact, inputs);
        Assert.Equal([70m, 30m], shares.Select(s => s.Amount));
    }

    [Fact]
    public void Exact_NotSummingToTotal_Throws()
    {
        var inputs = new[] { new SplitInput("A", 70m), new SplitInput("B", 20m) };
        var ex = Assert.Throws<DomainValidationException>(() => SplitCalculator.Calculate(100m, SplitType.Exact, inputs));
        Assert.Contains("90.00", ex.Message);
    }

    [Fact]
    public void Exact_MissingValue_Throws()
    {
        var inputs = new[] { new SplitInput("A", null), new SplitInput("B", 100m) };
        Assert.Throws<DomainValidationException>(() => SplitCalculator.Calculate(100m, SplitType.Exact, inputs));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveTotal_Throws(double total) =>
        Assert.Throws<DomainValidationException>(() => SplitCalculator.Calculate((decimal)total, SplitType.Equal, People("A")));

    [Fact]
    public void NoParticipants_Throws() =>
        Assert.Throws<DomainValidationException>(() => SplitCalculator.Calculate(10m, SplitType.Equal, []));

    [Fact]
    public void DuplicateParticipant_Throws() =>
        Assert.Throws<DomainValidationException>(() => SplitCalculator.Calculate(10m, SplitType.Equal, People("A", "A")));
}
