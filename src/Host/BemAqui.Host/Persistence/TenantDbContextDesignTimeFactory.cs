using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BemAqui.Host.Persistence;

/// <summary>Usado só pelo <c>dotnet ef migrations add</c>; não abre conexão.</summary>
internal sealed class TenantDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql("Host=localhost;Database=tenant_design")
            .UseSnakeCaseNamingConvention()
            .Options);
}
