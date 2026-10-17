using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BemAqui.Platform.Infrastructure.Catalog;

/// <summary>Usado só pelo <c>dotnet ef migrations add</c>; não abre conexão.</summary>
internal sealed class CatalogDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(CatalogOptions.Configure(new DbContextOptionsBuilder<CatalogDbContext>(), "Host=localhost;Database=catalog").Options);
}

public static class CatalogOptions
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static DbContextOptionsBuilder<CatalogDbContext> Configure(DbContextOptionsBuilder<CatalogDbContext> builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder
            .UseNpgsql(connectionString, o => o.MigrationsHistoryTable(MigrationsHistoryTable, CatalogDbContext.Schema))
            .UseSnakeCaseNamingConvention();
        return builder;
    }
}
