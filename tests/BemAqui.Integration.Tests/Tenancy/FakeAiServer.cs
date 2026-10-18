using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>Uma chamada que chegou ao "provedor": host, cabeçalhos (com a chave) e corpo (o prompt).</summary>
public sealed record FakeAiCall(string Host, string Path, IReadOnlyDictionary<string, string> Headers, string Body);

/// <summary>
/// Servidor HTTP falso dos três provedores (FND-15): grava cada chamada e responde no formato de cada API com o texto
/// definido pelo teste. <see cref="Status"/> diferente de 200 simula erro; <see cref="Delay"/> simula lentidão.
/// </summary>
public sealed class FakeAiServer : HttpMessageHandler
{
    public List<FakeAiCall> Calls { get; } = [];

    public string Reply { get; set; } = """{"resposta":"Ok.","fontes":["a1"],"naoEncontrou":false}""";

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public string ErrorBody { get; set; } = """{"error":{"message":"falhou"}}""";

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(",", h.Value));
        lock (Calls)
        {
            Calls.Add(new FakeAiCall(request.RequestUri!.Host, request.RequestUri.AbsolutePath, headers, body));
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        if (Status != HttpStatusCode.OK)
        {
            return new HttpResponseMessage(Status) { Content = new StringContent(ErrorBody, Encoding.UTF8, "application/json") };
        }

        JsonNode response = request.RequestUri.Host switch
        {
            "api.anthropic.com" => new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = Reply }),
                ["usage"] = new JsonObject { ["input_tokens"] = 321, ["output_tokens"] = 45 },
            },
            "api.openai.com" => new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = Reply } }),
                ["usage"] = new JsonObject { ["prompt_tokens"] = 300, ["completion_tokens"] = 40 },
            },
            _ => new JsonObject
            {
                ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = Reply }) } }),
                ["usageMetadata"] = new JsonObject { ["promptTokenCount"] = 310, ["candidatesTokenCount"] = 42 },
            },
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
