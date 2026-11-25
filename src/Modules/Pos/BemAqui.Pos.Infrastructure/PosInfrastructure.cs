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
        services.TryAddScoped<PosStockSyncService>(); // PDV-05: saldo da loja e a opção "vender sem saldo" descem ao caixa
        services.TryAddScoped<PosCustomerSearchService>(); // PDV-02 CA 15: cliente cadastrado na tela de venda
        services.TryAddScoped<CashSessionIngestion>(); // CX-01: sessão de caixa e movimentos de dinheiro (a subida da OFF-02 chama)
        services.TryAddScoped<PrinterService>(); // IMP-01: impressoras do caixa e as impressões registradas (a subida da OFF-02 chama)
        services.TryAddScoped<CashClosingService>(); // CX-02: fechamento com conferência cega (a subida da OFF-02 chama)
        services.TryAddScoped<CashSessionQueries>(); // CX-02: Loja → Caixas (lista, quadro, aprovação, reabertura, Pix)
        services.TryAddScoped<CashSessionReportService>(); // CX-02 CA 9: relatório do fechamento em PDF
        services.TryAddScoped<PosEventIngestion>(); // OFF-02: subida dos eventos do caixa
        services.TryAddScoped<PosSyncAlerts>(); // OFF-02 CA 16: caixa sem enviar vendas
        services.TryAddScoped<SyncDivergenceService>(); // OFF-02 CA 14 e 15: Pendências do PDV
        services.TryAddScoped<SupervisorApprovalService>(); // PDV-04: PIN do supervisor no balcão e o relatório
        services.TryAddScoped<IPosRegisters, PosRegistersContract>();
        services.TryAddScoped<IPosBackoffice, PosBackofficeContract>(); // PDV-07: trilha da venda e caixas abertos para a retaguarda e o Início
        services.TryAddScoped<IPosReturnSupport, PosReturnSupport>(); // PDV-06: o caixa, a aprovação e o dinheiro da devolução
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IProfileSeedPackage, PosRetailSeedPackage>());
        services.AddAuditableEntity<Store>("sales"); // IMP-01: o aceite do modo não fiscal (ligar, desligar, versão do texto) fica no histórico da loja
        services.AddAuditableEntity<Register>("sales");
        services.AddAuditableEntity<Printer>("pos");
        services.AddAuditableEntity<CashSession>("sales"); // CX-01: abertura e fechamento do caixa
        services.AddAuditableEntity<CashMovement>("sales"); // CX-01: fundo de troco, sangria e suprimento
        services.AddAuditableEntity<CashCount>("sales"); // CX-02: contagem do fechamento
        services.AddAuditableEntity<CashPixCheck>("sales"); // CX-02 CA 13: conferência do Pix contra o extrato
        services.AddAuditableEntity<SyncDivergence>("sales"); // OFF-02 CA 15: a resolução da pendência fica no histórico
        return services;
    }
}
