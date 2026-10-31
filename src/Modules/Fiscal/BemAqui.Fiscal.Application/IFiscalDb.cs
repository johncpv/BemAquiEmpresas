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

    ChangeTracker ChangeTracker { get; }

    /// <summary>Trava da nota na conferência de pendentes (<c>FOR UPDATE SKIP LOCKED</c>, CA 14).</summary>
    DatabaseFacade Database { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
