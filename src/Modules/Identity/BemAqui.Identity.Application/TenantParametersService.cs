using System.Globalization;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <param name="ChargesSettingId">Id da linha em <c>tenant_settings</c>, para o "Histórico de alterações" (CA 16 e 18).</param>
public sealed record TenantParametersDto(decimal PayrollChargesPct, PricingMode PricingMode, Guid? ChargesSettingId, Guid? PricingSettingId);

public sealed record PayrollChargesInput(decimal Pct);

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

    public async Task<TenantParametersDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.TenantSettings.AsNoTracking().Where(s => s.Key == ChargesKey || s.Key == PricingKey).ToListAsync(cancellationToken);
        var charges = rows.FirstOrDefault(r => r.Key == ChargesKey);
        var pricing = rows.FirstOrDefault(r => r.Key == PricingKey);
        return new TenantParametersDto(ParseCharges(charges?.Value), ParsePricing(pricing?.Value), charges?.Id, pricing?.Id);
    }

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
