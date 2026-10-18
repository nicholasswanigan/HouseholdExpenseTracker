using System.Globalization;

namespace HouseholdExpenses.Web.Services;

/// <summary>Display helpers. Invariant culture is used on purpose: Blazor WASM ships without ICU data by default.</summary>
public static class Fmt
{
    // Change the symbol here to switch currency display app-wide.
    public const string CurrencySymbol = "$";

    public static string Money(decimal amount) =>
        (amount < 0 ? "-" : "") + CurrencySymbol + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
