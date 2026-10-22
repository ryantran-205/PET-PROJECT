using Fnb.Api.Security;
using Fnb.Application.Inventory;
using Fnb.Application.Menu;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Fnb.Api.Endpoints;

public static class MenuInventoryEndpoints
{
    public static IEndpointRouteBuilder MapMenuInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        // ----- Organization-level menu catalog (read: staff; write: SuperAdmin enforced in service) -----
        var menu = app.MapGroup("/menu").WithTags("Menu").RequireAuthorization(Policies.Staff);
        menu.MapGet("/categories", (MenuService svc, CancellationToken ct) => svc.ListCategoriesAsync(ct));
        menu.MapPost("/categories", async Task<Created<CategoryDto>> (UpsertCategoryRequest r, MenuService svc, CancellationToken ct) =>
        {
            var c = await svc.CreateCategoryAsync(r, ct);
            return TypedResults.Created($"/api/v1/menu/categories/{c.Id}", c);
        }).RequireAuthorization(Policies.SuperAdmin);
        menu.MapPut("/categories/{id:guid}", (Guid id, UpsertCategoryRequest r, MenuService svc, CancellationToken ct) => svc.UpdateCategoryAsync(id, r, ct))
            .RequireAuthorization(Policies.SuperAdmin);

        menu.MapGet("/add-ons", (MenuService svc, CancellationToken ct) => svc.ListAddOnsAsync(ct));
        menu.MapPost("/add-ons", async Task<Created<AddOnDto>> (UpsertAddOnRequest r, MenuService svc, CancellationToken ct) =>
        {
            var a = await svc.CreateAddOnAsync(r, ct);
            return TypedResults.Created($"/api/v1/menu/add-ons/{a.Id}", a);
        }).RequireAuthorization(Policies.SuperAdmin);
        menu.MapPut("/add-ons/{id:guid}", (Guid id, UpsertAddOnRequest r, MenuService svc, CancellationToken ct) => svc.UpdateAddOnAsync(id, r, ct))
            .RequireAuthorization(Policies.SuperAdmin);

        menu.MapGet("/items", ([AsParameters] MenuItemQuery q, MenuService svc, CancellationToken ct) => svc.ListItemsAsync(q, ct));
        menu.MapPost("/items", async Task<Created<MenuItemDto>> (UpsertMenuItemRequest r, MenuService svc, CancellationToken ct) =>
        {
            var i = await svc.CreateItemAsync(r, ct);
            return TypedResults.Created($"/api/v1/menu/items/{i.Id}", i);
        }).RequireAuthorization(Policies.SuperAdmin);
        menu.MapGet("/items/{itemId:guid}", (Guid itemId, MenuService svc, CancellationToken ct) => svc.GetItemAsync(itemId, ct));
        menu.MapPut("/items/{itemId:guid}", (Guid itemId, UpsertMenuItemRequest r, MenuService svc, CancellationToken ct) => svc.UpdateItemAsync(itemId, r, ct))
            .RequireAuthorization(Policies.SuperAdmin);

        menu.MapGet("/variants/{variantId:guid}/recipe", (Guid variantId, MenuService svc, CancellationToken ct) => svc.GetRecipeAsync(variantId, null, ct));
        menu.MapPut("/variants/{variantId:guid}/recipe", (Guid variantId, UpdateRecipeRequest r, MenuService svc, CancellationToken ct) =>
            svc.UpdateRecipeAsync(variantId, null, r, ct)).RequireAuthorization(Policies.SuperAdmin);
        menu.MapGet("/add-ons/{addOnId:guid}/recipe", (Guid addOnId, MenuService svc, CancellationToken ct) => svc.GetRecipeAsync(null, addOnId, ct));
        menu.MapPut("/add-ons/{addOnId:guid}/recipe", (Guid addOnId, UpdateRecipeRequest r, MenuService svc, CancellationToken ct) =>
            svc.UpdateRecipeAsync(null, addOnId, r, ct)).RequireAuthorization(Policies.SuperAdmin);

