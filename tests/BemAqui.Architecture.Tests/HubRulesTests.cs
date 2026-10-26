using System.Reflection;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Host.Realtime;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PRD-02 CA 15 e 16: todo hub de negócio é mapeado com <c>RequireHubAccess</c> (módulo liberado e permissão do catálogo; o filtro
/// de endpoint do <c>RequireModule</c> não roda em hub) e não tem método de servidor (o cliente nunca escolhe grupo).
/// </summary>
public static partial class HubRules
{
    /// <summary>O hub técnico da FND-02 só prova token e host do tenant (grupo "all").</summary>
    public static readonly string[] TechnicalHubs = ["PingHub"];

    /// <summary>Hubs do principal de quiosque (PNL-01a): sem usuário, só com a sessão da TV.</summary>
    public static readonly string[] KioskHubs = ["KioskHub"];

    [GeneratedRegex(@"MapHub<(?<hub>[\w.]+)>[^;]*;", RegexOptions.Singleline)]
    private static partial Regex MapHub();

    [GeneratedRegex("""\.RequireHubAccess\("(?<module>[^"]*)"(?<perms>(?:\s*,\s*"[^"]*")*)\)""")]
    private static partial Regex HubAccessUse();

    public static IReadOnlyList<string> Violations(IEnumerable<(string File, string Content)> sources) =>
        sources.SelectMany(s => MapHub().Matches(s.Content).SelectMany<Match, string>(m =>
        {
            var hub = m.Groups["hub"].Value.Split('.')[^1];
            if (TechnicalHubs.Contains(hub))
            {
                return [];
            }

            // Hub do Painel da TV (ADR 0030 §3): não é de usuário; exige o principal de quiosque (RequireKiosk, policy kiosk).
            if (KioskHubs.Contains(hub))
            {
                return m.Value.Contains(".RequireKiosk()", StringComparison.Ordinal) && !m.Value.Contains("RequireHubAccess", StringComparison.Ordinal)
                    ? []
                    : new[] { $"{s.File}: MapHub<{hub}> do quiosque sem RequireKiosk" };
            }

            if (m.Value.Contains(".RequireKiosk()", StringComparison.Ordinal))
            {
                return new[] { $"{s.File}: MapHub<{hub}> de usuário com RequireKiosk" };
            }

            var access = HubAccessUse().Match(m.Value);
            if (!access.Success)
            {
                return new[] { $"{s.File}: MapHub<{hub}> sem RequireHubAccess" };
            }

            var problems = new List<string>();
            if (ModuleCatalog.Find(access.Groups["module"].Value) is null)
            {
                problems.Add($"{s.File}: MapHub<{hub}> com módulo fora do ModuleCatalog");
            }

            problems.AddRange(Regex.Matches(access.Groups["perms"].Value, "\"([^\"]*)\"").Select(p => p.Groups[1].Value)
                .Where(p => !ModuleCatalog.IsKnownPermission(p)).Select(p => $"{s.File}: MapHub<{hub}> com permissão {p} fora do ModuleCatalog"));
            return problems;
        })).ToList();

    /// <summary>Métodos públicos declarados no hub que o cliente poderia chamar (as sobrescritas do ciclo de vida não contam).</summary>
    public static IReadOnlyList<string> ServerMethods(Type hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        return hub.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetBaseDefinition().DeclaringType == hub)
            .Select(m => hub.Name + "." + m.Name)
            .ToList();
    }
}

public class HubRulesTests
{
    [Fact]
    public void Todo_hub_de_negocio_exige_modulo_e_permissao() =>
        HubRules.Violations(PermissionRules.SolutionSources()).Should().BeEmpty();

    [Theory]
    [InlineData("""app.MapHub<ProductionHub>(ProductionHub.Path);""")]
    [InlineData("""app.MapHub<ProductionHub>(ProductionHub.Path).RequireHubAccess("crm", "production.view");""")]
    [InlineData("""app.MapHub<ProductionHub>(ProductionHub.Path).RequireHubAccess("production", "production.publish");""")]
    public void Hub_sem_acesso_ou_fora_do_catalogo_viola(string source) =>
        HubRules.Violations([("X.cs", source)]).Should().NotBeEmpty();

    [Fact]
    public void Hub_com_acesso_do_catalogo_e_aceito() =>
        HubRules.Violations([("X.cs", """app.MapHub<ProductionHub>(ProductionHub.Path).RequireHubAccess("production", "production.view", "shopfloor.view");""")])
            .Should().BeEmpty();

    [Theory]
    [InlineData("""app.MapHub<KioskHub>(KioskHub.Path);""")]
    [InlineData("""app.MapHub<KioskHub>(KioskHub.Path).RequireHubAccess("kiosk", "kiosk.view");""")]
    [InlineData("""app.MapHub<ProductionHub>(ProductionHub.Path).RequireHubAccess("production", "production.view").RequireKiosk();""")]
    public void Hub_do_quiosque_so_com_RequireKiosk_e_hub_de_usuario_nunca_com_ele(string source) =>
        HubRules.Violations([("X.cs", source)]).Should().NotBeEmpty();

    [Fact]
    public void Hub_do_quiosque_com_RequireKiosk_e_aceito() =>
        HubRules.Violations([("X.cs", """app.MapHub<KioskHub>(KioskHub.Path).RequireKiosk();""")]).Should().BeEmpty();

    [Fact]
    public void Hubs_de_negocio_nao_tem_metodo_de_servidor()
    {
        var hubs = typeof(ProductionHub).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(Hub).IsAssignableFrom(t) && !HubRules.TechnicalHubs.Contains(t.Name))
            .ToList();

        hubs.Should().Contain(typeof(ProductionHub)).And.Contain(typeof(KioskHub));
        hubs.SelectMany(HubRules.ServerMethods).Should().BeEmpty("o cliente só recebe; entrar em grupo é decisão do servidor");
    }
}
