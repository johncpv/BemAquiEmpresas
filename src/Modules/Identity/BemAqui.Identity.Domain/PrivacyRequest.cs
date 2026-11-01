using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

/// <summary>Tipo do pedido do titular (LGPD art. 18, LGPD-01a CA 1).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrivacyRequestKind>))]
public enum PrivacyRequestKind
{
    Confirmation,
    Access,
    Correction,
    Anonymization,
    Deletion,
    Portability,
    ConsentRevocation,
    SharingInformation,
}

/// <summary>Situação do pedido (CA 4): aberto, concluído ou recusado. Nunca é apagado.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrivacyRequestStatus>))]
public enum PrivacyRequestStatus
{
    Open,
    Done,
    Refused,
}

/// <summary>Por onde o titular fez o pedido.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrivacyRequestChannel>))]
public enum PrivacyRequestChannel
{
    Email,
    Phone,
    InPerson,
    Letter,
    Other,
}

/// <summary>Como o Proprietário conferiu a identidade do titular (CA 5), sem copiar o documento.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IdentityCheckMethod>))]
public enum IdentityCheckMethod
{
    DocumentPresented,
    RegisteredEmail,
    RegisteredPhone,
    InPerson,
}

/// <summary>Situação da exportação dos dados do titular (CA 8): pedida, pronta ou com falha (o módulo que falhou vai na mensagem).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrivacyExportStatus>))]
public enum PrivacyExportStatus
{
    None,
    Running,
    Ready,
    Failed,
    Expired,
}

/// <summary>Situação da anonimização do titular (LGPD-01b CA 4 e 5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrivacyAnonymizationStatus>))]
public enum PrivacyAnonymizationStatus
{
    None,
    Running,
    Partial,
    Done,
}

/// <summary>Situação de uma etapa (um módulo) da anonimização.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrivacyAnonymizationStepStatus>))]
public enum PrivacyAnonymizationStepStatus
{
    Pending,
    Done,
    Failed,
}

/// <summary>
/// Pedido do titular (<c>settings.privacy_requests</c>, LGPD-01a, ADR 0018 e 0037): atendido só pelo Proprietário (o controlador). O titular é
/// um cadastro (terceiro PF, contato, funcionário ou usuário) pelo id, ou "não encontrado" com nome e contato (<c>[PersonalData]</c>). Prazo em
/// dias corridos (<c>privacy.response_days</c>). A identidade conferida (como, por quem, quando) é exigida antes de exportar. A exportação é um
/// arquivo do sistema (dono <c>PrivacyRequest</c>) baixado por link de uso único que vence em 15 min (só o hash fica aqui) e apagado 30 dias
/// depois da conclusão. Nunca é apagado.
/// </summary>
[DocumentName("O pedido do titular")]
public sealed class PrivacyRequest : Entity, IAuditable, IConcurrencyVersioned
{
    public const int NotesMaxLength = 500;
    public const string NumberType = "LGPD";
    public const string OwnerType = "PrivacyRequest";

    public string Number { get; init; } = string.Empty;

    public PersonalDataSubjectKind SubjectType { get; init; }

    public Guid? SubjectId { get; init; }

    /// <summary>Nome do titular: o do cadastro na hora do pedido, ou o informado quando "não encontrado".</summary>
    [PersonalData]
    public string SubjectName { get; init; } = string.Empty;

    /// <summary>Meio de contato informado (só no "não encontrado").</summary>
    [PersonalData]
    public string? SubjectContact { get; init; }

    public PrivacyRequestKind Kind { get; init; }

    public DateOnly RequestedOn { get; init; }

    public PrivacyRequestChannel Channel { get; init; }

    [PersonalData]
    public string? Notes { get; init; }

    public DateOnly DueOn { get; init; }

    public PrivacyRequestStatus Status { get; set; } = PrivacyRequestStatus.Open;

    public DateTimeOffset? IdentityVerifiedAt { get; set; }

    public Guid? IdentityVerifiedBy { get; set; }

    public IdentityCheckMethod? IdentityMethod { get; set; }

    /// <summary>Data em que a identidade foi conferida (informada pelo Proprietário).</summary>
    public DateOnly? IdentityCheckedOn { get; set; }

    [PersonalData]
    public string? IdentityNotes { get; set; }

    /// <summary>O que foi feito (até 500).</summary>
    [PersonalData]
    public string? Response { get; set; }

    public DateOnly? RespondedOn { get; set; }

    public bool? FileSent { get; set; }