        // ----- Ingredient catalog -----
        var ingredients = app.MapGroup("/ingredients").WithTags("Inventory").RequireAuthorization(Policies.Staff);
        ingredients.MapGet("/", (InventoryService svc, CancellationToken ct) => svc.ListIngredientsAsync(ct));
        ingredients.MapPost("/", async Task<Created<IngredientDto>> (UpsertIngredientRequest r, InventoryService svc, CancellationToken ct) =>
        {
            var i = await svc.CreateIngredientAsync(r, ct);
            return TypedResults.Created($"/api/v1/ingredients/{i.Id}", i);
        }).RequireAuthorization(Policies.SuperAdmin);
        ingredients.MapPut("/{id:guid}", (Guid id, UpsertIngredientRequest r, InventoryService svc, CancellationToken ct) => svc.UpdateIngredientAsync(id, r, ct))
            .RequireAuthorization(Policies.SuperAdmin);

        // ----- Branch-scoped menu & inventory -----
        var branch = app.MapGroup("/branches/{branchId:guid}").RequireAuthorization(Policies.Staff);
        branch.MapGet("/menu", (Guid branchId, MenuService svc, CancellationToken ct) => svc.GetBranchMenuAsync(branchId, ct)).WithTags("Menu");
        branch.MapPut("/menu", (Guid branchId, UpdateBranchMenuRequest r, MenuService svc, CancellationToken ct) => svc.UpdateBranchMenuAsync(branchId, r, ct))
            .WithTags("Menu").RequireAuthorization(Policies.BranchAdmin).WithSummary("Bật/tắt món và giá riêng theo chi nhánh.");

        var inv = branch.MapGroup("").WithTags("Inventory");
        inv.MapGet("/ingredients", (Guid branchId, InventoryService svc, CancellationToken ct) => svc.ListStockAsync(branchId, false, ct));
        inv.MapPost("/ingredients", (Guid branchId, ConfigureBranchIngredientRequest r, InventoryService svc, CancellationToken ct) =>
            svc.ConfigureIngredientAsync(branchId, r, ct)).RequireAuthorization(Policies.BranchAdmin).WithSummary("Thêm nguyên liệu vào kho chi nhánh / cập nhật ngưỡng tồn tối thiểu.");
        inv.MapGet("/inventory", (Guid branchId, InventoryService svc, CancellationToken ct) => svc.ListStockAsync(branchId, false, ct));
        inv.MapGet("/inventory/low-stock", (Guid branchId, InventoryService svc, CancellationToken ct) => svc.ListStockAsync(branchId, true, ct));
        inv.MapPost("/inventory/receipts", (Guid branchId, StockDocumentRequest r, InventoryService svc, CancellationToken ct) => Created(svc.ReceiveAsync(branchId, r, ct), branchId));
        inv.MapPost("/inventory/issues", (Guid branchId, StockDocumentRequest r, InventoryService svc, CancellationToken ct) => Created(svc.IssueAsync(branchId, r, ct), branchId));
        inv.MapPost("/inventory/adjustments", (Guid branchId, AdjustmentRequest r, InventoryService svc, CancellationToken ct) => Created(svc.AdjustAsync(branchId, r, ct), branchId))
            .RequireAuthorization(Policies.BranchAdmin);
        inv.MapPost("/inventory/stocktakes", (Guid branchId, StocktakeRequest r, InventoryService svc, CancellationToken ct) => Created(svc.StocktakeAsync(branchId, r, ct), branchId))
            .RequireAuthorization(Policies.BranchAdmin);
        inv.MapGet("/inventory/documents", (Guid branchId, int? page, int? pageSize, InventoryService svc, CancellationToken ct) =>
            svc.ListDocumentsAsync(branchId, page ?? 1, pageSize ?? 50, ct));
        inv.MapGet("/inventory/ledger", (Guid branchId, [AsParameters] LedgerQuery q, InventoryService svc, CancellationToken ct) => svc.LedgerAsync(branchId, q, ct));
        return app;
    }

    private static async Task<Created<InventoryDocumentDto>> Created(Task<InventoryDocumentDto> task, Guid branchId)
    {
        var doc = await task;
        return TypedResults.Created($"/api/v1/branches/{branchId}/inventory/documents/{doc.Id}", doc);
    }
}
