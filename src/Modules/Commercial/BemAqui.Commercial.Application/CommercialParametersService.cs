using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>% padrão de markup ou margem (COM-01a CA 12, <c>pricing.default_pct</c>): vazio no seed; editado com <c>settings.edit</c>.</summary>
public sealed class CommercialParametersService(ICommercialDb db, ITenantParameters tenantParameters, IPermissionChecker permissions, TimeProvider clock) : ICommercialParameters
{
    public const string DefaultPct = "pricing.default_pct";
    public const string ValidityDays = "quotes.validity_days";

    public async Task<int> ValidityDaysAsync(CancellationToken cancellationToken = default) =>
        (int)(await db.CommercialParameters.AsNoTracking().Where(p => p.Key == ValidityDays).Select(p => p.Value).FirstOrDefaultAsync(cancellationToken) ?? 15m);

    public async Task<decimal?> DefaultPricingPctAsync(CancellationToken cancellationToken = default) =>
        await db.CommercialParameters.AsNoTracking().Where(p => p.Key == DefaultPct).Select(p => p.Value).FirstOrDefaultAsync(cancellationToken);

    public async Task<CommercialParametersView> GetAsync(CancellationToken cancellationToken = default) =>
        new(await DefaultPricingPctAsync(cancellationToken), (await tenantParameters.PricingModeAsync(cancellationToken)).ToString(), await ValidityDaysAsync(cancellationToken));

    public async Task<Result<CommercialParametersView>> UpdateAsync(CommercialParametersInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (input.DefaultPct is < 0 or > 1000)
        {
            return QuoteService.Invalid("defaultPct", "Informe um percentual de 0% a 1.000%.");
        }

        if (input.DefaultPct is >= 100 && await tenantParameters.PricingModeAsync(cancellationToken) == PricingMode.Margin)
        {
            return QuoteService.Invalid("defaultPct", "A margem precisa ser menor que 100%.");
        }

        if (input.ValidityDays is < 1 or > 365)
        {
            return QuoteService.Invalid("validityDays", "Informe de 1 a 365 dias.");
        }

        await SetAsync(ValidityDays, input.ValidityDays, cancellationToken);
        var parameter = await db.CommercialParameters.FirstOrDefaultAsync(p => p.Key == DefaultPct, cancellationToken);
        if (parameter is null)
        {
            db.CommercialParameters.Add(new CommercialParameter { Key = DefaultPct, Value = input.DefaultPct, UpdatedAt = clock.GetUtcNow() });
        }
        else
        {
            parameter.Value = input.DefaultPct is { } v ? Math.Round(v, 4, MidpointRounding.AwayFromZero) : null;
            parameter.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private async Task SetAsync(string key, decimal? value, CancellationToken ct)
    {
        var parameter = await db.CommercialParameters.FirstOrDefaultAsync(p => p.Key == key, ct);
        if (parameter is null)
        {
            db.CommercialParameters.Add(new CommercialParameter { Key = key, Value = value, UpdatedAt = clock.GetUtcNow() });
        }
        else if (parameter.Value != value)
        {
            parameter.Value = value;
            parameter.UpdatedAt = clock.GetUtcNow();
        }
    }
}
