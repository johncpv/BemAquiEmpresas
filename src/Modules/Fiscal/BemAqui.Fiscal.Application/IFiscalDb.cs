using BemAqui.Fiscal.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Fiscal.Application;

/// <summary>Dados do módulo Fiscal (ADR 0006): os handlers do módulo só enxergam estas tabelas (schema <c>fiscal</c>).</summary>
public interface IFiscalDb
{
    DbSet<FiscalIntegrationSettings> FiscalIntegrationSettings { get; }

    DbSet<OutgoingInvoice> OutgoingInvoices { get; }

    DbSet<FiscalSettings> FiscalSettings { get; }

    DbSet<SettingConfirmation> SettingConfirmations { get; }

    DbSet<CertificateUpload> CertificateUploads { get; }

    /// <summary>Regras fiscais por operação, uma linha por versão (FIS-02b).</summary>
    DbSet<TaxRule> TaxRules { get; }

    /// <summary>Itens da NF-e de saída (FIS-03a).</summary>
    DbSet<OutgoingInvoiceItem> OutgoingInvoiceItems { get; }

    /// <summary>Parcelas do contrato referenciadas pela nota (só referência, FIS-03a).</summary>
    DbSet<OutgoingInvoiceReceivable> OutgoingInvoiceReceivables { get; }

    /// <summary>Números gastos sem nota válida (rejeitada ou denegada, FIS-03a; a inutilização é da FIS-03b).</summary>
    DbSet<NumberGap> NumberGaps { get; }

    /// <summary>Cancelamento, carta de correção e inutilização (FIS-03b).</summary>
    DbSet<FiscalEvent> FiscalEvents { get; }

    /// <summary>"Pedir cancelamento" de quem não tem <c>fiscal.approve</c> (FIS-03b).</summary>
    DbSet<CancellationRequest> CancellationRequests { get; }

    /// <summary>NF-e de entrada e os itens (FIS-04a).</summary>
    DbSet<IncomingInvoice> IncomingInvoices { get; }

    DbSet<IncomingInvoiceItem> IncomingInvoiceItems { get; }

    ChangeTracker ChangeTracker { get; }

    /// <summary>Trava da nota na conferência de pendentes (<c>FOR UPDATE SKIP LOCKED</c>, CA 14).</summary>
    DatabaseFacade Database { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
