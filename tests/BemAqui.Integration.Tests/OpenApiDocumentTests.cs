using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace BemAqui.Integration.Tests;

/// <summary>
/// FND-09c CA 1: o OpenAPI do Host versionado em <c>src/Web/openapi.json</c> é a entrada do Orval. Este teste falha se
/// ele estiver desatualizado; para atualizar, rode com <c>BQ_UPDATE_OPENAPI=1</c> (ex.: <c>BQ_UPDATE_OPENAPI=1 sh scripts/test-docker.sh</c>).
/// </summary>
[Collection(PostgresTestGroup.Name)]
public class OpenApiDocumentTests(PostgresFixture postgres)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public async Task OpenAPI_versionado_para_o_Orval_esta_atualizado()
    {
        await using var factory = HostSmokeTests.Factory(postgres.ConnectionString).WithWebHostBuilder(b => b.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var document = JsonNode.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken))!.AsObject();
        document.Remove("servers"); // depende do host do teste
        var current = document.ToJsonString(Indented).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        var path = Path.Combine(RepositoryRoot(), "src", "Web", "openapi.json");

        if (Environment.GetEnvironmentVariable("BQ_UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(path, current, TestContext.Current.CancellationToken);
        }

        File.Exists(path).Should().BeTrue("o OpenAPI precisa estar versionado para o Orval");
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Should().Be(current, "a API mudou: rode BQ_UPDATE_OPENAPI=1 sh scripts/test-docker.sh e depois pnpm gen:api em src/Web");
    }

    /// <summary>
    /// INT-03 CA 24: o documento <c>integration-v1</c> (só <c>/api/v1/integration</c>) é comparado ao contrato versionado
    /// <c>docs/integracoes/openapi-integration-v1.json</c>. Remover ou renomear campo, rota ou código falha mesmo com <c>BQ_UPDATE_OPENAPI=1</c> (v1 só
    /// cresce de forma aditiva); o que cresce entra com a atualização, junto com a referência das rotas que a tela do Conecta mostra.
    /// </summary>
    [Fact]
    public async Task OpenAPI_da_integracao_v1_so_cresce_de_forma_aditiva()
    {
        await using var factory = HostSmokeTests.Factory(postgres.ConnectionString).WithWebHostBuilder(b => b.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var document = JsonNode.Parse(await client.GetStringAsync(new Uri("/openapi/integration-v1.json", UriKind.Relative), TestContext.Current.CancellationToken))!.AsObject();
        document.Remove("servers");
        document["paths"]!.AsObject().Select(p => p.Key).Should().OnlyContain(p => p.StartsWith("/api/v1/integration/", StringComparison.Ordinal), "o documento é só da integração");
        var current = document.ToJsonString(Indented).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        var path = Path.Combine(RepositoryRoot(), "docs", "integracoes", "openapi-integration-v1.json");
        var reference = Path.Combine(RepositoryRoot(), "src", "Web", "src", "pages", "settings", "integrations", "integration-v1-routes.ts");
        var routes = Routes(document);

        if (File.Exists(path))
        {
            var removed = new List<string>();
            Removed(JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)), document, "$", removed);
            removed.Should().BeEmpty("v1 só cresce de forma aditiva: quebra vai para /api/v2 (INT-03 CA 24)");
        }

        if (Environment.GetEnvironmentVariable("BQ_UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(path, current, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(reference, routes, TestContext.Current.CancellationToken);
        }

        File.Exists(path).Should().BeTrue("o contrato da integração precisa estar versionado");
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Should().Be(current, "a API de integração mudou: rode BQ_UPDATE_OPENAPI=1 sh scripts/test-docker.sh --filter-class \"*OpenApiDocumentTests\"");
        (await File.ReadAllTextAsync(reference, TestContext.Current.CancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Should().Be(routes, "a referência das rotas da tela do Conecta vem do mesmo documento");
    }

    /// <summary>Método, rota, escopo e resumo de cada operação, na ordem do documento (a referência "Para desenvolvedores" da tela do Conecta).</summary>
    private static string Routes(JsonObject document)
    {
        var list = new JsonArray();
        foreach (var (route, item) in document["paths"]!.AsObject())
        {
            foreach (var (method, operation) in item!.AsObject())
            {
                list.Add(new JsonObject
                {
                    ["method"] = method.ToUpperInvariant(),
                    ["path"] = route,
                    ["scope"] = operation?["x-scope"]?.GetValue<string>(),
                    ["operationId"] = operation?["operationId"]?.GetValue<string>(),
                });
            }
        }

        return "// Gerado pelo OpenApiDocumentTests (INT-03 CA 24) a partir do documento integration-v1. Não edite à mão: rode BQ_UPDATE_OPENAPI=1.\n"
            + "export type IntegrationRoute = { method: string; path: string; scope: string | null; operationId: string | null }\n\n"
            + "export const INTEGRATION_V1_ROUTES: readonly IntegrationRoute[] = " + list.ToJsonString(Indented).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    /// <summary>Tudo o que existia no contrato e sumiu: chave de objeto, item de lista de texto (códigos, enums) ou valor trocado (fora textos livres).</summary>
    private static void Removed(JsonNode? before, JsonNode? after, string at, List<string> removed)
    {
        switch (before)
        {
            case JsonObject old:
                if (after is not JsonObject now)
                {
                    removed.Add(at);
                    return;
                }

                foreach (var (key, value) in old)
                {
                    if (!now.ContainsKey(key))
                    {
                        removed.Add(at + "." + key);
                    }
                    else if (key is not ("description" or "summary" or "title" or "required"))
                    {
                        Removed(value, now[key], at + "." + key, removed);
                    }
                }

                break;
            case JsonArray list when list.All(i => i is JsonValue):
                var present = (after as JsonArray)?.Select(i => i?.ToJsonString()).ToHashSet(StringComparer.Ordinal) ?? [];
                removed.AddRange(list.Select(i => i?.ToJsonString()).Where(i => !present.Contains(i)).Select(i => at + "[" + i + "]"));
                break;
            case JsonArray list:
                for (var i = 0; i < list.Count; i++)
                {
                    Removed(list[i], (after as JsonArray) is { } other && i < other.Count ? other[i] : null, at + "[" + i + "]", removed);
                }

                break;
            case JsonValue value when after is not JsonValue || value.ToJsonString() != after.ToJsonString():
                removed.Add(at + " = " + value.ToJsonString());
                break;
        }
    }

    /// <summary>Sobe a partir deste arquivo-fonte (caminho de compilação), do diretório atual ou dos binários.</summary>
    internal static string RepositoryRoot([CallerFilePath] string source = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(source) ?? string.Empty, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "BemAquiEmpresas.slnx")))
                {
                    return dir.FullName;
                }
            }
        }

        throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
