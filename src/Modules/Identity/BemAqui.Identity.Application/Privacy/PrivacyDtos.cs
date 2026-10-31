using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Identity.Application.Privacy;

/// <summary>Linha da lista (CA 3 e 12): o titular sai mascarado ("Maria S."); <c>DueChip</c> é <c>warning</c>, <c>critical</c> ou nulo.</summary>
public sealed record PrivacyRequestRow(
    Guid Id, string Number, PersonalDataSubjectKind SubjectType, string SubjectMasked, PrivacyRequestKind Kind, DateOnly RequestedOn, DateOnly DueOn,
    PrivacyRequestStatus Status, string? DueChip, bool IdentityVerified, PrivacyExportStatus ExportStatus);

/// <summary>Detalhe do pedido, só para o Proprietário (CA 1 a 12): o titular em claro, a conferência, a resposta e a exportação.</summary>
public sealed record PrivacyRequestDetail(
    Guid Id,
    string Number,
    PersonalDataSubjectKind SubjectType,
    Guid? SubjectId,
    string SubjectName,
    string? SubjectContact,
    PrivacyRequestKind Kind,
    DateOnly RequestedOn,
    PrivacyRequestChannel Channel,
    string? Notes,
    DateOnly DueOn,
    string? DueChip,
    PrivacyRequestStatus Status,
    bool RequiresIdentity,
    bool IdentityVerified,
    DateTimeOffset? IdentityVerifiedAt,
    string? IdentityVerifiedByName,
    IdentityCheckMethod? IdentityMethod,
    DateOnly? IdentityCheckedOn,
    string? IdentityNotes,
    string? Response,
    DateOnly? RespondedOn,
    bool? FileSent,
    string? RefusalReason,
    DateTimeOffset? ClosedAt,
    string? CorrectionLink,
    bool CanExport,
    PrivacyExportStatus ExportStatus,
    DateTimeOffset? ExportedAt,
    string? ExportError,
    DateTimeOffset? ExportExpiresAt,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record CreatePrivacyRequestInput(
    PersonalDataSubjectKind SubjectType, Guid? SubjectId, string? SubjectName, string? SubjectContact, PrivacyRequestKind Kind, DateOnly RequestedOn,
    PrivacyRequestChannel Channel, string? Notes);

public sealed record VerifyIdentityInput(IdentityCheckMethod Method, DateOnly CheckedOn, string? Notes, uint Version);

public sealed record ConcludePrivacyRequestInput(DateOnly RespondedOn, string Response, bool FileSent, uint Version);

public sealed record RefusePrivacyRequestInput(string Reason, uint Version);

public sealed record PrivacyVersionInput(uint Version);

/// <summary>Link de download de uso único (CA 10): vence em 15 minutos e só vale com login do Proprietário.</summary>
public sealed record PrivacyDownloadLink(string Url, DateTimeOffset ExpiresAt, string FileName);

/// <summary>"Abrir cadastro" (CA 11): para onde ir corrigir.</summary>
public sealed record PrivacyCorrectionTarget(string Link, PrivacyRequestDetail Request);

public sealed record PrivacySettingsDto(int ResponseDays, Guid? SettingId);

public sealed record PrivacySettingsInput(int ResponseDays);

/// <summary>Resultado de "Exportar dados do titular" (CA 8): a exportação vai para a fila.</summary>
public sealed record PrivacyExportStarted(PrivacyRequestDetail Request, string Message);

/// <summary>Linha do histórico do pedido (CA 12): quem, quando, o quê (com os campos pessoais mascarados) e o IP.</summary>
public sealed record PrivacyHistoryEntry(Guid Id, DateTimeOffset At, string Action, string UserName, string? Ip, string Changes);

public sealed record PrivacyExportFile(Stream Content, string FileName, string ContentType);
