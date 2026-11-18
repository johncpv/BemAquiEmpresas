using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Parties.Infrastructure;

/// <summary>
/// Pacote do Comércio nos Terceiros (PERF-04 CA 1): a adquirente "Maquininha" (PJ, sem documento, papel Adquirente), para as formas de
/// cartão do pacote do Financeiro apontarem para ela até o lojista informar a empresa da maquininha de verdade. Nasce uma vez pela
/// <c>seed_key</c>; renomeada, inativada ou excluída pelo tenant, o seed não mexe nem recria.
/// </summary>
public sealed class PartiesRetailSeedPackage : IProfileSeedPackage
{
    public const string AcquirerSeedKey = "maquininha";

    public string Key => BusinessProfileSeeds.PartiesRetail;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IPartiesDb>();
        if (await db.Parties.IgnoreQueryFilters().AnyAsync(p => p.SeedKey == AcquirerSeedKey, cancellationToken))
        {
            return;
        }

        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var party = new Party { Kind = PartyKind.PJ, LegalName = "Maquininha", SeedKey = AcquirerSeedKey, CreatedAt = clock.GetUtcNow() };
        party.Roles.Add(new PartyRole { PartyId = party.Id, Role = PartyRoleKind.Acquirer });
        db.Parties.Add(party);
        await db.SaveChangesAsync(cancellationToken);
    }
}