    [PersonalData]
    public string? RefusalReason { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public Guid? ClosedBy { get; set; }

    /// <summary>Link do cadastro aberto pela "Correção" (CA 11): rota relativa do app.</summary>
    public string? CorrectionLink { get; set; }

    public PrivacyExportStatus ExportStatus { get; set; } = PrivacyExportStatus.None;

    public Guid? ExportAttachmentId { get; set; }

    public DateTimeOffset? ExportedAt { get; set; }

    /// <summary>Módulo que falhou na última exportação (sem dado pessoal).</summary>
    public string? ExportError { get; set; }

    /// <summary>SHA-256 do link de download de uso único; nulo depois de usado.</summary>
    [ProtectedSecret]
    public string? DownloadTokenHash { get; set; }

    public DateTimeOffset? DownloadTokenExpiresAt { get; set; }

    /// <summary>Situação da anonimização (LGPD-01b CA 4 e 5): rodando, parcial (algum módulo falhou) ou concluída.</summary>
    public PrivacyAnonymizationStatus AnonymizationStatus { get; set; } = PrivacyAnonymizationStatus.None;

    /// <summary>JSONB: uma etapa por módulo (situação, quando, o erro sem dado pessoal e os itens), para refazer só o que falta (CA 5).</summary>
    public string? AnonymizationSteps { get; set; }

    /// <summary>JSONB: o resumo da anonimização (quantos anonimizados, retidos e mantidos, por módulo), sem dado pessoal.</summary>
    public string? AnonymizationSummary { get; set; }

    public DateTimeOffset? AnonymizedAt { get; set; }

    /// <summary>Quem confirmou a anonimização (ou a última retentativa).</summary>
    public Guid? AnonymizedBy { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public uint Version { get; set; }

    public bool IsIdentityVerified => IdentityVerifiedAt is not null;
}

/// <summary>Regras do pedido sem banco (CA 1, 3 e 5).</summary>
public static class PrivacyRequestRules
{
    public const int DefaultResponseDays = 15;
    public const int MinResponseDays = 1;
    public const int MaxResponseDays = 30;
    public const int WarningDays = 3;
    public static readonly TimeSpan DownloadLinkLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ExportRetention = TimeSpan.FromDays(30);

    public const string IdentityNotVerifiedCode = "subject_identity_not_verified";
    public const string IdentityNotVerifiedMessage = "Confira a identidade do titular antes de exportar os dados.";

    /// <summary>Prazo em dias corridos a partir da data do pedido (CA 1).</summary>
    public static DateOnly DueOn(DateOnly requestedOn, int days) => requestedOn.AddDays(days);

    /// <summary>Tipos que exigem a identidade conferida antes de agir (CA 5).</summary>
    public static bool RequiresIdentity(PrivacyRequestKind kind) =>
        kind is PrivacyRequestKind.Access or PrivacyRequestKind.Portability or PrivacyRequestKind.Correction or PrivacyRequestKind.Anonymization or PrivacyRequestKind.Deletion;

    /// <summary>Tipos atendidos por anonimização (LGPD-01b CA 2): a eliminação também, porque documento com movimento não pode ser apagado.</summary>
    public static bool Anonymizes(PrivacyRequestKind kind) => kind is PrivacyRequestKind.Anonymization or PrivacyRequestKind.Deletion;

    public const string DeletionNotice = "A eliminação é atendida por anonimização: documentos com movimento não podem ser apagados.";

    public const string BackupNotice = "Os backups expiram em até 30 dias (FND-10b) e não são restaurados com dado anonimizado sem refazer a anonimização.";

    /// <summary>Tipos que exportam os dados (CA 8).</summary>
    public static bool Exports(PrivacyRequestKind kind) => kind is PrivacyRequestKind.Access or PrivacyRequestKind.Portability;

    /// <summary>Chip do prazo na lista (CA 3): crítico vencido, alerta a 3 dias ou menos, nada nos demais e nos fechados.</summary>
    public static string? DueChip(PrivacyRequestStatus status, DateOnly due, DateOnly today) =>
        status != PrivacyRequestStatus.Open ? null : due < today ? "critical" : due.DayNumber - today.DayNumber <= WarningDays ? "warning" : null;

    /// <summary>Avisa quando faltam 3 dias ou menos ou já venceu (CA 3).</summary>
    public static bool ShouldNotify(PrivacyRequestStatus status, DateOnly due, DateOnly today) => DueChip(status, due, today) is not null;

    /// <summary>Nome do titular fora da tela do Proprietário (CA 12, padrão 8): "Maria S." e o resto oculto.</summary>
    public static string MaskName(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch { 0 => "—", 1 => parts[0], _ => parts[0] + " " + parts[^1][0] + "." };
    }

    public static string KindLabel(PrivacyRequestKind kind) => kind switch
    {
        PrivacyRequestKind.Confirmation => "Confirmação",
        PrivacyRequestKind.Access => "Acesso",
        PrivacyRequestKind.Correction => "Correção",
        PrivacyRequestKind.Anonymization => "Anonimização",
        PrivacyRequestKind.Deletion => "Eliminação",
        PrivacyRequestKind.Portability => "Portabilidade",
        PrivacyRequestKind.ConsentRevocation => "Revogação de consentimento",
        _ => "Informação sobre compartilhamento",
    };
}
