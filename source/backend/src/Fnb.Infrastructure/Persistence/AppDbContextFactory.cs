using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fnb.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by `dotnet ef` so migrations can be created without running API startup validation.
/// Connection: env FNB_DESIGN_SQL or local SQL Server LocalDB (only used for `dotnet ef database update` from a dev box).
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("FNB_DESIGN_SQL")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=FnbDev;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
        return new AppDbContext(options);
    }
}
