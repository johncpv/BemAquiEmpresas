using BemAqui.Assets.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// Leitura da etiqueta (PAT-01b CA 5 e 7): a ficha resumida pela etiqueta (<c>/b/{etiqueta}</c>) e pelo id (<c>/fab/bens/{id}</c>). A rota
/// confere <c>assets.view</c> antes de chegar aqui; etiqueta inexistente, excluída ou de outro tenant (outro banco) caem no mesmo caminho:
/// uma consulta só, a mesma resposta e o mesmo tempo. A leitura não é registrada (CA 12).
/// </summary>
public sealed partial class AssetService
{
    public const string TagNotFoundCode = "asset_tag_not_found";

    public async Task<Result<AssetCardView>> ByTagAsync(string? tag, CancellationToken cancellationToken = default)
    {
        // Etiqueta malformada vira vazio e a consulta roda do mesmo jeito: nada de atalho que responda mais rápido.
        var normalized = AssetLabelRules.NormalizeTag(tag);
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Tag == normalized, cancellationToken);
        return asset is null ? TagNotFound() : await CardAsync(asset, cancellationToken);
    }

    public async Task<Result<AssetCardView>> CardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return asset is null ? NotFound() : await CardAsync(asset, cancellationToken);
    }

    private async Task<AssetCardView> CardAsync(Asset a, CancellationToken ct)
    {
        var names = await NamesAsync([a], ct);
        var photos = await attachments.LatestCleanAsync(PhotoOwner, [a.Id], ct);
        var employee = a.ResponsibleEmployeeId is { } r ? names.Employees.GetValueOrDefault(r) : null;

        // Próxima manutenção prevista e última feita: da PAT-03a (planos e OS de manutenção); até lá, vazias.
        return new AssetCardView(
            a.Id, a.Tag, a.Name, a.Type, a.Status,
            a.SiteId is { } s ? names.Sites.GetValueOrDefault(s) : null,
            employee?.Name, employee is { IsActive: false },
            a.WorkCenterId is { } c ? names.Centers.GetValueOrDefault(c) : null,
            photos.TryGetValue(a.Id, out var photo) ? photo : null,
            null, null);
    }

    internal static Error TagNotFound() => Error.NotFound(TagNotFoundCode, "Etiqueta não encontrada.");
}
