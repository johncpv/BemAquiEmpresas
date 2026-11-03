using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Identity;

namespace BemAqui.BuildingBlocks.Pdf;

/// <summary>Dados da empresa para o cabeçalho, sem o logo carregado (o caminho é lido do storage pelo <see cref="PdfFrameFactory"/>).</summary>
public sealed record PdfCompanyInfo(string LegalName, string? TradeName, string? Document, string? Address, string? Phone, string? Email, bool Complete, string? LogoStoragePath);

/// <summary>
/// Fonte dos dados da empresa para o PDF (implementada pelo Identity sobre o mesmo perfil do <c>ICompanyIdentity</c>; ADR 0045 §1). Fica nos
/// BuildingBlocks para o modelo único não depender de módulo.
/// </summary>
public interface IPdfCompanySource
{
    Task<PdfCompanyInfo> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Monta o <see cref="PdfFrame"/> de cada geração: empresa (com o logo do storage), quem emite (nome do usuário atual; "Sistema" em job) e
/// quando. Um lugar só para todo documento novo e para o PDF de lista.
/// </summary>
public sealed class PdfFrameFactory(IPdfCompanySource company, IFileStorage storage, ICurrentUser currentUser, IUserDirectory users, TimeProvider clock)
{
    public const string SystemIssuer = "Sistema";

    public async Task<PdfFrame> CreateAsync(CancellationToken cancellationToken = default)
    {
        var info = await company.GetAsync(cancellationToken);
        byte[]? logo = null;
        if (info.LogoStoragePath is { } path)
        {
            await using var content = await storage.OpenReadAsync(path, cancellationToken);
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            logo = buffer.ToArray();
        }

        var contact = string.Join(" · ", new[] { info.Phone, info.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var pdfCompany = new PdfCompany(info.TradeName ?? info.LegalName, info.LegalName, info.Document, info.Address, contact.Length > 0 ? contact : null, logo, info.Complete);
        var (issuer, at) = await IssuerAsync(cancellationToken);
        return new PdfFrame(pdfCompany, issuer, at);
    }

    /// <summary>Quem emite e quando, sem ler a empresa (a planilha só precisa disto).</summary>
    public async Task<(string Name, DateTimeOffset At)> IssuerAsync(CancellationToken cancellationToken = default)
    {
        var name = currentUser.UserId is { } user ? await users.DisplayNameAsync(user, cancellationToken) ?? "usuário removido" : SystemIssuer;
        return (name, clock.GetUtcNow());
    }
}
