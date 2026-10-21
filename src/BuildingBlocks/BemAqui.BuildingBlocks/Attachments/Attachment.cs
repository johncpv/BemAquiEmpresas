using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Attachments;

public enum ScanStatus
{
    Pending,
    Clean,
    Infected,
    Failed,
}

/// <summary>Quem criou o arquivo: o usuário (upload) ou o sistema (PDF gerado, COM-03).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<AttachmentOrigin>))]
public enum AttachmentOrigin
{
    User,
    System,
}

/// <summary>
/// Anexo de um documento (<c>shared.attachments</c>). O arquivo fica em caminho aleatório no storage; o nome
/// enviado pelo usuário é só para exibição. Download só com varredura <see cref="ScanStatus.Clean"/> (falha fechada).
/// </summary>
public sealed class Attachment : Entity, IAuditable
{
    public required string OwnerType { get; init; }

    public Guid OwnerId { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public long Size { get; init; }

    public required string Sha256 { get; init; }

    public required string StoragePath { get; set; }

    public ScanStatus ScanStatus { get; set; }

    public DateTimeOffset? ScannedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? UploadedBy { get; init; }

    public AttachmentOrigin Origin { get; init; } = AttachmentOrigin.User;
}
