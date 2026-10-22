namespace Fnb.Domain.Orders;

public sealed record PricingLine(decimal UnitPrice, decimal AddOnUnitTotal, int Quantity)
{
    /// <summary>Line total = (snapshot unit price + valid add-ons per unit) × quantity.</summary>
    public decimal LineTotal => (UnitPrice + AddOnUnitTotal) * Quantity;
}

public sealed record PricingPolicy(decimal TaxRatePercent, bool PricesIncludeTax, int RoundingUnit)
{
    public static PricingPolicy NoTax { get; } = new(0m, true, 1);

    public string Describe() =>
        $"order=subtotal>discount>tax>rounding;tax={TaxRatePercent}%;{(PricesIncludeTax ? "inclusive" : "exclusive")};round={RoundingUnit}";
}

public sealed record PricingResult(
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal RoundingAmount,
    decimal TotalAmount,
    string RuleSnapshot);

/// <summary>
/// Server-side price calculation. Order of application (configurable policy, fixed sequence):
/// 1. subtotal = Σ line totals
/// 2. discount (capped at subtotal)
/// 3. tax on (subtotal − discount): exclusive adds tax; inclusive extracts the tax portion
/// 4. total rounded to policy.RoundingUnit (VND, midpoint away from zero)
/// </summary>
public static class PricingCalculator
{
    public static PricingResult Calculate(IEnumerable<PricingLine> lines, decimal discount, PricingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.TaxRatePercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(policy), "Tax rate must be between 0 and 100.");
        }

        if (discount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(discount), "Discount cannot be negative.");
        }

        var subtotal = lines.Sum(l => l.LineTotal);
        var appliedDiscount = Math.Min(discount, subtotal);
        var net = subtotal - appliedDiscount;

        decimal tax;
        decimal gross;
        if (policy.PricesIncludeTax)
        {
            tax = policy.TaxRatePercent == 0 ? 0 : Math.Round(net * policy.TaxRatePercent / (100 + policy.TaxRatePercent), 0, MidpointRounding.AwayFromZero);
            gross = net;
        }
        else
        {
            tax = Math.Round(net * policy.TaxRatePercent / 100, 0, MidpointRounding.AwayFromZero);
            gross = net + tax;
        }

        var unit = Math.Max(1, policy.RoundingUnit);
        var total = Math.Round(gross / unit, 0, MidpointRounding.AwayFromZero) * unit;
        var rounding = total - gross;

        return new PricingResult(subtotal, appliedDiscount, tax, rounding, total, policy.Describe());
    }
}
