using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BemAqui.Identity.Application.Ai;

namespace BemAqui.Identity.Infrastructure.Ai;

/// <summary>
/// Base dos adaptadores (R-AJD-8): só <c>HttpClient</c>, chave sempre em cabeçalho (nunca na URL, que pode ir para log) e
/// erros HTTP traduzidos em <see cref="AiFailureKind"/>. O corpo do erro não é guardado, porque alguns provedores o ecoam.
/// </summary>
internal abstract class HttpAiProvider(HttpClient http) : IAiProvider
{
    public abstract string Code { get; }

    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = Build(request);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException(AiFailureKind.Network, "HTTP " + ex.HttpRequestError);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new AiProviderException(Classify(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken)), "HTTP " + (int)response.StatusCode);
            }

            JsonNode? body;
            try
            {
                body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (System.Text.Json.JsonException)
            {
                throw new AiProviderException(AiFailureKind.Provider, "resposta não é JSON");
            }

            return Read(body) ?? throw new AiProviderException(AiFailureKind.Provider, "resposta sem texto");
        }
    }

    protected abstract HttpRequestMessage Build(AiRequest request);

    protected abstract AiCompletion? Read(JsonNode? body);

    /// <summary>Status e, quando ajuda, o código do erro no corpo (Gemini responde 400 para chave inválida).</summary>
    protected virtual AiFailureKind Classify(HttpStatusCode status, string body) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiFailureKind.InvalidKey,
        HttpStatusCode.NotFound => AiFailureKind.ModelNotFound,
        HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired => AiFailureKind.RateLimited,
        HttpStatusCode.BadRequest when body.Contains("model", StringComparison.OrdinalIgnoreCase) => AiFailureKind.ModelNotFound,
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => AiFailureKind.Timeout,
        _ => AiFailureKind.Provider,
    };

    protected static StringContent Json(JsonNode node) => new(node.ToJsonString(), Encoding.UTF8, "application/json");

    protected static int? Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var n) ? n : null;
}

/// <summary>Anthropic Messages API (<c>POST /v1/messages</c>).</summary>
internal sealed class AnthropicProvider(HttpClient http) : HttpAiProvider(http)
{
    public override string Code => AiProviders.Anthropic;

    protected override HttpRequestMessage Build(AiRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = Json(new JsonObject
            {
                ["model"] = request.Model,
                ["max_tokens"] = request.MaxOutputTokens,
                ["system"] = request.System,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = request.User }),
            }),
        };
        message.Headers.Add("x-api-key", request.ApiKey.Reveal());
        message.Headers.Add("anthropic-version", "2023-06-01");
        return message;
    }

    protected override AiCompletion? Read(JsonNode? body)
    {
        var text = body?["content"] is JsonArray parts
            ? string.Concat(parts.Where(p => (string?)p?["type"] == "text").Select(p => (string?)p?["text"]))
            : null;
        return string.IsNullOrEmpty(text) ? null : new AiCompletion(text, Int(body?["usage"]?["input_tokens"]), Int(body?["usage"]?["output_tokens"]));
    }
}

/// <summary>OpenAI Chat Completions (<c>POST /v1/chat/completions</c>).</summary>
internal sealed class OpenAiProvider(HttpClient http) : HttpAiProvider(http)
{
    public override string Code => AiProviders.OpenAi;

    protected override HttpRequestMessage Build(AiRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = Json(new JsonObject
            {
                ["model"] = request.Model,
                ["max_completion_tokens"] = request.MaxOutputTokens,
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = request.System },
                    new JsonObject { ["role"] = "user", ["content"] = request.User }),
            }),
        };
        message.Headers.Authorization = new("Bearer", request.ApiKey.Reveal());
        return message;
    }

    protected override AiCompletion? Read(JsonNode? body)
    {
        var text = (string?)body?["choices"]?[0]?["message"]?["content"];
        return string.IsNullOrEmpty(text) ? null : new AiCompletion(text, Int(body?["usage"]?["prompt_tokens"]), Int(body?["usage"]?["completion_tokens"]));
    }
}

/// <summary>Google Gemini (<c>POST /v1beta/models/{modelo}:generateContent</c>), chave no cabeçalho <c>x-goog-api-key</c>.</summary>
internal sealed class GeminiProvider(HttpClient http) : HttpAiProvider(http)
{
    public override string Code => AiProviders.Gemini;

    protected override HttpRequestMessage Build(AiRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "v1beta/models/" + Uri.EscapeDataString(request.Model) + ":generateContent")
        {
            Content = Json(new JsonObject
            {
                ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.System }) },
                ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = request.User }) }),
                ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = request.MaxOutputTokens },
            }),
        };
        message.Headers.Add("x-goog-api-key", request.ApiKey.Reveal());
        return message;
    }

    protected override AiFailureKind Classify(HttpStatusCode status, string body) =>
        status == HttpStatusCode.BadRequest && body.Contains("API_KEY_INVALID", StringComparison.Ordinal) ? AiFailureKind.InvalidKey : base.Classify(status, body);

    protected override AiCompletion? Read(JsonNode? body)
    {
        var text = body?["candidates"]?[0]?["content"]?["parts"] is JsonArray parts ? string.Concat(parts.Select(p => (string?)p?["text"])) : null;
        return string.IsNullOrEmpty(text) ? null : new AiCompletion(text, Int(body?["usageMetadata"]?["promptTokenCount"]), Int(body?["usageMetadata"]?["candidatesTokenCount"]));
    }
}
