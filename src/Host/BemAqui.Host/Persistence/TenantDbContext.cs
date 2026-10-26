using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Domain;
using BemAqui.Migrations.Application;
using BemAqui.Migrations.Domain;
using BemAqui.Parties.Application;
using BemAqui.Parties.Domain;
using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Host.Persistence;

/// <summary>
/// DbContext único do banco de tenant (ADR 0006): tabelas compartilhadas da base e, conforme as stories
/// avançam, as configurações de cada módulo. A conexão por tenant chega na FND-02.
/// </summary>
public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : BemAquiDbContext(options), IIdentityDb, IPartiesDb, IMigrationDb, IFinanceDb, IInventoryDb, IProductionDb, IProductionPlanDb, IHrDb, BemAqui.Commercial.Application.ICommercialDb, BemAqui.Engineering.Application.IEngineeringDb, BemAqui.Purchasing.Application.IPurchasingDb
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ShopfloorDevice> ShopfloorDevices => Set<ShopfloorDevice>();

    public DbSet<UserPin> UserPins => Set<UserPin>();

    public DbSet<KioskScreen> KioskScreens => Set<KioskScreen>();

    public DbSet<KioskSession> KioskSessions => Set<KioskSession>();

    public DbSet<UserInvite> UserInvites => Set<UserInvite>();

    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    public DbSet<TwoFactorChallenge> TwoFactorChallenges => Set<TwoFactorChallenge>();

    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();

    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();

    public DbSet<CatalogModule> CatalogModules => Set<CatalogModule>();

    public DbSet<CatalogModuleAction> CatalogModuleActions => Set<CatalogModuleAction>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupModule> GroupModules => Set<GroupModule>();

    public DbSet<GroupGrant> GroupPermissions => Set<GroupGrant>();

    public DbSet<UserGroup> UserGroups => Set<UserGroup>();

    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    public DbSet<AiSettings> AiSettings => Set<AiSettings>();

    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();

    public DbSet<SeedPatch> SeedPatches => Set<SeedPatch>();

    public DbSet<Party> Parties => Set<Party>();

    public DbSet<PartyRole> PartyRoles => Set<PartyRole>();

    public DbSet<PartyAddress> PartyAddresses => Set<PartyAddress>();

    public DbSet<PartyContact> PartyContacts => Set<PartyContact>();

    public DbSet<CnpjLookupCache> CnpjLookups => Set<CnpjLookupCache>();

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    public DbSet<PaymentTerm> PaymentTerms => Set<PaymentTerm>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<CostCenter> CostCenters => Set<CostCenter>();

    public DbSet<Receivable> Receivables => Set<Receivable>();

    public DbSet<BemAqui.Finance.Domain.ProjectTriggerEvent> ProjectTriggerEvents => Set<BemAqui.Finance.Domain.ProjectTriggerEvent>();

    public DbSet<ReceivableSettlement> ReceivableSettlements => Set<ReceivableSettlement>();

    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();

    public DbSet<FinanceParameter> FinanceParameters => Set<FinanceParameter>();

    public DbSet<Payable> Payables => Set<Payable>();

    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();

    public DbSet<OpeningSetup> OpeningSetups => Set<OpeningSetup>();

    public DbSet<OpeningBatch> OpeningBatches => Set<OpeningBatch>();

    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();

    public DbSet<PaymentOrderItem> PaymentOrderItems => Set<PaymentOrderItem>();

    public DbSet<PaymentOrderApproval> PaymentOrderApprovals => Set<PaymentOrderApproval>();

    public DbSet<PayableSettlement> PayableSettlements => Set<PayableSettlement>();

    public DbSet<BemAqui.Finance.Domain.ApprovalRule> ApprovalRules => Set<BemAqui.Finance.Domain.ApprovalRule>();

    public DbSet<BemAqui.Finance.Domain.PaymentOrderLevel> PaymentOrderLevels => Set<BemAqui.Finance.Domain.PaymentOrderLevel>();

    public DbSet<BankTransfer> BankTransfers => Set<BankTransfer>();

    public DbSet<BankEntry> BankEntries => Set<BankEntry>();

    public DbSet<DreViewMark> DreViewMarks => Set<DreViewMark>();

    public DbSet<RevenueTarget> RevenueTargets => Set<RevenueTarget>();

    public DbSet<BemAqui.Commercial.Domain.Quote> Quotes => Set<BemAqui.Commercial.Domain.Quote>();

    public DbSet<BemAqui.Commercial.Domain.QuoteItem> QuoteItems => Set<BemAqui.Commercial.Domain.QuoteItem>();

    public DbSet<BemAqui.Commercial.Domain.QuoteItemCost> QuoteItemCosts => Set<BemAqui.Commercial.Domain.QuoteItemCost>();

    public DbSet<BemAqui.Commercial.Domain.QuoteMilestone> QuoteMilestones => Set<BemAqui.Commercial.Domain.QuoteMilestone>();

    public DbSet<BemAqui.Commercial.Domain.QuoteDispatch> QuoteDispatches => Set<BemAqui.Commercial.Domain.QuoteDispatch>();

    public DbSet<BemAqui.Commercial.Domain.CommercialParameter> CommercialParameters => Set<BemAqui.Commercial.Domain.CommercialParameter>();

    public DbSet<BemAqui.Commercial.Domain.Contract> Contracts => Set<BemAqui.Commercial.Domain.Contract>();

    public DbSet<BemAqui.Commercial.Domain.ContractMilestone> ContractMilestones => Set<BemAqui.Commercial.Domain.ContractMilestone>();

    public DbSet<BemAqui.Commercial.Domain.Project> Projects => Set<BemAqui.Commercial.Domain.Project>();

    public DbSet<BemAqui.Commercial.Domain.ProjectItem> ProjectItems => Set<BemAqui.Commercial.Domain.ProjectItem>();

    public DbSet<BemAqui.Commercial.Domain.ProjectBudget> ProjectBudgets => Set<BemAqui.Commercial.Domain.ProjectBudget>();

    public DbSet<BemAqui.Commercial.Domain.ProjectCost> ProjectCosts => Set<BemAqui.Commercial.Domain.ProjectCost>();

    public DbSet<BemAqui.Commercial.Domain.ContractAmendment> ContractAmendments => Set<BemAqui.Commercial.Domain.ContractAmendment>();

    public DbSet<BemAqui.Commercial.Domain.ContractAmendmentMilestone> ContractAmendmentMilestones => Set<BemAqui.Commercial.Domain.ContractAmendmentMilestone>();

    public DbSet<BemAqui.Commercial.Domain.ContractAmendmentReduction> ContractAmendmentReductions => Set<BemAqui.Commercial.Domain.ContractAmendmentReduction>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<MeasureUnit> Units => Set<MeasureUnit>();

    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();

    public DbSet<WarrantyDefault> WarrantyDefaults => Set<WarrantyDefault>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<ItemUnitConversion> ItemUnitConversions => Set<ItemUnitConversion>();

    public DbSet<ItemSupplierCode> ItemSupplierCodes => Set<ItemSupplierCode>();

    public DbSet<StockPosition> StockPositions => Set<StockPosition>();

    public DbSet<StockBalance> StockBalances => Set<StockBalance>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<Lot> Lots => Set<Lot>();

    public DbSet<ManualPosting> ManualPostings => Set<ManualPosting>();

    public DbSet<ManualPostingLine> ManualPostingLines => Set<ManualPostingLine>();

    public DbSet<InventoryParameters> InventoryParameters => Set<InventoryParameters>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<ReservationChange> ReservationChanges => Set<ReservationChange>();

    public DbSet<MaterialRequisition> MaterialRequisitions => Set<MaterialRequisition>();

    public DbSet<MaterialRequisitionLine> MaterialRequisitionLines => Set<MaterialRequisitionLine>();

    public DbSet<Serial> Serials => Set<Serial>();

    public DbSet<SerialComponent> SerialComponents => Set<SerialComponent>();

    public DbSet<SerialSequence> SerialSequences => Set<SerialSequence>();

    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();

    public DbSet<StockAdjustmentLine> StockAdjustmentLines => Set<StockAdjustmentLine>();

    public DbSet<InventoryCount> InventoryCounts => Set<InventoryCount>();

    public DbSet<InventoryCountLine> InventoryCountLines => Set<InventoryCountLine>();

    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();

    public DbSet<Calendar> Calendars => Set<Calendar>();

    public DbSet<CalendarExceptionDay> CalendarExceptions => Set<CalendarExceptionDay>();

    public DbSet<BemAqui.Production.Domain.ProductionOrder> ProductionOrders => Set<BemAqui.Production.Domain.ProductionOrder>();

    public DbSet<BemAqui.Production.Domain.ProductionOrderStep> ProductionOrderSteps => Set<BemAqui.Production.Domain.ProductionOrderStep>();

    public DbSet<BemAqui.Production.Domain.ProductionOrderChecklistItem> ProductionOrderChecklist => Set<BemAqui.Production.Domain.ProductionOrderChecklistItem>();

    public DbSet<BemAqui.Production.Domain.ProductionOrderProjectItem> ProductionOrderProjectItems => Set<BemAqui.Production.Domain.ProductionOrderProjectItem>();

    public DbSet<BemAqui.Production.Domain.InstallationTemplateLine> InstallationTemplate => Set<BemAqui.Production.Domain.InstallationTemplateLine>();

    public DbSet<BemAqui.Production.Domain.LaborEntry> LaborEntries => Set<BemAqui.Production.Domain.LaborEntry>();

    public DbSet<BemAqui.Production.Domain.StepPause> StepPauses => Set<BemAqui.Production.Domain.StepPause>();

    public DbSet<BemAqui.Production.Domain.LaborCostRecord> LaborCostHistory => Set<BemAqui.Production.Domain.LaborCostRecord>();

    public DbSet<BemAqui.Production.Domain.ProductionParameter> ProductionParameters => Set<BemAqui.Production.Domain.ProductionParameter>();

    public DbSet<BemAqui.Production.Domain.NonConformity> NonConformities => Set<BemAqui.Production.Domain.NonConformity>();

    public DbSet<BemAqui.Production.Domain.QualityDecision> QualityDecisions => Set<BemAqui.Production.Domain.QualityDecision>();

    public DbSet<BemAqui.Production.Domain.Shipment> Shipments => Set<BemAqui.Production.Domain.Shipment>();

    public DbSet<BemAqui.Production.Domain.ShipmentLine> ShipmentLines => Set<BemAqui.Production.Domain.ShipmentLine>();

    public DbSet<BemAqui.Production.Domain.ShipmentLineSerial> ShipmentLineSerials => Set<BemAqui.Production.Domain.ShipmentLineSerial>();

    // Plano (PLN-01a, ADR 0029): a escrita só pelo IProductionPlanDb (ScheduleService); o IProductionDb só lê, sem rastreamento.
    public DbSet<ScheduleRun> ScheduleRuns => Set<ScheduleRun>();

    public DbSet<ScheduleAllocation> ScheduleAllocations => Set<ScheduleAllocation>();

    public DbSet<ScheduleStepForecast> ScheduleStepForecasts => Set<ScheduleStepForecast>();

    IQueryable<ScheduleRun> IProductionDb.ScheduleRunsView => Set<ScheduleRun>().AsNoTracking();

    IQueryable<ScheduleAllocation> IProductionDb.ScheduleAllocationsView => Set<ScheduleAllocation>().AsNoTracking();

    IQueryable<ScheduleStepForecast> IProductionDb.ScheduleStepForecastsView => Set<ScheduleStepForecast>().AsNoTracking();

    // Previsão de entrega por projeto (PLN-01b): escrita pelo ScheduleService; a cópia do Commercial é a project_forecasts.
    public DbSet<ScheduleProjectForecast> ScheduleProjectForecasts => Set<ScheduleProjectForecast>();

    IQueryable<ScheduleProjectForecast> IProductionDb.ScheduleProjectForecastsView => Set<ScheduleProjectForecast>().AsNoTracking();

    // Fila de prioridade dos projetos (PLN-02, ADR 0029 §3): do Production, auditada.
    public DbSet<BemAqui.Production.Domain.PlanningQueueEntry> PlanningQueue => Set<BemAqui.Production.Domain.PlanningQueueEntry>();

    public DbSet<BemAqui.Production.Domain.PlanningQueueState> PlanningQueueStates => Set<BemAqui.Production.Domain.PlanningQueueState>();

    public DbSet<BemAqui.Production.Domain.PlanningQueueChange> PlanningQueueChanges => Set<BemAqui.Production.Domain.PlanningQueueChange>();

    public DbSet<BemAqui.Commercial.Domain.ProjectForecast> ProjectForecasts => Set<BemAqui.Commercial.Domain.ProjectForecast>();

    public DbSet<BemAqui.Engineering.Domain.RoutingTemplate> RoutingTemplates => Set<BemAqui.Engineering.Domain.RoutingTemplate>();

    public DbSet<BemAqui.Engineering.Domain.RoutingTemplateStep> RoutingTemplateSteps => Set<BemAqui.Engineering.Domain.RoutingTemplateStep>();

    public DbSet<BemAqui.Engineering.Domain.RoutingTemplateItem> RoutingTemplateItems => Set<BemAqui.Engineering.Domain.RoutingTemplateItem>();

    public DbSet<BemAqui.Engineering.Domain.Bom> Boms => Set<BemAqui.Engineering.Domain.Bom>();

    public DbSet<BemAqui.Engineering.Domain.BomLine> BomLines => Set<BemAqui.Engineering.Domain.BomLine>();

    public DbSet<BemAqui.Purchasing.Domain.PurchaseRequest> PurchaseRequests => Set<BemAqui.Purchasing.Domain.PurchaseRequest>();

    public DbSet<BemAqui.Purchasing.Domain.PurchaseRequestLine> PurchaseRequestLines => Set<BemAqui.Purchasing.Domain.PurchaseRequestLine>();

    public DbSet<BemAqui.Purchasing.Domain.PurchasingParameter> PurchasingParameters => Set<BemAqui.Purchasing.Domain.PurchasingParameter>();

    public DbSet<BemAqui.Purchasing.Domain.PurchaseOrder> PurchaseOrders => Set<BemAqui.Purchasing.Domain.PurchaseOrder>();

    public DbSet<BemAqui.Purchasing.Domain.PurchaseOrderLine> PurchaseOrderLines => Set<BemAqui.Purchasing.Domain.PurchaseOrderLine>();

    public DbSet<BemAqui.Purchasing.Domain.PurchaseOrderLevel> PurchaseOrderLevels => Set<BemAqui.Purchasing.Domain.PurchaseOrderLevel>();

    public DbSet<BemAqui.Purchasing.Domain.PurchaseOrderApproval> PurchaseOrderApprovals => Set<BemAqui.Purchasing.Domain.PurchaseOrderApproval>();

    public DbSet<BemAqui.Purchasing.Domain.PurchaseOrderPriceCorrection> PurchaseOrderPriceCorrections => Set<BemAqui.Purchasing.Domain.PurchaseOrderPriceCorrection>();

    public DbSet<BemAqui.Purchasing.Domain.GoodsReceipt> GoodsReceipts => Set<BemAqui.Purchasing.Domain.GoodsReceipt>();

    public DbSet<BemAqui.Purchasing.Domain.GoodsReceiptLine> GoodsReceiptLines => Set<BemAqui.Purchasing.Domain.GoodsReceiptLine>();

    public DbSet<BemAqui.Purchasing.Domain.Rfq> Rfqs => Set<BemAqui.Purchasing.Domain.Rfq>();

    public DbSet<BemAqui.Purchasing.Domain.RfqSupplier> RfqSuppliers => Set<BemAqui.Purchasing.Domain.RfqSupplier>();

    public DbSet<BemAqui.Purchasing.Domain.RfqLine> RfqLines => Set<BemAqui.Purchasing.Domain.RfqLine>();

    public DbSet<BemAqui.Purchasing.Domain.RfqLineRequest> RfqLineRequests => Set<BemAqui.Purchasing.Domain.RfqLineRequest>();

    public DbSet<BemAqui.Purchasing.Domain.RfqQuote> RfqQuotes => Set<BemAqui.Purchasing.Domain.RfqQuote>();

    public DbSet<BemAqui.Purchasing.Domain.PurchaseOrderDispatch> PurchaseOrderDispatches => Set<BemAqui.Purchasing.Domain.PurchaseOrderDispatch>();

    public DbSet<JobPosition> JobPositions => Set<JobPosition>();

    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();

    public DbSet<LaborUnion> LaborUnions => Set<LaborUnion>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<EmployeeHistoryEntry> EmployeeHistory => Set<EmployeeHistoryEntry>();

    public DbSet<EmployeeDependent> EmployeeDependents => Set<EmployeeDependent>();

    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();

    public DbSet<ImportItem> ImportItems => Set<ImportItem>();

    public DbSet<ExternalRef> ExternalRefs => Set<ExternalRef>();

    protected override void ConfigureModules(ModelBuilder modelBuilder)
    {
        // Busca por parte do nome e do CNPJ (CAD-01a CA 18). pg_trgm é extensão confiável: o dono do banco cria.
        modelBuilder.HasPostgresExtension("pg_trgm");
        foreach (var module in ModuleRegistry.All)
        {
            if (module.PersistenceAssembly is { } assembly)
            {
                modelBuilder.ApplyConfigurationsFromAssembly(assembly);
            }
        }
    }
}
