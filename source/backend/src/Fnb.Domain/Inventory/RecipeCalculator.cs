namespace Fnb.Domain.Inventory;

public sealed record RecipeComponent(Guid IngredientId, decimal QuantityPerUnit);

public sealed record ConsumptionLine(Guid? VariantId, IReadOnlyList<Guid> AddOnIds, int Quantity);

public static class RecipeCalculator
{
    /// <summary>
    /// Aggregates total ingredient requirement for a set of order lines.
    /// required(ingredient) = Σ_lines quantity × (variantRecipe(ingredient) + Σ_addOns addOnRecipe(ingredient)).
    /// </summary>
    public static IReadOnlyDictionary<Guid, decimal> Requirements(
        IEnumerable<ConsumptionLine> lines,
        IReadOnlyDictionary<Guid, IReadOnlyList<RecipeComponent>> variantRecipes,
        IReadOnlyDictionary<Guid, IReadOnlyList<RecipeComponent>> addOnRecipes)
    {
        var result = new Dictionary<Guid, decimal>();

        void Add(IReadOnlyList<RecipeComponent>? components, int qty)
        {
            if (components is null)
            {
                return;
            }

            foreach (var c in components)
            {
                result[c.IngredientId] = result.GetValueOrDefault(c.IngredientId) + (c.QuantityPerUnit * qty);
            }
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                continue;
            }

            if (line.VariantId is { } v)
            {
                Add(variantRecipes.GetValueOrDefault(v), line.Quantity);
            }

            foreach (var a in line.AddOnIds)
            {
                Add(addOnRecipes.GetValueOrDefault(a), line.Quantity);
            }
        }

        return result;
    }

    /// <summary>Returns ingredient shortfalls (required − available) where stock is insufficient.</summary>
    public static IReadOnlyDictionary<Guid, decimal> Shortfalls(
        IReadOnlyDictionary<Guid, decimal> required,
        IReadOnlyDictionary<Guid, decimal> available)
    {
        var shortfalls = new Dictionary<Guid, decimal>();
        foreach (var (ingredientId, qty) in required)
        {
            var onHand = available.GetValueOrDefault(ingredientId);
            if (onHand < qty)
            {
                shortfalls[ingredientId] = qty - onHand;
            }
        }

        return shortfalls;
    }
}
