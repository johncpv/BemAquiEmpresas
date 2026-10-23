using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// Parâmetros de Compras (ADR 0016 adendo M6), em <c>purchasing.parameters</c>: o prazo padrão de compra (<c>default_lead_days</c>,
/// 15 dias, de 0 a 365; ponto 31), editado com <c>settings.edit</c> e auditado; e a marca da recomposição da implantação (D3).
/// </summary>
public sealed class PurchasingParametersService(IPurchasingDb db, IPermissionChecker permissions, TimeProvider clock)
{
    public const string DefaultLeadDays = "default_lead_days";
    public const string ShortagesBackfilled = "shortages_backfilled";

    public async Task<int> LeadDaysAsync(CancellationToken cancellationToken = default) =>
        await ValueAsync(DefaultLeadDays, cancellationToken) is { } days && days >= PurchaseRequestRules.MinLeadDays && days <= PurchaseRequestRules.MaxLeadDays
            ? (int)days
            : PurchaseRequestRules.DefaultLeadDays;

    public async Task<PurchasingParametersView> GetAsync(CancellationToken cancellationToken = default)
    {
        var id = await db.PurchasingParameters.AsNoTracking().Where(p => p.Key == DefaultLeadDays).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        return new PurchasingParametersView(await LeadDaysAsync(cancellationToken), id);
    }

    public async Task<Result<PurchasingParametersView>> UpdateAsync(PurchasingParametersInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (input.DefaultLeadDays is < PurchaseRequestRules.MinLeadDays or > PurchaseRequestRules.MaxLeadDays)
        {
            const string message = "Informe de 0 a 365 dias.";
            return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["defaultLeadDays"] = [message] });
        }

        await SetAsync(DefaultLeadDays, input.DefaultLeadDays, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>A recomposição da implantação já rodou neste tenant (D3)?</summary>
    public async Task<bool> ShortagesBackfilledAsync(CancellationToken cancellationToken = default) => await ValueAsync(ShortagesBackfilled, cancellationToken) is 1m;

    /// <summary>Marca a recomposição da implantação como feita (grava no próximo <c>SaveChanges</c>).</summary>
    public Task MarkShortagesBackfilledAsync(CancellationToken cancellationToken = default) => SetAsync(ShortagesBackfilled, 1m, cancellationToken);

    private async Task<decimal?> ValueAsync(string key, CancellationToken ct) =>
        await db.PurchasingParameters.AsNoTracking().Where(p => p.Key == key).Select(p => p.Value).FirstOrDefaultAsync(ct);

    private async Task SetAsync(string key, decimal value, CancellationToken ct)
    {
        var parameter = await db.PurchasingParameters.FirstOrDefaultAsync(p => p.Key == key, ct);
        if (parameter is null)
        {
            db.PurchasingParameters.Add(new PurchasingParameter { Key = key, Value = value, UpdatedAt = clock.GetUtcNow() });
        }
        else if (parameter.Value != value)
        {
            parameter.Value = value;
            parameter.UpdatedAt = clock.GetUtcNow();
        }
    }
}
