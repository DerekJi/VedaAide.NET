using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Veda.Storage;

/// <summary>
/// Design-time factory for EF Core tooling (dotnet ef migrations add, etc.)
/// Used when running EF commands without the startup project.
/// </summary>
public class VedaDbContextFactory : IDesignTimeDbContextFactory<VedaDbContext>
{
    public VedaDbContext CreateDbContext(string[] args)
    {
        // Allow overriding the DB path via environment variable for dev tooling
        // e.g. set VEDA_DB_PATH=src/Veda.Api/veda.db before running dotnet ef commands
        var dbPath = Environment.GetEnvironmentVariable("VEDA_DB_PATH")
            ?? "design-time.db";
        var options = new DbContextOptionsBuilder<VedaDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new VedaDbContext(options);
    }
}
