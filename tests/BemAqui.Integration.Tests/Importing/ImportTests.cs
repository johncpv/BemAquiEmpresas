using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Migrations.Application;
using BemAqui.Migrations.Domain;
using BemAqui.Parties.Application;
using BemAqui.Platform.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Importing;

/// <summary>MIG-01a: executor da importação com um carregador de teste de terceiros (o real é da MIG-01b).</summary>
public partial class ImportTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Operator = "operador@konebras.com.br";

    private static int _nextRow = 1000;

    /// <summary>Linhas de origem únicas por teste: a chave externa é por tabela e id de origem.</summary>
    private static int NextRow() => Interlocked.Add(ref _nextRow, 10);

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Roda uma execução num escopo novo (como a CLI faz), com o carregador de teste.</summary>
    private async Task<ImportRunResult> RunAsync(FakeSnapshot source, ImportMode mode = ImportMode.Real, int? failAfter = null)
    {
        await using var scope = await ScopeAsync();
        var sp = scope.ServiceProvider;
        var loader = new TestPartyLoader(sp.GetRequiredService<PartyService>(), sp.GetRequiredService<TenantDbContext>()) { FailAfter = failAfter };
        var runner = new ImportRunner(sp.GetRequiredService<IMigrationDb>(), [loader], sp.GetRequiredService<ITenantProfile>(),
            sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<AuditOrigin>(), TimeProvider.System);
        return await runner.RunAsync(source, new ImportRequest(mode, [TestPartyLoader.Name], Operator, ImportMapping.Empty));
    }

    private async Task<T> QueryAsync<T>(Func<TenantDbContext, Task<T>> query)
    {
        await using var scope = await ScopeAsync();
        return await query(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private Task<ExternalRef?> RefAsync(int rowid) =>
        QueryAsync(db => db.ExternalRefs.AsNoTracking().FirstOrDefaultAsync(r => r.SourceTable == "llx_societe" && r.SourceId == rowid.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>CNPJ válido e único por teste (14 dígitos com DV calculado).</summary>
    private static string NewCnpj()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("12345678").Take(12).Select(c => c - '0').ToList();
        int Dv(int[] w) { var r = w.Select((x, i) => x * digits[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        digits.Add(Dv([5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        digits.Add(Dv([6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(digits);
    }
}
