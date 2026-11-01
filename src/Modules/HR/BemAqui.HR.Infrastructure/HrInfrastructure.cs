using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.HR.Application;
using BemAqui.HR.Application.Documents;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.HR.Infrastructure;

/// <summary>Jornada do seed (CAD-04a CA 16): "44 h — seg a sex 08:00–17:48", pela <c>seed_key</c>, sem recriar a excluída.</summary>
public sealed class WorkScheduleSeeder : ITenantSeeder
{
    public const string SeedKey = "44h-seg-sex";
    public const string Name = "44 h — seg a sex 08:00–17:48";

    public int Order => 40;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IHrDb>();
        if (await db.WorkSchedules.IgnoreQueryFilters().AnyAsync(s => s.SeedKey == SeedKey, cancellationToken) || await db.WorkSchedules.AnyAsync(s => s.Name == Name, cancellationToken))
        {
            return;
        }

        var schedule = new WorkSchedule { SeedKey = SeedKey, Name = Name, CreatedAt = tenantScope.GetRequiredService<TimeProvider>().GetUtcNow() };
        schedule.SetDays(Enumerable.Range(1, 5).Select(day => new ScheduleDay(day, "08:00", "17:48", 60)).ToList());
        db.WorkSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Folha lançada (RH-02a CA 1 e 3): a lista padrão de rubricas e os parâmetros do RH, "A confirmar com a contadora", também nos tenants
/// existentes no próximo deploy; idempotente, sem recriar o que o tenant excluiu nem sobrescrever o que mudou.
/// </summary>
public sealed class PayrollSeeder : ITenantSeeder
{
    public int Order => 41;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IHrDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        await PayrollItemService.SeedAsync(db, clock, cancellationToken);
        await HrParametersService.SeedAsync(db, clock, cancellationToken);
        // FLH-01 CA 1: o pacote de tabelas legais do ano, "A confirmar com a contadora", uma vez por tenant.
        await PayrollTaxTableService.SeedAsync(db, clock, cancellationToken);
    }
}

