using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>Usado só pelo <c>dotnet ef migrations add</c>; não abre conexão.</summary>
internal sealed class DataProtectionKeysDesignTimeFactory : IDesignTimeDbContextFactory<DataProtectionKeysDbContext>
{
    public DataProtectionKeysDbContext CreateDbContext(string[] args) =>
        new(SecretsServiceCollectionExtensions.ConfigureKeysContext(
                new DbContextOptionsBuilder<DataProtectionKeysDbContext>(), "Host=localhost;Database=catalog")
            .Options);
}
