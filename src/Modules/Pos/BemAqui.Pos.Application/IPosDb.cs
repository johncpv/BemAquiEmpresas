using BemAqui.Pos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Pos.Application;

/// <summary>Dados do módulo Pos (ADR 0006): os handlers do módulo só enxergam estas tabelas (schema <c>pos</c>).</summary>
public interface IPosDb
{
    DbSet<Store> Stores { get; }

    DbSet<Register> Registers { get; }

    /// <summary>Modelo de leitura do caixa (OFF-01).</summary>
    DbSet<CatalogItem> CatalogItems { get; }

    /// <summary>Carga inicial do catálogo de cada loja (OFF-01).</summary>
    DbSet<CatalogStoreBuild> CatalogStoreBuilds { get; }

    /// <summary>Sessões de caixa (turnos) e os movimentos de dinheiro: fundo de troco, sangria e suprimento (CX-01).</summary>
    DbSet<CashSession> CashSessions { get; }

    DbSet<CashMovement> CashMovements { get; }

    /// <summary>CX-02: a contagem do fechamento por forma (esperado e diferença só no servidor) e a conferência dos Pix contra o extrato.</summary>
    DbSet<CashCount> CashCounts { get; }

    DbSet<CashPixCheck> CashPixChecks { get; }

    /// <summary>Impressoras dos caixas e as impressões registradas (IMP-01).</summary>
    DbSet<Printer> Printers { get; }

    DbSet<PrintRecord> PrintRecords { get; }

    /// <summary>OFF-02: registro imutável dos eventos do caixa, as pendências, as exceções por operador e o dinheiro das vendas por sessão.</summary>
    DbSet<DeviceEvent> DeviceEvents { get; }

    DbSet<SyncDivergence> SyncDivergences { get; }

    DbSet<OperatorIncident> OperatorIncidents { get; }

    DbSet<SessionSale> SessionSales { get; }

    DbSet<SupervisorApproval> SupervisorApprovals { get; }

    /// <summary>Limpar o rastreamento depois de voltar ao ponto de salvamento (OFF-02).</summary>
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    /// <summary>Gravação em lote do modelo de leitura e a trava do tenant (OFF-01).</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Schema do módulo.</summary>
public static class PosSchema
{
    public const string Name = "pos";
}
