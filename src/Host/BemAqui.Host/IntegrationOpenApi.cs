using System.Text.Json.Nodes;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BemAqui.Host;

/// <summary>
/// Documento <c>integration-v1</c> (INT-03 CA 24): só as rotas de <c>/api/v1/integration</c>, com o escopo exigido em cada operação
/// (<c>x-scope</c>, lido do <c>RequirePermission</c>) e a lista de códigos de erro estáveis (<c>x-error-codes</c>). O arquivo versionado
/// <c>docs/integracoes/openapi-integration-v1.json</c> é o contrato: v1 só cresce de forma aditiva.
/// </summary>
public static class IntegrationOpenApi
{
    public const string PathPrefix = "api/v1/integration";

    public static void Configure(OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ShouldInclude = d => d.RelativePath?.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase) == true;
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "BemAqui Empresas — API de integração";
            document.Info.Version = "v1";
            document.Info.Description = "Rotas para aplicativos conectados (Conecta, API pública e MCP). Token de aplicativo (Bearer bqa_…), escopos da ADR 0053, "
                + "erros em ProblemDetails (RFC 9457) com code estável. Versão v1: só cresce de forma aditiva; quebra vai para /api/v2.";
            document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            document.Extensions["x-error-codes"] = new JsonNodeExtension(new JsonArray(IntegrationErrorCodes.All.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()));
            return Task.CompletedTask;
        });
        options.AddOperationTransformer((operation, context, _) =>
        {
            var scopes = context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>()
                .Select(a => a.Policy).OfType<string>().Where(p => p.StartsWith(ModuleEndpoints.PermissionPolicyPrefix, StringComparison.Ordinal))
                .Select(p => p[ModuleEndpoints.PermissionPolicyPrefix.Length..]).ToList();
            if (scopes.Count > 0)
            {
                operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                operation.Extensions["x-scope"] = new JsonNodeExtension(JsonValue.Create(string.Join(' ', scopes))!);
            }

            return Task.CompletedTask;
        });
    }
}
