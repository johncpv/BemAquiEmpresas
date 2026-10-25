using System.Globalization;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <param name="ChargesSettingId">Id da linha em <c>tenant_settings</c>, para o "Histórico de alterações" (CA 16 e 18).</param>
/// <param name="EmailDispatchMaxPerUserDay">Envios de documento por e-mail por usuário por dia (CMP-03b CA 5).</param>
public sealed record TenantParametersDto(
    decimal PayrollChargesPct, PricingMode PricingMode, Guid? ChargesSettingId, Guid? PricingSettingId, int EmailDispatchMaxPerUserDay = TenantParametersService.DefaultEmailDispatch,
    Guid? EmailDispatchSettingId = null);

public sealed record PayrollChargesInput(decimal Pct);

public sealed record EmailDispatchLimitInput(int MaxPerUserDay);

public sealed record PricingModeInput(PricingMode Mode);

/// <summary>
/// Configurações → Parâmetros (CAD-06 CA 15 a 19), em <c>identity.tenant_settings</c> (auditada: o histórico tem anterior,
/// novo, quem e quando). Mudar o % de encargos publica <c>PayrollChargesChanged</c> no outbox, na mesma transação.
/// </summary>
public sealed class TenantParametersService(IIdentityDb db, IEventPublisher events, ITenantContext tenant, TimeProvider clock) : ITenantParameters
{
    public const string ChargesKey = "payroll.charges_pct";
    public const string PricingKey = "pricing.mode";
    public const decimal DefaultCharges = 80.00m;

    /// <summary>Limite diário de envios por usuário (CMP-03b CA 5): padrão 20, de 1 a 50.</summary>
    public const string EmailDispatchKey = "email.dispatch_max_per_user_day";
    public const int DefaultEmailDispatch = 20;
    public const int MaxEmailDispatch = 50;

    public async Task<TenantParametersDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.TenantSettings.AsNoTracking().Where(s => s.Key == ChargesKey || s.Key == PricingKey || s.Key == EmailDispatchKey).ToListAsync(cancellationToken);
        var charges = rows.FirstOrDefault(r => r.Key == ChargesKey);
        var pricing = rows.FirstOrDefault(r => r.Key == PricingKey);
        var dispatch = rows.FirstOrDefault(r => r.Key == EmailDispatchKey);
        return new TenantParametersDto(ParseCharges(charges?.Value), ParsePricing(pricing?.Value), charges?.Id, pricing?.Id, ParseDispatch(dispatch?.Value), dispatch?.Id);
    }

    /// <summary>Configurações → Parâmetros → Envios por e-mail (CMP-03b CA 5): de 1 a 50 por usuário por dia, auditado.</summary>
    public async Task<Result<TenantParametersDto>> SaveEmailDispatchLimitAsync(int maxPerUserDay, CancellationToken cancellationToken = default)
    {
        if (maxPerUserDay is < 1 or > MaxEmailDispatch)
        {
            const string message = "Informe de 1 a 50 envios por dia.";
            return Error.Validation("validation", message, new Dictionary<string, string[]> { ["maxPerUserDay"] = [message] });
        }

        var row = await RowAsync(EmailDispatchKey, cancellationToken);
        row.Value = maxPerUserDay.ToString(CultureInfo.InvariantCulture);
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<int> EmailDispatchMaxPerUserDayAsync(CancellationToken cancellationToken = default) =>
        ParseDispatch(await db.TenantSettings.AsNoTracking().Where(s => s.Key == EmailDispatchKey).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken));

    private static int ParseDispatch(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) && max is >= 1 and <= MaxEmailDispatch ? max : DefaultEmailDispatch;

    public async Task<Result<TenantParametersDto>> SaveChargesAsync(decimal pct, CancellationToken cancellationToken = default)
    {
        if (pct is < 0 or > 200 || decimal.Round(pct, 2) != pct)
        {
            const string message = "Informe um percentual entre 0 e 200, com até 2 casas.";
            return Error.Validation("validation", message, new Dictionary<string, string[]> { ["pct"] = [message] });
        }

        var row = await RowAsync(ChargesKey, cancellationToken);
        var old = ParseCharges(row.Value);
        row.Value = pct.ToString("0.00", CultureInfo.InvariantCulture);
        row.UpdatedAt = clock.GetUtcNow();
        if (old != pct)
        {
            events.Publish(new PayrollChargesChanged(tenant.Tenant?.Id ?? Guid.Empty, old, pct, row.UpdatedAt));
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<Result<TenantParametersDto>> SavePricingAsync(PricingMode mode, CancellationToken cancellationToken = default)
    {
        var row = await RowAsync(PricingKey, cancellationToken);
        row.Value = mode == PricingMode.Margin ? "margin" : "markup";
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<decimal> PayrollChargesPctAsync(CancellationToken cancellationToken = default) => (await GetAsync(cancellationToken)).PayrollChargesPct;

    public async Task<PricingMode> PricingModeAsync(CancellationToken cancellationToken = default) => (await GetAsync(cancellationToken)).PricingMode;

    private async Task<TenantSetting> RowAsync(string key, CancellationToken ct)
    {
        var row = await db.TenantSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            row = new TenantSetting { Key = key };
            db.TenantSettings.Add(row);
        }

        return row;
    }

    private static decimal ParseCharges(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var pct) ? pct : DefaultCharges;

    private static PricingMode ParsePricing(string? value) => value == "margin" ? PricingMode.Margin : PricingMode.Markup;
}
