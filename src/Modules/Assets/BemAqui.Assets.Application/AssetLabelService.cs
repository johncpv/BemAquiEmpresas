using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.SharedKernel;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// Etiquetas do patrimônio (PAT-01b CA 1 a 4 e 12). O QR leva só <c>https://{host primário do tenant}/b/{etiqueta}</c> (catálogo, nunca o
/// Host da requisição). "Imprimir etiqueta" com <c>assets.view</c> e "Reimprimir etiqueta" com <c>assets.edit</c> (a mesma etiqueta: a
/// etiqueta nunca muda); as duas vão para o histórico do bem (<c>Exported</c>, sem conteúdo). O lote: até 50 na hora; acima, o
/// <see cref="AssetLabelBatchJob"/> (só <c>tenantId</c> e ids) gera e avisa no sino. O PDF do lote é arquivo do sistema (dono
/// <c>AssetLabelBatch</c>, o usuário que pediu) e vale por 7 dias.
/// </summary>
public sealed class AssetLabelService(
    IAssetsDb db,
    IPdfRenderer renderer,
    AssetParametersService parameters,
    TenantLinks links,
    TenantContextHolder tenant,
    AttachmentService attachments,
    IAuditWriter audit,
    INotificationService notifications,
    IBackgroundJobClient jobs,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const string BatchOwner = "AssetLabelBatch";
    public const string PdfContentType = "application/pdf";
    public const string NotificationType = "assets.labels";

    /// <summary>Formatos gravados no histórico do bem (<c>Exported</c>).</summary>
    public const string PrintFormat = "etiqueta";
    public const string ReprintFormat = "etiqueta-reimpressao";

    public const string BatchNotFoundCode = "asset_label_batch_not_found";
    public const string BatchExpiredCode = "asset_label_batch_expired";

    /// <summary>"Imprimir etiqueta" (CA 1) e "Reimprimir etiqueta" (CA 4): o PDF de uma etiqueta no tamanho do parâmetro.</summary>
    public async Task<Result<AssetLabelFile>> LabelAsync(Guid id, bool reprint, CancellationToken cancellationToken = default)
    {
        var document = await LabelDocumentAsync(id, cancellationToken);
        if (document is null)
        {
            return AssetService.NotFound();
        }

        var bytes = await renderer.RenderAsync(document, cancellationToken);
        await audit.RecordExportAsync(nameof(Asset), id.ToString(), reprint ? ReprintFormat : PrintFormat, 1, cancellationToken);
        return new AssetLabelFile(new MemoryStream(bytes, writable: false), $"etiqueta-{document.Model.Labels[0].Tag}.pdf");
    }

    /// <summary>O documento da etiqueta de um bem (o mesmo do PDF); a data do PDF é a do bem, então a reimpressão sai igual à original.</summary>
    public async Task<AssetLabelPdfDocument?> LabelDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return asset is null ? null : new AssetLabelPdfDocument(await ModelAsync([asset], AssetLabelLayout.Label, asset.CreatedAt, cancellationToken));
    }

    /// <summary>
    /// "Imprimir etiquetas" (CA 3): os bens escolhidos ou todos do filtro (até 500), em ordem de etiqueta. A impressão vai para o histórico de
    /// cada bem na hora do pedido (quem pediu). Até 50, o PDF sai agora; acima, em segundo plano.
    /// </summary>
    public async Task<Result<AssetLabelBatchResult>> StartBatchAsync(AssetLabelBatchInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(input.Layout))
        {
            return AssetService.Invalid("layout", "Escolha etiqueta por página ou folha A4.");
        }

        var user = currentUser.UserId ?? throw new InvalidOperationException("Etiquetas em lote sem usuário.");
        IQueryable<Asset> query = db.Assets.AsNoTracking();
        if (input.AllFromFilter)
        {
            var f = input.Filter ?? new AssetLabelFilter(null, null, null, null, null, null);
            query = AssetService.Filtered(query, new AssetListQuery(f.Q, f.Type, f.SiteId, f.WorkCenterId, f.ResponsibleEmployeeId, f.Status));
        }
        else
        {
            var ids = (input.AssetIds ?? []).Distinct().ToList();
            if (ids.Count > AssetLabelRules.BatchMax)
            {
                return TooMany();
            }

            query = query.Where(a => ids.Contains(a.Id));
        }

        var found = await query.OrderBy(a => a.Tag).Select(a => a.Id).Take(AssetLabelRules.BatchMax + 1).ToListAsync(cancellationToken);
        if (found.Count == 0)
        {
            return AssetService.Invalid("assetIds", "Selecione ao menos um bem para imprimir as etiquetas.");
        }

        if (found.Count > AssetLabelRules.BatchMax)
        {
            return TooMany();
        }

        await audit.RecordExportsAsync(nameof(Asset), found.Select(i => i.ToString()).ToList(), PrintFormat, cancellationToken);
        if (found.Count > AssetLabelRules.InlineLimit)
        {
            var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Etiquetas em lote fora do escopo de tenant.");
            var args = new AssetLabelBatchArgs(found, input.Layout, user);
            jobs.Create<AssetLabelBatchJob>(j => j.ExecuteAsync(tenantId, args, CancellationToken.None), new EnqueuedState(JobQueues.Default));
            return new AssetLabelBatchResult(true, found.Count, null, $"Gerando {found.Count} etiquetas… Avisamos no sino quando o PDF estiver pronto.");
        }

        var stored = await BuildBatchAsync(found, input.Layout, user, cancellationToken);
        return new AssetLabelBatchResult(false, found.Count, stored, found.Count == 1 ? "1 etiqueta pronta." : $"{found.Count} etiquetas prontas.");
    }

    /// <summary>Segundo plano (CA 3): gera o PDF e avisa quem pediu no sino, com o link para baixar.</summary>
    public async Task CompleteBatchAsync(AssetLabelBatchArgs args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        var stored = await BuildBatchAsync(args.AssetIds, args.Layout, args.RequestedBy, cancellationToken);
        if (stored is { } id)
        {
            notifications.Notify(args.RequestedBy, NotificationType, $"Etiquetas prontas para imprimir ({args.AssetIds.Count}).", "/app/patrimonio/bens?lote=" + id);
        }
        else
        {
            notifications.Notify(args.RequestedBy, NotificationType, "Não foi possível gerar as etiquetas: os bens foram excluídos.", "/app/patrimonio/bens");
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>PDF do lote, só para quem pediu e por 7 dias; de outro usuário ou inexistente, 404.</summary>
    public async Task<Result<AssetLabelFile>> BatchFileAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var user = currentUser.UserId;
        var file = await attachments.FindAsync(attachmentId, cancellationToken);
        if (user is null || file is null || file.OwnerType != BatchOwner || file.OwnerId != user)
        {
            return Error.NotFound(BatchNotFoundCode, "Arquivo de etiquetas não encontrado.");
        }

        if (clock.GetUtcNow() - file.CreatedAt > AssetLabelRules.BatchRetention)
        {
            return Error.NotFound(BatchExpiredCode, "O arquivo de etiquetas expirou depois de 7 dias. Gere de novo pela lista de bens.");
        }

        var content = await attachments.DownloadCheckedAsync(BatchOwner, user.Value, attachmentId, cancellationToken);
        return content.IsFailure ? content.Error! : new AssetLabelFile(content.Value.Content, file.FileName);
    }

    private async Task<Guid?> BuildBatchAsync(IReadOnlyList<Guid> ids, AssetLabelLayout layout, Guid requestedBy, CancellationToken ct)
    {
        var assets = await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id)).OrderBy(a => a.Tag).ToListAsync(ct);
        if (assets.Count == 0)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var bytes = await renderer.RenderAsync(new AssetLabelPdfDocument(await ModelAsync(assets, layout, now, ct)), ct);
        var name = $"etiquetas-{BusinessTime.Today(clock):yyyy-MM-dd}.pdf";
        var stored = await attachments.StoreGeneratedAsync(BatchOwner, requestedBy, name, PdfContentType, bytes, (tenant.Tenant?.Id ?? Guid.Empty).ToString("N"), ct);
        await db.SaveChangesAsync(ct);
        return stored.Id;
    }

    private async Task<AssetLabelModel> ModelAsync(IReadOnlyList<Asset> assets, AssetLabelLayout layout, DateTimeOffset at, CancellationToken ct)
    {
        var resolved = tenant.Resolved;
        var company = resolved?.TradeName is { Length: > 0 } trade ? trade : resolved?.LegalName ?? string.Empty;
        var labels = assets.Select(a => new AssetLabelItem(a.Tag, a.Name, links.Absolute(AssetLabelRules.ShortPath(a.Tag)))).ToList();
        return new AssetLabelModel(labels, company, await parameters.LabelSizeAsync(ct), layout, at);
    }

    private static Error TooMany() =>
        AssetService.Invalid("assetIds", $"Imprima até {AssetLabelRules.BatchMax} etiquetas por vez. Refine o filtro da lista.");
}
