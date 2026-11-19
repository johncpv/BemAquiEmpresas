using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Pos.Application;
using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Pos.Infrastructure;

public static class PosInfrastructure
{
    /// <summary>
    /// PDV (PDV-01): lojas e caixas (auditados no módulo <c>sales</c>, cujas telas de retaguarda os mostram), o contrato <see cref="IPosRegisters"/>
    /// (o Identity troca o aparelho do caixa), o aperto de mão do aparelho e o pacote de dados iniciais do Comércio.
    /// </summary>
    public static IServiceCollection AddPosInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<StoreService>();
        services.TryAddScoped<PosSyncService>();
        services.TryAddScoped<PosCatalogProjector>(); // OFF-01: modelo de leitura do caixa (consumidor e carga inicial)
        services.TryAddScoped<PosCatalogSyncService>();
        services.TryAddScoped<PosCustomerSearchService>(); // PDV-02 CA 15: cliente cadastrado na tela de venda
        services.TryAddScoped<CashSessionIngestion>(); // CX-01: sessão de caixa e movimentos de dinheiro (a subida da OFF-02 chama)
        services.TryAddScoped<IPosRegisters, PosRegistersContract>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProfileSeedPackage, PosRetailSeedPackage>());
        services.AddAuditableEntity<Store>("sales");
        services.AddAuditableEntity<Register>("sales");
        services.AddAuditableEntity<CashSession>("sales"); // CX-01: abertura e fechamento do caixa
        services.AddAuditableEntity<CashMovement>("sales"); // CX-01: fundo de troco, sangria e suprimento
        return services;
    }
}
