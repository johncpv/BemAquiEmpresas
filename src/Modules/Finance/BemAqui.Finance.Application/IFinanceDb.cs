using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Finance.Application;

/// <summary>Dados do módulo Finance (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface IFinanceDb
{
    DbSet<BankAccount> BankAccounts { get; }

    DbSet<PaymentTerm> PaymentTerms { get; }

    DbSet<Account> Accounts { get; }

    DbSet<CostCenter> CostCenters { get; }

    DbSet<Receivable> Receivables { get; }

    DbSet<ProjectTriggerEvent> ProjectTriggerEvents { get; }

    DbSet<ReceivableSettlement> ReceivableSettlements { get; }

    DbSet<FinancialTransaction> FinancialTransactions { get; }

    DbSet<FinanceParameter> FinanceParameters { get; }

    DbSet<Payable> Payables { get; }

    DbSet<RecurringExpense> RecurringExpenses { get; }

    DbSet<OpeningSetup> OpeningSetups { get; }

    DbSet<OpeningBatch> OpeningBatches { get; }

    DbSet<PaymentOrder> PaymentOrders { get; }

    DbSet<PaymentOrderItem> PaymentOrderItems { get; }

    DbSet<PaymentOrderApproval> PaymentOrderApprovals { get; }

    DbSet<PayableSettlement> PayableSettlements { get; }

    DbSet<ApprovalRule> ApprovalRules { get; }

    DbSet<PaymentOrderLevel> PaymentOrderLevels { get; }

    DbSet<BankTransfer> BankTransfers { get; }

    DbSet<BankEntry> BankEntries { get; }

    DbSet<DreViewMark> DreViewMarks { get; }

    DbSet<RevenueTarget> RevenueTargets { get; }

    DbSet<BankIntegration> BankIntegrations { get; }

    DbSet<BankStatementBatch> BankStatementBatches { get; }

    DbSet<BankStatementLine> BankStatementLines { get; }

    DbSet<BankStatementBalance> BankStatementBalances { get; }

    DbSet<BankOfxContingency> BankOfxContingencies { get; }

    DbSet<BankReconciliation> BankReconciliations { get; }

    DbSet<BankReconciliationLink> BankReconciliationLinks { get; }

    DbSet<BankStatementRule> BankStatementRules { get; }

    DbSet<BankReconciliationClosing> BankReconciliationClosings { get; }

    DbSet<BillingSlip> BillingSlips { get; }

    DbSet<BillingReferenceSequence> BillingReferenceSequences { get; }

    /// <summary>Para trocar a conta padrão em dois passos numa transação (o índice único parcial é checado a cada comando).</summary>
    DatabaseFacade Database { get; }

    /// <summary>Para descartar o que ficou rastreado depois de uma gravação recusada (conferência diária do Inter, BNK-02).</summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Para conferir a versão (xmin) que o cliente abriu (ADR 0019 §2).</summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}

/// <summary>
/// Movimentação de uma conta (CA 5 e 8): a FIN-01 e a FIN-04 implementam e registram no DI. Com movimentação, o saldo inicial
/// fica travado e a conta não é excluída, só inativada.
/// </summary>
public interface IBankAccountUsageProvider
{
    Task<bool> HasMovementAsync(Guid accountId, CancellationToken cancellationToken = default);
}
