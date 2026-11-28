using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Finance.Infrastructure;

/// <summary>
/// Tenant novo nasce com "Só o Proprietário" na autoaprovação da OP (FIN-10a CA 2, decisão do PO de 27/11/2026). Roda depois do
/// <see cref="FinanceParametersSeeder"/>, que já gravou "Nunca"; os tenants existentes não passam por aqui.
/// </summary>
public sealed class FinanceProvisioningDefaults : ITenantProvisioningDefaults
{
    public int Order => 42;

    public async Task<string> ApplyAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IFinanceDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        const decimal ownerOnly = (int)SelfApprovalMode.OwnerOnly;
        var parameter = await db.FinanceParameters.FirstOrDefaultAsync(p => p.Key == FinanceParametersService.OwnerCanApproveOwn, cancellationToken);
        if (parameter is null)
        {
            db.FinanceParameters.Add(new FinanceParameter { Key = FinanceParametersService.OwnerCanApproveOwn, Value = ownerOnly, UpdatedAt = clock.GetUtcNow() });
        }
        else if (parameter.Value != ownerOnly)
        {
            parameter.Value = ownerOnly;
            parameter.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
        return "Ordem de pagamento: " + FinanceParametersService.Label(SelfApprovalMode.OwnerOnly) + ".";
    }
}
