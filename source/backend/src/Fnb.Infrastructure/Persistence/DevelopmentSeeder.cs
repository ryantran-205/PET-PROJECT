using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Hr;
using Fnb.Domain.Identity;
using Fnb.Domain.Inventory;
using Fnb.Domain.Menu;
using Fnb.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Fnb.Infrastructure.Persistence;

/// <summary>
/// DEVELOPMENT/DEMO SEED ONLY. Creates a demo organization, two branches, users, employees, shifts, a small menu with
/// recipes and opening stock (opening stock is written through the ledger so balances reconcile). Never runs unless
/// explicitly invoked (<c>--seed</c> or <c>Seed:Enabled=true</c> in Development). User passwords come from
/// configuration <c>Seed:DevPassword</c> (user-secrets/env) — there is no default password in source.
/// </summary>
public sealed class DevelopmentSeeder(AppDbContext db, IPasswordService passwords, IConfiguration configuration, ILogger<DevelopmentSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Organizations.AnyAsync(ct))
        {
            logger.LogInformation("Seed skipped: database already contains data.");
            return;
        }

        var password = configuration["Seed:DevPassword"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new InvalidOperationException("Set 'Seed:DevPassword' (>= 8 chars) via user-secrets or environment variable Seed__DevPassword before seeding.");
        }

        var now = DateTime.UtcNow;
        var org = new Organization { Name = "Demo F&B Co.", BrandName = "Demo Coffee", CreatedAtUtc = now };
        db.Organizations.Add(org);

        var q1 = new Branch
        {
            OrganizationId = org.Id,
            Code = "HCM-Q1",
            Name = "Demo Coffee – Quận 1",
            Address = "Địa chỉ demo, Quận 1, TP.HCM",
            Phone = "0000000001",
            InvoiceFooter = "Cảm ơn quý khách – hẹn gặp lại!",
            CreatedAtUtc = now,
        };
        var q3 = new Branch
        {
            OrganizationId = org.Id,
            Code = "HCM-Q3",
            Name = "Demo Coffee – Quận 3",
            Address = "Địa chỉ demo, Quận 3, TP.HCM",
            Phone = "0000000003",
            CreatedAtUtc = now,
        };
        db.Branches.AddRange(q1, q3);
        foreach (var branch in new[] { q1, q3 })
        {
            for (var i = 1; i <= 5; i++)
            {
                db.BranchTables.Add(new BranchTable { BranchId = branch.Id, Name = $"Bàn {i}", PublicCode = SecureTokens.Create(12) });
            }
        }

        User NewUser(string userName, string display, UserRole role, params Branch[] branches)
        {
            var user = new User { UserName = userName, NormalizedUserName = User.Normalize(userName), DisplayName = display, Role = role, CreatedAtUtc = now };
            user.PasswordHash = passwords.Hash(user, password);
            user.BranchAssignments = branches.Select(b => new UserBranchAssignment { UserId = user.Id, BranchId = b.Id }).ToList();
            db.Users.Add(user);
            return user;
        }

        NewUser("superadmin", "Super Admin", UserRole.SuperAdmin);
        NewUser("admin.q1", "Quản lý Q1", UserRole.BranchAdmin, q1);
        var staffQ1 = NewUser("staff.q1", "Nhân viên Q1", UserRole.BranchStaff, q1);
        NewUser("admin.q3", "Quản lý Q3", UserRole.BranchAdmin, q3);
        NewUser("staff.q3", "Nhân viên Q3", UserRole.BranchStaff, q3);

        var today = DateOnly.FromDateTime(BusinessTime.ToLocal(now, q1.TimeZoneId));
        db.Employees.AddRange(
            new Employee { BranchId = q1.Id, EmployeeCode = "NV001", FullName = "Nguyễn Văn An", Position = "Barista", HourlyRate = 25000, UserId = staffQ1.Id, CreatedAtUtc = now },
            new Employee { BranchId = q1.Id, EmployeeCode = "NV002", FullName = "Trần Thị Bình", Position = "Thu ngân", HourlyRate = 24000, CreatedAtUtc = now },
            new Employee { BranchId = q3.Id, EmployeeCode = "NV003", FullName = "Lê Minh Châu", Position = "Barista", HourlyRate = 25000, CreatedAtUtc = now });
        foreach (var branch in new[] { q1, q3 })
        {
            db.WorkShifts.AddRange(
                new WorkShift { BranchId = branch.Id, Name = "Ca sáng", StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(14, 0), EffectiveFrom = today },
                new WorkShift { BranchId = branch.Id, Name = "Ca chiều", StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(22, 0), EffectiveFrom = today },
                new WorkShift { BranchId = branch.Id, Name = "Ca đêm", StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(6, 0), PayMultiplier = 1.3m, EffectiveFrom = today });
        }

        // Ingredients (base units)
        var coffee = new Ingredient { Code = "CF-BEAN", Name = "Cà phê hạt", Unit = "g" };
        var milk = new Ingredient { Code = "MILK", Name = "Sữa tươi", Unit = "ml" };
        var condensed = new Ingredient { Code = "MILK-COND", Name = "Sữa đặc", Unit = "ml" };
        var tea = new Ingredient { Code = "TEA", Name = "Trà đen", Unit = "g" };
        var peach = new Ingredient { Code = "PEACH", Name = "Đào ngâm", Unit = "pcs" };
        var pearl = new Ingredient { Code = "PEARL", Name = "Trân châu", Unit = "g" };
        var cup = new Ingredient { Code = "CUP", Name = "Ly nhựa", Unit = "pcs" };
        var ingredients = new[] { coffee, milk, condensed, tea, peach, pearl, cup };
        db.Ingredients.AddRange(ingredients);

        var catCoffee = new MenuCategory { Name = "Cà phê", SortOrder = 1 };
        var catTea = new MenuCategory { Name = "Trà", SortOrder = 2 };
        db.MenuCategories.AddRange(catCoffee, catTea);

        var pearlAddOn = new AddOn { Name = "Trân châu", Price = 8000 };
        var extraShot = new AddOn { Name = "Thêm shot espresso", Price = 10000 };
        db.AddOns.AddRange(pearlAddOn, extraShot);
        db.RecipeLines.AddRange(
            new RecipeLine { AddOnId = pearlAddOn.Id, IngredientId = pearl.Id, Quantity = 30 },
            new RecipeLine { AddOnId = extraShot.Id, IngredientId = coffee.Id, Quantity = 9 });

        var items = new List<MenuItem>();

        MenuItem Item(MenuCategory cat, string sku, string name, string desc, int sort, (string Name, decimal Price, (Ingredient Ing, decimal Qty)[] Recipe)[] variants, params AddOn[] addOns)
        {
            var item = new MenuItem { CategoryId = cat.Id, Sku = sku, Name = name, Description = desc, SortOrder = sort, CreatedAtUtc = now };
            var vs = 0;
            foreach (var v in variants)
            {
                var variant = new MenuItemVariant { MenuItemId = item.Id, Name = v.Name, BasePrice = v.Price, SortOrder = vs++ };
                item.Variants.Add(variant);
                foreach (var (ing, qty) in v.Recipe)
                {
                    db.RecipeLines.Add(new RecipeLine { VariantId = variant.Id, IngredientId = ing.Id, Quantity = qty });
                }
            }

            item.AddOns = addOns.Select(a => new MenuItemAddOn { MenuItemId = item.Id, AddOnId = a.Id }).ToList();
            db.MenuItems.Add(item);
            items.Add(item);
            return item;
        }

        Item(catCoffee, "CF-DEN", "Cà phê đen", "Cà phê phin truyền thống", 1,
            [("S", 25000, [(coffee, 18), (cup, 1)]), ("L", 32000, [(coffee, 25), (cup, 1)])], extraShot);
        Item(catCoffee, "CF-SUA", "Cà phê sữa", "Cà phê phin với sữa đặc", 2,
            [("S", 29000, [(coffee, 18), (condensed, 30), (cup, 1)]), ("L", 36000, [(coffee, 25), (condensed, 40), (cup, 1)])], extraShot);
        Item(catCoffee, "CF-LATTE", "Latte", "Espresso và sữa tươi đánh nóng", 3,
            [("M", 45000, [(coffee, 18), (milk, 180), (cup, 1)]), ("L", 52000, [(coffee, 18), (milk, 240), (cup, 1)])], extraShot);
        Item(catTea, "TEA-DAO", "Trà đào", "Trà đen, đào ngâm", 1,
            [("M", 42000, [(tea, 8), (peach, 2), (cup, 1)]), ("L", 49000, [(tea, 10), (peach, 3), (cup, 1)])], pearlAddOn);
        Item(catTea, "TEA-SUA", "Trà sữa", "Trà sữa truyền thống", 2,
            [("M", 39000, [(tea, 6), (milk, 120), (cup, 1)]), ("L", 45000, [(tea, 8), (milk, 160), (cup, 1)])], pearlAddOn);

        var opening = new Dictionary<Ingredient, (decimal Qty, decimal Min)>
        {
            [coffee] = (5000, 500),
            [milk] = (10000, 1000),
            [condensed] = (3000, 300),
            [tea] = (1000, 100),
            [peach] = (200, 20),
            [pearl] = (3000, 300),
            [cup] = (500, 50),
        };
        foreach (var branch in new[] { q1, q3 })
        {
            foreach (var item in items)
            {
                db.BranchMenuItems.Add(new BranchMenuItem { BranchId = branch.Id, MenuItemId = item.Id, IsAvailable = true, UpdatedAtUtc = now });
            }

            foreach (var (ing, (qty, min)) in opening)
            {
                db.BranchInventories.Add(new BranchInventory { BranchId = branch.Id, IngredientId = ing.Id, QuantityOnHand = qty, MinThreshold = min, UpdatedAtUtc = now });
                db.InventoryLedgerEntries.Add(new InventoryLedgerEntry
                {
                    BranchId = branch.Id,
                    IngredientId = ing.Id,
                    EntryType = LedgerEntryType.Receipt,
                    QuantityChange = qty,
                    BalanceAfter = qty,
                    SourceType = "Seed",
                    SourceId = branch.Id,
                    ReferenceKey = $"seed:{branch.Id:N}:{ing.Id:N}",
                    Reason = "Tồn đầu kỳ (dữ liệu demo)",
                    CreatedAtUtc = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogWarning("Development seed data created (demo users: superadmin, admin.q1, staff.q1, admin.q3, staff.q3). DO NOT use in production.");
    }
}
