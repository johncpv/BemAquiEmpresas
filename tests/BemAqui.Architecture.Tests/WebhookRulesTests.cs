using BemAqui.BuildingBlocks.Webhooks;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// FND-16 CA 1 e 4 (ADR 0031 §1): o handler de cada provedor fica no módulo dono (nunca no Host nem nos BuildingBlocks); só a
/// rota <c>/webhooks/*</c> resolve tenant pelo <c>tenantKey</c>; a rota não entra no OpenAPI do front.
/// </summary>
public class WebhookRulesTests
{
    public static IReadOnlyList<string> HandlersOutsideModules(IEnumerable<Type> types) =>
        types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IWebhookHandler).IsAssignableFrom(t))
            .Where(t => Solution.Classify(t.Assembly.GetName().Name ?? string.Empty) is not { Module: not null, Layer: "Application" or "Infrastructure" })
            .Select(t => $"{t.FullName} ({t.Assembly.GetName().Name}): handler de webhook fora do Application/Infrastructure do módulo dono")
            .ToList();

    private sealed class LooseHandler : IWebhookHandler
    {
        public Task<WebhookHandling> HandleAsync(WebhookEventContext context, CancellationToken cancellationToken) => Task.FromResult(WebhookHandling.Processed());
    }

    [Fact]
    public void Handler_de_webhook_so_no_modulo_dono() =>
        HandlersOutsideModules(Solution.Assemblies.SelectMany(a => a.GetTypes())).Should().BeEmpty();

    [Fact]
    public void Regra_acusa_handler_fora_de_modulo() =>
        HandlersOutsideModules([typeof(LooseHandler)]).Should().ContainSingle();

    private static IEnumerable<(string File, string Content)> Sources() =>
        Directory.GetFiles(Path.Combine(Solution.RootDirectory, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(Solution.RootDirectory, f).Replace('\\', '/'), File.ReadAllText(f)));

    [Fact]
    public void So_a_rota_de_webhook_resolve_tenant_pelo_tenantKey()
    {
        var users = Sources().Where(s => s.Content.Contains("IWebhookTenantKeys>().ResolveAsync", StringComparison.Ordinal) || s.Content.Contains("tenantKeys.ResolveAsync", StringComparison.Ordinal))
            .Select(s => s.File).ToList();

        users.Should().Equal("src/BuildingBlocks/BemAqui.BuildingBlocks/Webhooks/WebhookEndpoints.cs");
    }

    [Fact]
    public void Rota_de_webhook_e_mapeada_uma_vez_no_Host_e_fora_do_OpenAPI()
    {
        Sources().Count(s => s.Content.Contains(".MapWebhookReceiver()", StringComparison.Ordinal)).Should().Be(1);
        File.ReadAllText(Path.Combine(Solution.RootDirectory, "src", "Web", "openapi.json")).Should().NotContain("\"/webhooks/");
    }
}
