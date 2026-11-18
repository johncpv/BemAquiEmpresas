using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Parties.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Finance.Infrastructure;

/// <summary>
/// Pacote do Comércio no Financeiro (PERF-04 CA 1): contas Caixa PDV 01, Cofre e Quebra de caixa e as formas de pagamento com a adquirente
/// "Maquininha", que o pacote dos Terceiros criou antes (lida pelo contrato <see cref="IParties"/>, nunca pela tabela).
/// </summary>
public sealed class FinanceRetailSeedPackage : IProfileSeedPackage
{
    /// <summary>A <c>seed_key</c> da adquirente do pacote dos Terceiros (<c>PartiesRetailSeedPackage</c>).</summary>
    public const string AcquirerSeedKey = "maquininha";

    public string Key => BusinessProfileSeeds.FinanceRetail;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var acquirer = await tenantScope.GetRequiredService<IParties>().FindBySeedKeyAsync(AcquirerSeedKey, cancellationToken);
        var usable = acquirer is { IsActive: true } && acquirer.Roles.Contains(PartyRoleKind.Acquirer) ? acquirer.Id : (Guid?)null;
        await tenantScope.GetRequiredService<RetailFinanceSeed>().ApplyRetailPackageAsync(usable, cancellationToken);
    }
}
