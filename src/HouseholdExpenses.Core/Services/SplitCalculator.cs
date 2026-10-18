using HouseholdExpenses.Core.Contracts;

namespace HouseholdExpenses.Core.Services;

/// <summary>Thrown when user-supplied data violates a business rule.</summary>
public sealed class DomainValidationException(string message) : Exception(message);

/// <summary>Input for one participant of a split. <see cref="Value"/> is a percentage or exact amount.</summary>
public sealed record SplitInput(string UserId, decimal? Value);

/// <summary>
/// Converts a split specification into exact per-person amounts that always add up to the total.
/// Pure logic: no database, web, or framework dependencies.
/// </summary>
public static class SplitCalculator
{
    public static IReadOnlyList<ShareEntry> Calculate(decimal total, SplitType type, IReadOnlyList<SplitInput> inputs)
    {
        if (total <= 0) throw new DomainValidationException("Amount must be greater than zero.");
        if (Math.Round(total, 2) != total) throw new DomainValidationException("Amount can have at most 2 decimal places.");
        if (inputs is null || inputs.Count == 0) throw new DomainValidationException("At least one participant is required.");
        if (inputs.Select(i => i.UserId).Distinct().Count() != inputs.Count)
            throw new DomainValidationException("A participant can only appear once in a split.");

        var totalCents = (long)Math.Round(total * 100m);

        return type switch
        {
            SplitType.Equal => Allocate(totalCents, inputs.Select(i => (i.UserId, 1m)).ToList()),
            SplitType.Percentage => ByPercentage(totalCents, inputs),
            SplitType.Exact => ByExact(totalCents, inputs),
            _ => throw new DomainValidationException($"Unknown split type '{type}'.")
        };
    }

    private static IReadOnlyList<ShareEntry> ByPercentage(long totalCents, IReadOnlyList<SplitInput> inputs)
    {
        if (inputs.Any(i => i.Value is null or < 0))
            throw new DomainValidationException("Every participant needs a non-negative percentage.");
        if (inputs.Sum(i => i.Value!.Value) != 100m)
            throw new DomainValidationException("Percentages must add up to exactly 100.");

        return Allocate(totalCents, inputs.Select(i => (i.UserId, i.Value!.Value)).ToList());
    }

    private static IReadOnlyList<ShareEntry> ByExact(long totalCents, IReadOnlyList<SplitInput> inputs)
    {
        if (inputs.Any(i => i.Value is null or < 0))
            throw new DomainValidationException("Every participant needs a non-negative amount.");
        if (inputs.Any(i => Math.Round(i.Value!.Value, 2) != i.Value!.Value))
            throw new DomainValidationException("Amounts can have at most 2 decimal places.");

        var sumCents = inputs.Sum(i => (long)Math.Round(i.Value!.Value * 100m));
        if (sumCents != totalCents)
            throw new DomainValidationException(
                $"Exact amounts add up to {sumCents / 100m:0.00} but the expense is {totalCents / 100m:0.00}.");

        return inputs.Select(i => new ShareEntry(i.UserId, i.Value!.Value)).ToList();
    }

    /// <summary>
    /// Largest-remainder allocation: each person gets the floor of their weighted share in cents,
    /// then leftover cents go to those with the biggest fractional remainders. Guarantees the
    /// shares sum exactly to the total (e.g. $10.00 / 3 => 3.34, 3.33, 3.33).
    /// </summary>
    private static IReadOnlyList<ShareEntry> Allocate(long totalCents, IReadOnlyList<(string UserId, decimal Weight)> weights)
    {
        var weightSum = weights.Sum(w => w.Weight);
        var floors = new long[weights.Count];
        var remainders = new decimal[weights.Count];

        for (var i = 0; i < weights.Count; i++)
        {
            var exact = totalCents * weights[i].Weight / weightSum;
            floors[i] = (long)Math.Floor(exact);
            remainders[i] = exact - floors[i];
        }

        var leftover = totalCents - floors.Sum();
        var order = Enumerable.Range(0, weights.Count)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i) // stable: earlier participants win ties
            .Take((int)leftover);

        foreach (var i in order) floors[i]++;

        return weights.Select((w, i) => new ShareEntry(w.UserId, floors[i] / 100m)).ToList();
    }
}
