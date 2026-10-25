using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Parâmetros da Produção na tela Configurações → Parâmetros; os ids levam ao histórico de alterações. PLN-01b CA 12: o prazo padrão de
/// instalação (dias úteis, seed 5) e se ele já foi conferido (enquanto não, o selo "A confirmar", ADR 0042).
/// </summary>
public sealed record ProductionParametersView(
    bool ShowLaborEntryCost, int OpenLaborAlertHours, Guid? ShowLaborEntryCostId, Guid? OpenLaborAlertHoursId, int InstallationDays = ProjectForecastCalculator.DefaultInstallationDays,
    bool InstallationDaysConfirmed = false, Guid? InstallationDaysId = null);

/// <summary>Os campos novos são opcionais: sem eles, o prazo de instalação e a conferência ficam como estão.</summary>
public sealed record ProductionParametersInput(bool ShowLaborEntryCost, int OpenLaborAlertHours, int? InstallationDays = null, bool? InstallationDaysConfirmed = null);

/// <summary>
/// Parâmetros da Produção (decisões do PO de 22/10/2026 na PRD-04), em <c>production.parameters</c>, editados com
/// <c>settings.edit</c> e auditados: "Exibir custo-hora em cada apontamento" (padrão desligado: só o total por etapa e por OS), o
/// limite do apontamento aberto (padrão 12 h, de 1 a 72; tela "Apontamentos abertos", PRD-03a CA 22) e o prazo padrão de instalação
/// (<c>planning.installation_days</c>, PLN-01b CA 12: de 0 a 120 dias úteis, padrão 5, "A confirmar" até alguém marcar a conferência;
/// mudar o prazo pede um recálculo do plano).
/// </summary>
public sealed class ProductionParametersService(IProductionDb db, IPermissionChecker permissions, TimeProvider clock, IScheduleTrigger trigger)
{
    public const string ShowLaborEntryCost = "labor.show_entry_cost";
    public const string OpenLaborAlertHours = "labor.open_alert_hours";
    public const int MinAlertHours = 1;
    public const int MaxAlertHours = 72;

    /// <summary>Prazo padrão de instalação em dias úteis do calendário padrão (PLN-01b CA 5 e 12; ADR 0029 §4).</summary>
    public const string InstallationDays = "planning.installation_days";

    /// <summary>Prazo de instalação conferido com a empresa (pré-condição externa da PLN-01b; ADR 0042 §1).</summary>
    public const string InstallationDaysConfirmed = "planning.installation_days_confirmed";

    public async Task<bool> ShowLaborEntryCostAsync(CancellationToken cancellationToken = default) =>
        await ValueAsync(ShowLaborEntryCost, cancellationToken) is 1m;

    public async Task<int> OpenLaborAlertHoursAsync(CancellationToken cancellationToken = default) =>
        await ValueAsync(OpenLaborAlertHours, cancellationToken) is { } hours && hours >= MinAlertHours && hours <= MaxAlertHours ? (int)hours : LaborRules.DefaultOpenAlertHours;

    /// <summary>Prazo de instalação (CA 5): vazio ou fora de 0 a 120 = padrão de 5 dias úteis.</summary>
    public async Task<int> InstallationDaysAsync(CancellationToken cancellationToken = default) =>
        await ValueAsync(InstallationDays, cancellationToken) is { } days && days >= ProjectForecastCalculator.MinInstallationDays && days <= ProjectForecastCalculator.MaxInstallationDays
            ? (int)days
            : ProjectForecastCalculator.DefaultInstallationDays;

    public async Task<ProductionParametersView> GetAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.ProductionParameters.AsNoTracking().ToDictionaryAsync(p => p.Key, p => p.Id, cancellationToken);
        return new ProductionParametersView(
            await ShowLaborEntryCostAsync(cancellationToken), await OpenLaborAlertHoursAsync(cancellationToken), rows.TryGetValue(ShowLaborEntryCost, out var show) ? show : (Guid?)null,
            rows.TryGetValue(OpenLaborAlertHours, out var hours) ? hours : (Guid?)null, await InstallationDaysAsync(cancellationToken),
            await ValueAsync(InstallationDaysConfirmed, cancellationToken) is 1m, rows.TryGetValue(InstallationDays, out var installation) ? installation : (Guid?)null);
    }

    public async Task<Result<ProductionParametersView>> UpdateAsync(ProductionParametersInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (input.OpenLaborAlertHours is < MinAlertHours or > MaxAlertHours)
        {
            return LaborRules.Invalid("openLaborAlertHours", $"Informe de {MinAlertHours} a {MaxAlertHours} horas.");
        }

        if (input.InstallationDays is < ProjectForecastCalculator.MinInstallationDays or > ProjectForecastCalculator.MaxInstallationDays)
        {
            return LaborRules.Invalid("installationDays", $"Informe de {ProjectForecastCalculator.MinInstallationDays} a {ProjectForecastCalculator.MaxInstallationDays} dias úteis.");
        }

        await SetAsync(ShowLaborEntryCost, input.ShowLaborEntryCost ? 1m : 0m, cancellationToken);
        await SetAsync(OpenLaborAlertHours, input.OpenLaborAlertHours, cancellationToken);
        if (input.InstallationDays is { } days && await ValueAsync(InstallationDays, cancellationToken) != days)
        {
            await SetAsync(InstallationDays, days, cancellationToken);

            // CA 12: o prazo novo muda a data prevista dos projetos; o recálculo sai depois do commit.
            await trigger.RequestAsync("prazo de instalação", cancellationToken);
        }

        if (input.InstallationDaysConfirmed is { } confirmed)
        {
            await SetAsync(InstallationDaysConfirmed, confirmed ? 1m : 0m, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private async Task<decimal?> ValueAsync(string key, CancellationToken ct) =>
        await db.ProductionParameters.AsNoTracking().Where(p => p.Key == key).Select(p => p.Value).FirstOrDefaultAsync(ct);

    private async Task SetAsync(string key, decimal value, CancellationToken ct)
    {
        var parameter = await db.ProductionParameters.FirstOrDefaultAsync(p => p.Key == key, ct);
        if (parameter is null)
        {
            db.ProductionParameters.Add(new ProductionParameter { Key = key, Value = value, UpdatedAt = clock.GetUtcNow() });
        }
        else if (parameter.Value != value)
        {
            parameter.Value = value;
            parameter.UpdatedAt = clock.GetUtcNow();
        }
    }
}
