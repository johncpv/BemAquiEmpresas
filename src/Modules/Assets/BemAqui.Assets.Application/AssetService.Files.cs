using BemAqui.BuildingBlocks.Attachments;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// Foto e documentos do bem (CA 8, revisão pré-Fases 4 a 8 §4.3): donos <c>AssetPhoto</c> (PNG, JPG, WebP até 5 MB, sem EXIF/GPS) e
/// <c>Asset</c> (PDF, PNG, JPG até 10 MB), leitura com <c>assets.view</c> e escrita com <c>assets.edit</c> pela rota do bem. O anexo é
/// autorizado pelo registro: precisa ser do bem da rota (de outro, 404). Storage com o prefixo do tenant.
/// </summary>
public sealed partial class AssetService
{
    public async Task<Result<AssetFiles>> FilesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.Assets.AnyAsync(a => a.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var photos = await attachments.ListCheckedAsync(PhotoOwner, id, cancellationToken);
        var documents = await attachments.ListCheckedAsync(DocumentOwner, id, cancellationToken);
        return new AssetFiles(photos.FirstOrDefault(p => p.ScanStatus == ScanStatus.Clean)?.Id, photos, documents);
    }

    public Task<Result<AttachmentDto>> UploadPhotoAsync(Guid id, string fileName, Stream content, CancellationToken cancellationToken = default) =>
        UploadAsync(PhotoOwner, id, fileName, content, cancellationToken);

    public Task<Result<AttachmentDto>> UploadDocumentAsync(Guid id, string fileName, Stream content, CancellationToken cancellationToken = default) =>
        UploadAsync(DocumentOwner, id, fileName, content, cancellationToken);

    /// <summary>Download da foto ou do documento do bem da rota; de outro bem, 404 (IDOR, CA 13).</summary>
    public async Task<Result<AttachmentContent>> FileAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        if (!await db.Assets.AnyAsync(a => a.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var photo = await attachments.DownloadCheckedAsync(PhotoOwner, id, attachmentId, cancellationToken);
        return photo.IsFailure && photo.Error!.Type == ErrorType.NotFound
            ? await attachments.DownloadCheckedAsync(DocumentOwner, id, attachmentId, cancellationToken)
            : photo;
    }

    private async Task<Result<AttachmentDto>> UploadAsync(string owner, Guid id, string fileName, Stream content, CancellationToken ct)
    {
        if (!await db.Assets.AnyAsync(a => a.Id == id, ct))
        {
            return NotFound();
        }

        return await attachments.UploadCheckedAsync(owner, id, fileName, content, StoragePrefix(), ct);
    }

    /// <summary>Responsáveis (CA 4): só funcionários ativos pelo <c>IEmployees</c>, sem dado sensível.</summary>
    public async Task<IReadOnlyList<AssetResponsibleChoice>> ResponsibleChoicesAsync(CancellationToken cancellationToken = default) =>
        (await employees.ActiveAsync(cancellationToken)).OrderBy(e => e.Name).Select(e => new AssetResponsibleChoice(e.Id, e.Name, e.Registration, e.PositionName)).ToList();

    public async Task<IReadOnlyList<AssetWorkCenterChoice>> WorkCenterChoicesAsync(CancellationToken cancellationToken = default) =>
        (await workCenters.ActiveAsync(cancellationToken)).OrderBy(c => c.Name).Select(c => new AssetWorkCenterChoice(c.Id, c.Name, c.Line)).ToList();

    /// <summary>Fornecedores (CA 7): papel Fornecedor do <c>IParties</c>, só ativos.</summary>
    public async Task<IReadOnlyList<AssetSupplierChoice>> SupplierChoicesAsync(string? q, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(q, PartyRoleKind.Supplier, 20, cancellationToken)).Select(p => new AssetSupplierChoice(p.Id, p.TradeName ?? p.LegalName, p.Document)).ToList();

    /// <summary>Notas de entrada (CA 7) pelo <c>IIncomingInvoices</c> do Fiscal: só as importadas pela FIS-04a.</summary>
    public async Task<IReadOnlyList<AssetInvoiceChoice>> InvoiceChoicesAsync(string? q, CancellationToken cancellationToken = default) =>
        (await invoices.SearchAsync(q, 20, cancellationToken)).Select(i => new AssetInvoiceChoice(i.Id, i.Number, i.Series, i.IssuerPartyId, i.IssuerName, i.IssueDate)).ToList();
}
