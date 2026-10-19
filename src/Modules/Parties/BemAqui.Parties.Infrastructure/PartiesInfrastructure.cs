using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Parties.Infrastructure;

public static class PartiesInfrastructure
{
    /// <summary>Terceiros (CAD-01a): serviço, contrato para os outros módulos e entidades com histórico consultável.</summary>
    public static IServiceCollection AddPartiesInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<PartyService>();
        services.TryAddScoped<IParties, PartiesContract>();
        services.TryAddScoped<IPartyImport, PartyImport>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.Finance.Contracts.IPaymentTermUsageProvider, PartyPaymentTermUsage>());
        services.AddAuditableEntity<Party>("parties");
        services.AddAuditableEntity<PartyRole>("parties");
        services.AddAuditableEntity<PartyAddress>("parties");
        services.AddAuditableEntity<PartyContact>("parties");
        return services;
    }
}
