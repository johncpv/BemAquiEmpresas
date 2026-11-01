using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// Parâmetros do Patrimônio (PAT-01b CA 1) em <c>assets.parameters</c>: o tamanho da etiqueta (<c>assets.label_size</c>, seed 50 × 25 mm até a
/// Konebras informar a impressora, P-13). Ler com <c>assets.view</c>; gravar com <c>settings.edit</c>, auditado (dono <c>settings</c>).
/// </summary>
public sealed class AssetParametersService(IAssetsDb db, IPermissionChecker permissions, TimeProvider clock)
{
    public async Task<LabelSize> LabelSizeAsync(CancellationToken cancellationToken = default) =>
        AssetLabelRules.ParseOrDefault(await db.AssetParameters.AsNoTracking().Where(p => p.Key == AssetLabelRules.SizeKey).Select(p => p.Value).FirstOrDefaultAsync(cancellationToken));

    public async Task<AssetParametersView> GetAsync(CancellationToken cancellationToken = default)
    {
        var size = await LabelSizeAsync(cancellationToken);
        var id = await db.AssetParameters.AsNoTracking().Where(p => p.Key == AssetLabelRules.SizeKey).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        return new AssetParametersView(size.WidthMm, size.HeightMm, id);
    }

    public async Task<Result<AssetParametersView>> UpdateAsync(AssetParametersInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", StandardActions.Edit, cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (AssetLabelRules.SizeError(input.LabelWidthMm, input.LabelHeightMm) is { } message)
        {
            return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["labelWidthMm"] = [message], ["labelHeightMm"] = [message] });
        }

        var value = new LabelSize(input.LabelWidthMm, input.LabelHeightMm).ToString();
        var parameter = await db.AssetParameters.FirstOrDefaultAsync(p => p.Key == AssetLabelRules.SizeKey, cancellationToken);
        if (parameter is null)
        {
            db.AssetParameters.Add(new AssetParameter { Key = AssetLabelRules.SizeKey, Value = value, UpdatedAt = clock.GetUtcNow() });
        }
        else if (parameter.Value != value)
        {
            parameter.Value = value;
            parameter.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }
}