public static class HrInfrastructure
{
    /// <summary>
    /// Cargos, jornadas e sindicatos (CAD-04a): serviços, contrato <see cref="IHourlyCost"/>, seed e histórico. O histórico do
    /// HR só abre com <c>hr.view_sensitive</c> (CA 13).
    /// </summary>
    public static IServiceCollection AddHrInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<PositionService>();
        // Minha pauta (DSH-04): cargo com apontamento "Sem custo-hora" (os apontamentos pelo ILaborCostGaps da Produção).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Agenda.IAgendaSource, HrAgendaSource>());
        services.TryAddScoped<IHourlyCost>(sp => sp.GetRequiredService<PositionService>());
        services.TryAddScoped<IPositions, PositionsContract>();
        services.TryAddScoped<ScheduleService>();
        services.TryAddScoped<UnionService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, WorkScheduleSeeder>());
        // Funcionários (CAD-04b).
        services.TryAddScoped<EmployeeService>();
        services.TryAddScoped<EmployeesContract>();
        services.TryAddScoped<IEmployees>(sp => sp.GetRequiredService<EmployeesContract>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPositionUsageProvider, EmployeesContract>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IUnionUsageProvider, EmployeesContract>());
        // Afastamentos (CAD-04c1): eventos do job diário.
        services.TryAddScoped<EmployeeLeaveEvents>();
        // Complemento do cadastro (CAD-04c2): dependentes, conta para pagamento, adicionais e documentos. Os documentos digitalizados
        // são do dono EmployeeDocument (ADR 0019 §7 adendo): ler e gravar com hr.view_sensitive, pdf, png e jpg até 10 MB, e cada
        // download grava a leitura sensível (AuditReads).
        services.TryAddScoped<EmployeeComplementService>();
        services.AddAttachmentOwner(EmployeeComplementService.DocumentOwner, new AttachmentOwnerRules("hr", ["application/pdf", "image/png", "image/jpeg"], 10 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IHrDb>().Employees.AnyAsync(e => e.Id == id, ct),
            ReadAction: PositionService.SensitiveAction, WriteAction: PositionService.SensitiveAction, AuditReads: true));
        // Folha lançada (RH-02a): rubricas, folha, holerites, guias e os parâmetros do RH (Configurações → Parâmetros → RH). O
        // histórico da folha e dos holerites só abre com hr.view_sensitive; o das rubricas, com hr.view; o dos parâmetros, com settings.
        services.TryAddScoped<HrParametersService>();
        services.TryAddScoped<PayrollItemService>();
        services.TryAddScoped<PayrollService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, PayrollSeeder>());
        services.AddAuditableEntity<PayrollItem>("hr");
        services.AddAuditableEntity<PayrollRun>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<Payslip>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<PayslipLine>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<PayrollRunCharge>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<HrParameter>("settings");
        // Fechamento, folha prevista e a conta de salário para o Finance (RH-02c, ADR 0036): a conta em claro só sai no "Pagar" da OP.
        services.TryAddScoped<PayrollCostTargets>();
        services.TryAddScoped<PayrollForecastService>();
        services.TryAddScoped<PayrollClosingService>();
        services.TryAddScoped<IEmployeeBankAccounts, EmployeeBankAccountsContract>();
        services.AddAuditableEntity<PayrollForecast>("hr", PositionService.SensitiveAction);
        // Tabelas legais da folha por vigência (FLH-01, ADR 0039 §1): o histórico abre com hr.view (tabela legal não é dado pessoal); as
        // mudanças desde o último fechamento vão para o diálogo de fechamento (CA 12).
        services.TryAddScoped<PayrollTaxTableService>();
        services.TryAddScoped<IPayrollTaxTables>(sp => sp.GetRequiredService<PayrollTaxTableService>());
        services.TryAddScoped<PayrollChangeNotices>();
        // Folha calculada (FLH-02a, ADR 0039 §2): o motor é puro (Domain); o serviço monta o retrato, grava e roda o job acima de 20.
        services.TryAddScoped<PayrollCalculationService>();
        services.AddAuditableEntity<PayrollTaxTable>("hr");
        services.AddAuditableEntity<PayrollTaxTableDataset>("hr");
        // Pacote para a contadora (RH-03): um layout (o padrão v1) e as seções dele; a RH-01c registra a do ponto.
        services.TryAddScoped<AccountantExportService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccountantExport, StandardAccountantExport>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccountantExportSection, AccountantMovementsSection>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccountantExportSection, AccountantPayrollSection>());
        // RH-01c: o layout padrão sobe para v2 (com o ponto.csv); o v1 continua para quem já conferiu com a contadora.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccountantExport, AccountantExportV1>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccountantExportSection, AccountantTimesheetSection>());

        // Holerite em PDF, "Meus holerites" e aviso (RH-02b). Os PDFs guardados (dono Payslip) e o ZIP de "Baixar todos" (dono
        // PayslipBatch, a folha) são arquivos do sistema: ler com hr.view_sensitive, cada download grava SensitiveRead (AuditReads) e o
        // usuário não anexa nada a eles (CanAttach recusa; só o StoreGeneratedAsync grava).
        services.TryAddScoped<PayslipDocumentService>();
        services.TryAddScoped<PayslipNoticeService>();
        services.AddAttachmentOwner(PayslipDocumentRules.OwnerType, new AttachmentOwnerRules("hr", ["application/pdf"],
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IHrDb>().Payslips.AnyAsync(p => p.Id == id, ct),
            ReadAction: PositionService.SensitiveAction, WriteAction: PositionService.SensitiveAction, CanAttach: SystemOnly, AuditReads: true));
        services.AddAttachmentOwner(PayslipDocumentRules.BatchOwnerType, new AttachmentOwnerRules("hr", ["application/zip"],
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IHrDb>().PayrollRuns.AnyAsync(r => r.Id == id, ct),
            ReadAction: PositionService.SensitiveAction, WriteAction: PositionService.SensitiveAction, CanAttach: SystemOnly, AuditReads: true));
        // Ponto (RH-01a, ADR 0035): "Meu ponto", a consulta do RH e a localização só com hr.view_sensitive. A marcação é dado pessoal do
        // funcionário: o histórico abre com hr.view_sensitive e a localização sai mascarada ([PersonalData]).
        services.TryAddScoped<BemAqui.HR.Application.TimeClock.TimeClockService>();
        services.AddAuditableEntity<TimeEntry>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<TimeClockGeoConsent>("hr", PositionService.SensitiveAction);
        // Ajustes do ponto (RH-01b): lançamento, pedido, abono e aprovação. O período fechado vem da RH-01c (até lá, nenhum fechado). O
        // documento do abono (dono TimeAdjustmentDocument, ADR 0019 §7) lê e grava com hr.view_health, pdf, png e jpg até 10 MB, confere o
        // pedido e grava SensitiveRead no download; a rota genérica de anexos não grava (CanAttach): só a do pedido, que confere o registro.
        // O histórico do pedido abre com hr.view (justificativa, motivo e comentário mascarados, [PersonalData]).
        services.TryAddScoped<BemAqui.HR.Application.TimeClock.ITimesheetPeriodLock, BemAqui.HR.Application.TimeClock.TimesheetClosings>();
        services.TryAddScoped<BemAqui.HR.Application.TimeClock.TimeAdjustmentService>();
        services.AddAuditableEntity<TimeAdjustment>("hr");
        services.AddAttachmentOwner(BemAqui.HR.Application.TimeClock.TimeAdjustmentService.DocumentOwner, new AttachmentOwnerRules("hr", ["application/pdf", "image/png", "image/jpeg"], 10 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IHrDb>().TimeAdjustments.AnyAsync(a => a.Id == id && a.Kind == TimeAdjustmentKind.Absence, ct),
            ReadAction: BemAqui.HR.Application.TimeClock.TimeAdjustmentService.HealthAction, WriteAction: BemAqui.HR.Application.TimeClock.TimeAdjustmentService.HealthAction,
            CanAttach: ByRequestOnly, AuditReads: true));
        // Espelho, banco de horas e fechamento (RH-01c): o histórico do espelho e dos dias revisados abre com hr.view (os dias congelados e o
        // comentário saem mascarados, [PersonalData]). O PDF do espelho fechado (dono Timesheet) e o ZIP do período (dono TimesheetBatch) são
        // arquivos do sistema: ler com hr.view (o próprio funcionário pela rota dele), cada download auditado, e ninguém anexa nada a eles.
        services.TryAddScoped<BemAqui.HR.Application.TimeClock.TimesheetService>();
        services.TryAddScoped<BemAqui.HR.Application.TimeClock.TimesheetPdfService>();
        services.AddAuditableEntity<Timesheet>("hr");
        services.AddAuditableEntity<TimesheetDayReview>("hr");
        services.AddAttachmentOwner(BemAqui.HR.Application.TimeClock.TimesheetPdfService.OwnerType, new AttachmentOwnerRules("hr", ["application/pdf"],
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IHrDb>().Timesheets.AnyAsync(t => t.Id == id, ct),
            ReadAction: "view", WriteAction: "view", CanAttach: SystemOnly, AuditReads: true));
        services.AddAttachmentOwner(BemAqui.HR.Application.TimeClock.TimesheetPdfService.BatchOwnerType, new AttachmentOwnerRules("hr", ["application/zip"],
            OwnerExists: async (sp, id, ct) => BemAqui.HR.Application.TimeClock.TimesheetPdfService.PeriodOfBatchOwner(id) is { } period
                && await sp.GetRequiredService<IHrDb>().Timesheets.AnyAsync(t => t.Period == period, ct),
            ReadAction: "view", WriteAction: "view", CanAttach: SystemOnly, AuditReads: true));
        // LGPD-01a: titular funcionário (identificação, seletor e exportação, com o sensível pelas ações do RH).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Privacy.IPersonalDataProvider, HrPersonalDataProvider>());
        services.AddAuditableEntity<EmployeeDependent>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<Employee>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<EmployeeHistoryEntry>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<JobPosition>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<WorkSchedule>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<LaborUnion>("hr", PositionService.SensitiveAction);
        return services;
    }

    private static Task<BemAqui.SharedKernel.Error?> ByRequestOnly(IServiceProvider services, Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<BemAqui.SharedKernel.Error?>(BemAqui.SharedKernel.Error.Validation("attachment.by_request_only", "Anexe o documento pelo pedido de abono."));

    private static Task<BemAqui.SharedKernel.Error?> SystemOnly(IServiceProvider services, Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<BemAqui.SharedKernel.Error?>(BemAqui.SharedKernel.Error.Validation("attachment.system_only", "Este documento é gerado pelo sistema e não aceita anexos."));
}
