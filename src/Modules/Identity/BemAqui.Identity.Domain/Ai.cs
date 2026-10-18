using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

/// <summary>
/// Provedor de IA do tenant (<c>identity.ai_settings</c>, FND-15, R-AJD-8): uma linha só. A chave fica cifrada com
/// <c>SecretKind.AiApiKey</c> e nunca volta pela API; na auditoria aparece só que mudou.
/// </summary>
public sealed class AiSettings : Entity, IAuditable
{
    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    [ProtectedSecret]
    public string ApiKeyCipher { get; set; } = string.Empty;

    public DateTimeOffset KeySetAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>Uma chamada à IA (<c>identity.ai_usage_logs</c>, FND-15 CA 14): quem, qual provedor e modelo, tokens, duração e resultado.</summary>
public sealed class AiUsageLog : Entity
{
    public const string Answered = "answered";
    public const string NoCitation = "no_citation";
    public const string Failed = "failed";
    public const string TestOk = "test_ok";
    public const string TestFailed = "test_failed";

    public DateTimeOffset At { get; init; }

    public Guid? UserId { get; init; }

    public required string Provider { get; init; }

    public required string Model { get; init; }

    public int? InputTokens { get; init; }

    public int? OutputTokens { get; init; }

    public int DurationMs { get; init; }

    public required string Outcome { get; init; }
}
