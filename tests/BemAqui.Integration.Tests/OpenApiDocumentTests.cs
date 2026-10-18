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
