using System.Security.Cryptography;
using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Attachments;

/// <summary>Arquivo enviado que já passou pela allowlist do dono, pelos magic bytes e pelo antivírus, antes de qualquer leitura.</summary>
public sealed record ScreenedFile(string FileName, AllowedFileType Type, byte[] Content, string Sha256)
{
    public override string ToString() => $"ScreenedFile({Type.Name}, {Content.Length} bytes)";
}

public sealed partial class AttachmentService
{
    public const string ScanUnavailableMessage = "O antivírus está indisponível agora e o arquivo precisa ser verificado antes da leitura. Tente de novo em alguns minutos.";

    /// <summary>
    /// Triagem do arquivo que o módulo vai LER antes de gravar o documento (BNK-03a CA 4: o OFX passa pelo antivírus antes do parser):
    /// permissão de gravar do dono, limite de envios, tamanho, tipo (com os <see cref="AttachmentOwnerRules.ExtraTypes"/>) e a varredura.
    /// Antivírus indisponível recusa (falha fechada: o arquivo não pode ser lido sem a varredura). Nada é gravado aqui.
    /// </summary>
    public async Task<Result<ScreenedFile>> ScreenAsync(string ownerType, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var rules = owners.RulesOf(ownerType);
        if (rules is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Este tipo de documento não aceita anexos.");
        }

        if (!await AllowedAsync(rules, rules.WriteAction, cancellationToken))
        {
            return Forbidden();
        }

        if (await TooManyUploadsAsync(cancellationToken))
        {
            return Error.TooManyRequests("attachment.too_many_uploads", "Muitos envios em pouco tempo. Tente de novo em alguns minutos.");
        }

        var max = rules.MaxSize ?? AttachmentPolicy.MaxSize;
        if (content.Length == 0 || content.Length > max)
        {
            return Error.Validation("attachment.size", $"O arquivo precisa ter até {max / 1024 / 1024} MB e não pode estar vazio.");
        }

        var type = AttachmentPolicy.Match(fileName, content.AsSpan(0, Math.Min(HeadSize, content.Length)), rules.ExtraTypes);
        if (type is null || (rules.ContentTypes is { } allowed && !allowed.Contains(type.ContentType)))
        {
            var names = rules.ContentTypes is { } only
                ? string.Join(", ", AttachmentPolicy.Types.Concat(rules.ExtraTypes ?? []).Where(t => only.Contains(t.ContentType)).Select(t => t.Name))
                : "um tipo permitido";
            return Error.Validation("attachment.type_not_allowed", "Tipo de arquivo não permitido aqui. Envie " + names + ".");
        }

        using (var stream = new MemoryStream(content, writable: false))
        {
            var scan = await scanner.ScanAsync(stream, cancellationToken);
            switch (scan.Verdict)
            {
                case ScanVerdict.Infected:
                    return Error.Validation("attachment.infected", "O arquivo foi recusado: o antivírus encontrou uma ameaça.");
                case ScanVerdict.TooLarge:
                    return Error.Validation("attachment.too_large_to_scan", "O arquivo é grande demais para o antivírus e foi recusado.");
                case ScanVerdict.Unavailable:
                    return Error.Conflict("attachment.scan_unavailable", ScanUnavailableMessage);
            }
        }

        return new ScreenedFile(SafeDisplayName(fileName), type, content, Convert.ToHexStringLower(SHA256.HashData(content)));
    }

    /// <summary>
    /// Grava o arquivo triado (<see cref="ScreenAsync"/>) como anexo do dono, já limpo, na unidade de trabalho do chamador (o chamador
    /// salva). Sem rota: o dono que lê o arquivo é quem grava (ex.: o lote do extrato).
    /// </summary>
    public async Task<Attachment> StoreScreenedAsync(string ownerType, Guid ownerId, ScreenedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var buffer = new MemoryStream(file.Content, writable: false);
        var path = await storage.SaveAsync(buffer, cancellationToken);
        var now = clock.GetUtcNow();
        var attachment = new Attachment
        {
            OwnerType = ownerType, OwnerId = ownerId, FileName = file.FileName, ContentType = file.Type.ContentType, Size = file.Content.Length, Sha256 = file.Sha256,
            StoragePath = path, ScanStatus = ScanStatus.Clean, ScannedAt = now, CreatedAt = now, UploadedBy = currentUser.UserId, Origin = AttachmentOrigin.User,
        };
        db.Attachments.Add(attachment);
        return attachment;
    }
}
