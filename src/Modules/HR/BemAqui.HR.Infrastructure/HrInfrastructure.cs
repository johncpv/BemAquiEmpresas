using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.HR.Application;
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
        services.AddAuditableEntity<EmployeeDependent>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<Employee>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<EmployeeHistoryEntry>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<JobPosition>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<WorkSchedule>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<LaborUnion>("hr", PositionService.SensitiveAction);
        return services;
    }
}
