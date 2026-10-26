using System.Reflection;
using System.Text.RegularExpressions;
using BemAqui.Identity.Application.Kiosk;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PNL-01a CA 8, 9 e 11 (ADR 0030 §1 e §2): o principal de quiosque só existe no GET do painel e no hub do quiosque; a troca do token
/// é a única rota que escreve em <c>/api/v1/kiosk</c>; o que a TV recebe não tem dado pessoal nem dinheiro; e o log do nginx não
/// grava o token, o cookie nem o corpo.
/// </summary>
public static partial class KioskRules
{
    public const string EndpointsFile = "KioskScreenEndpoints.cs";

    [GeneratedRegex("""\.RequireKiosk\(\)""")]
    private static partial Regex Use();

    [GeneratedRegex("""kiosk\.Map(?<method>\w+)\("(?<path>[^"]*)"[\s\S]*?\.WithName\("(?<name>[^"]+)"\)""")]
    private static partial Regex KioskRoute();

    /// <summary>Usos de <c>.RequireKiosk()</c> fora de <c>kiosk.MapGet(...)</c> ou do <c>MapHub&lt;KioskHub&gt;</c>.</summary>
    public static IReadOnlyList<string> Violations(IEnumerable<(string File, string Content)> sources) =>
        sources.SelectMany(s => Use().Matches(s.Content).Select(m =>
        {
            var start = Math.Max(s.Content.LastIndexOf(';', m.Index), s.Content.LastIndexOf('{', m.Index)) + 1;
            var statement = s.Content[start..m.Index].Trim();
            var allowed = (statement.StartsWith("kiosk.MapGet(", StringComparison.Ordinal) && s.File.EndsWith(EndpointsFile, StringComparison.Ordinal))
                || statement.Contains("MapHub<KioskHub>", StringComparison.Ordinal);
            return allowed ? null : $"{s.File}: RequireKiosk() fora do GET do painel ou do KioskHub";
        }).OfType<string>()).ToList();

    /// <summary>Rotas do grupo <c>/kiosk</c>: GET com <c>RequireKiosk()</c>, ou a troca do token (POST anônimo com rate limit).</summary>
    public static IReadOnlyList<string> RouteViolations(string content) =>
        KioskRoute().Matches(content).Select(m =>
        {
            var method = m.Groups["method"].Value;
            var body = m.Value;
            return method switch
            {
                "Get" when body.Contains(".RequireKiosk()", StringComparison.Ordinal) && !body.Contains("AllowAnonymous", StringComparison.Ordinal) => null,
                "Post" when m.Groups["path"].Value == "/session" && body.Contains(".AllowAnonymous()", StringComparison.Ordinal)
                    && body.Contains(".RequireRateLimiting(KioskEndpoints.SessionRateLimitPolicy)", StringComparison.Ordinal) => null,
                _ => $"kiosk.Map{method}(\"{m.Groups["path"].Value}\") ({m.Groups["name"].Value}) fora da regra do painel",
            };
        }).OfType<string>().ToList();

    public static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Solution.RootDirectory, .. parts]));
}

public class KioskRulesTests
{
    [Fact]
    public void Principal_de_quiosque_so_no_GET_do_painel_e_no_hub_do_quiosque() =>
        KioskRules.Violations(PermissionRules.SolutionSources()).Should().BeEmpty();

    [Fact]
    public void Regra_acusa_RequireKiosk_em_endpoint_de_usuario()
    {
        const string source = """
            api.MapGet("/finance/payables", () => 1).RequireKiosk().WithName("ListPayables");
            kiosk.MapPost("/screen", () => 1).RequireKiosk().WithName("X");
            app.MapHub<KioskHub>(KioskHub.Path).RequireKiosk();
            """;

        KioskRules.Violations([("src/KioskScreenEndpoints.cs", source)]).Should().HaveCount(2);
    }

    [Fact]
    public void Grupo_do_quiosque_so_tem_GET_e_a_troca_do_token()
    {
        var content = KioskRules.Read("src", "Modules", "Identity", "BemAqui.Identity.Endpoints", KioskRules.EndpointsFile);

        Regex.Matches(content, """kiosk\.Map\w+\(""").Should().HaveCountGreaterThanOrEqualTo(2);
        KioskRules.RouteViolations(content).Should().BeEmpty();
        KioskRules.RouteViolations("""kiosk.MapDelete("/screen", () => 1).RequireKiosk().WithName("X");""").Should().NotBeEmpty();
    }

    /// <summary>Allowlist do que a TV recebe nesta story (o DTO do painel é da PNL-01b): sem cliente, operador, custo nem dinheiro.</summary>
    [Theory]
    [InlineData(typeof(KioskScreenContext), new[] { "Name", "Line", "Views", "RotationSeconds", "CompanyName", "ServerNow" })]
    [InlineData(typeof(KioskSessionOpened), new[] { "ScreenName" })]
    public void O_que_a_TV_recebe_e_so_a_allowlist(Type dto, string[] allowed) =>
        dto.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Should().BeEquivalentTo(allowed);

    [Fact]
    public void Log_do_nginx_nao_grava_query_corpo_cookie_nem_Authorization()
    {
        var conf = KioskRules.Read("deploy", "nginx", "bemaqui-empresas-homolog.conf");
        var format = Regex.Match(conf, """log_format bqe_masked[^;]*;""").Value;

        format.Should().NotBeEmpty().And.Contain("$uri");
        foreach (var leak in new[] { "$request_uri", "$args", "$query_string", "$request_body", "$http_cookie", "$cookie_", "$http_authorization", "$request " })
        {
            format.Should().NotContain(leak, "o token da TV fica no fragmento e no corpo; o cookie nunca vai para o log (CA 8)");
        }

        Regex.Matches(conf, """access_log\s+[^;]*;""").Select(m => m.Value).Should().OnlyContain(l => l.Contains("bqe_masked", StringComparison.Ordinal) || l.Contains(" off", StringComparison.Ordinal));
    }

    [Fact]
    public void Pagina_tv_sai_sem_referer_e_sem_cache_no_nginx_do_front()
    {
        var conf = KioskRules.Read("deploy", "web", "nginx.conf");
        var tv = Regex.Match(conf, """location ~ \^/tv\(/\|\$\) \{[^}]*\}""").Value;

        tv.Should().NotBeEmpty("a página /tv tem a própria location");
        tv.Should().Contain("Referrer-Policy \"no-referrer\"").And.Contain("Cache-Control \"no-store\"").And.Contain("try_files $uri /index.html");
    }
}
