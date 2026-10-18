using BemAqui.SharedKernel;

namespace BemAqui.Identity.Application.Ai;

/// <summary>Uma chamada à IA: instruções fixas (system) e o conteúdo (pergunta + trechos). Nada além disso sai do sistema.</summary>
public sealed record AiRequest(string Model, Secret ApiKey, string System, string User, int MaxOutputTokens = 800);

public sealed record AiCompletion(string Text, int? InputTokens, int? OutputTokens);

/// <summary>Falha traduzida do provedor (FND-15 CA 11): o tipo vira mensagem em PT-BR; o detalhe técnico nunca traz a chave.</summary>
public enum AiFailureKind
{
    InvalidKey,
    ModelNotFound,
    Network,
    Timeout,
    RateLimited,
    Provider,
}

public sealed class AiProviderException(AiFailureKind kind, string detail) : Exception(detail)
{
    public AiFailureKind Kind { get; } = kind;

    public string UserMessage => Kind switch
    {
        AiFailureKind.InvalidKey => "A chave de API foi recusada pelo provedor. Confira se copiou a chave inteira.",
        AiFailureKind.ModelNotFound => "O provedor não encontrou esse modelo. Confira o nome do modelo.",
        AiFailureKind.Network => "Não foi possível falar com o provedor. Verifique a rede e tente de novo.",
        AiFailureKind.Timeout => "O provedor demorou demais para responder. Tente de novo em instantes.",
        AiFailureKind.RateLimited => "O provedor recusou por excesso de uso ou falta de saldo. Confira a conta no provedor.",
        _ => "O provedor respondeu com erro. Tente de novo em instantes.",
    };
}

/// <summary>Adaptador de um provedor de IA (R-AJD-8). Só <c>HttpClient</c>, sem SDK.</summary>
public interface IAiProvider
{
    /// <summary>Código gravado em <c>ai_settings.provider</c>: <c>anthropic</c>, <c>gemini</c> ou <c>openai</c>.</summary>
    string Code { get; }

    /// <summary>Lança <see cref="AiProviderException"/> em qualquer falha.</summary>
    Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken);
}

/// <summary>Provedor oferecido na tela, com os modelos sugeridos (a lista é do sistema; "Outro" aceita qualquer nome).</summary>
public sealed record AiProviderInfo(string Code, string Name, IReadOnlyList<string> Models);

public static class AiProviders
{
    public const string Anthropic = "anthropic";
    public const string Gemini = "gemini";
    public const string OpenAi = "openai";

    public static IReadOnlyList<AiProviderInfo> All { get; } =
    [
        new(Anthropic, "Anthropic Claude", ["claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5"]),
        new(Gemini, "Google Gemini", ["gemini-2.5-flash", "gemini-2.5-pro"]),
        new(OpenAi, "OpenAI", ["gpt-5-mini", "gpt-5"]),
    ];

    public static AiProviderInfo? Find(string? code) => All.FirstOrDefault(p => p.Code == code);
}

/// <summary>Seção <c>Ai</c>: tempo limite da chamada (sem nova tentativa automática) e endereços dos provedores (trocados só em teste).</summary>
public sealed class AiOptions
{
    public const string Section = "Ai";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

    public Uri AnthropicBaseUrl { get; set; } = new("https://api.anthropic.com/");

    public Uri GeminiBaseUrl { get; set; } = new("https://generativelanguage.googleapis.com/");

    public Uri OpenAiBaseUrl { get; set; } = new("https://api.openai.com/");
}
