using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>Parâmetros da Produção na tela Configurações → Parâmetros; os ids levam ao histórico de alterações.</summary>
public sealed record ProductionParametersView(bool ShowLaborEntryCost, int OpenLaborAlertHours, Guid? ShowLaborEntryCostId, Guid? OpenLaborAlertHoursId);

public sealed record ProductionParametersInput(bool ShowLaborEntryCost, int OpenLaborAlertHours);

/// <summary>
/// Parâmetros da Produção (decisões do PO de 22/10/2026 na PRD-04), em <c>production.parameters</c>, editados com
/// <c>settings.edit</c> e auditados: "Exibir custo-hora em cada apontamento" (padrão desligado: só o total por etapa e por OS) e o
/// limite do apontamento aberto (padrão 12 h, de 1 a 72; tela "Apontamentos abertos", PRD-03a CA 22).
/// </summary>
public sealed class ProductionParametersService(IProductionDb db, IPermissionChecker permissions, TimeProvider clock)
{
    public const string ShowLaborEntryCost = "labor.show_entry_cost";
    public const string OpenLaborAlertHours = "labor.open_alert_hours";
    public const int MinAlertHours = 1;
    public const int MaxAlertHours = 72;

    public async Task<bool> ShowLaborEntryCostAsync(CancellationToken cancellationToken = default) =>
        await ValueAsync(ShowLaborEntryCost, cancellationToken) is 1m;

    public async Task<int> OpenLaborAlertHoursAsync(CancellationToken cancellationToken = default) =>
        await ValueAsync(OpenLaborAlertHours, cancellationToken) is { } hours && hours >= MinAlertHours && hours <= MaxAlertHours ? (int)hours : LaborRules.DefaultOpenAlertHours;

    public async Task<ProductionParametersView> GetAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.ProductionParameters.AsNoTracking().ToDictionaryAsync(p => p.Key, p => p.Id, cancellationToken);
        return new ProductionParametersView(
            await ShowLaborEntryCostAsync(cancellationToken), await OpenLaborAlertHoursAsync(cancellationToken), rows.TryGetValue(ShowLaborEntryCost, out var show) ? show : (Guid?)null,
            rows.TryGetValue(OpenLaborAlertHours, out var hours) ? hours : (Guid?)null);
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

        await SetAsync(ShowLaborEntryCost, input.ShowLaborEntryCost ? 1m : 0m, cancellationToken);
        await SetAsync(OpenLaborAlertHours, input.OpenLaborAlertHours, cancellationToken);
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