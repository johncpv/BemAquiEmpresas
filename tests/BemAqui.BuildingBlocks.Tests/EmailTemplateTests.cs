using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Tenancy;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>FND-08 CA 4, 6, 7 e 8: templates em sandbox, valores escapados, PT-BR com HTML e texto, link com host primário.</summary>
public class EmailTemplateTests
{
    private static readonly EmailTemplateRenderer Renderer = new();

    private static Dictionary<string, object?> Model(string name = "Ana") => new()
    {
        ["tenant_name"] = "Konebras Elevadores",
        ["recipient_name"] = name,
        ["inviter_name"] = "João",
        ["link"] = "https://app.konebras.com.br/convite/abc",
        ["expires_at"] = new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero),
    };

    [Theory]
    [InlineData("invite", "Convite para acessar Konebras Elevadores", "Aceitar convite")]
    [InlineData("password-reset", "Redefinição de senha", "Redefinir senha")]
    public void Templates_padrao_em_portugues_com_html_texto_e_link_em_destaque(string template, string subject, string action)
    {
        var email = Renderer.Render(template, Model());

        email.Subject.Should().StartWith(subject);
        email.Html.Should().Contain("lang=\"pt-BR\"").And.Contain(action).And.Contain("href=\"https://app.konebras.com.br/convite/abc\"");
        email.Text.Should().Contain("https://app.konebras.com.br/convite/abc").And.NotContain("<");
        email.Html.Should().Contain("20/10/2026 12:00", "data exibida em America/Sao_Paulo");
    }

    [Fact]
    public void Valor_com_HTML_sai_escapado()
    {
        var email = Renderer.Render("invite", Model("<script>alert(1)</script>"));

        email.Html.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
    }

    [Fact]
    public void Template_nao_carrega_arquivo_por_include() =>
        FluentActions.Invoking(() => EmailTemplateRenderer.RenderUntrusted("{% include 'appsettings' %}", Model(), html: true))
            .Should().Throw<Exception>();

    [Fact]
    public void Laco_sem_fim_e_interrompido_pelo_limite_de_passos() =>
        FluentActions.Invoking(() => EmailTemplateRenderer.RenderUntrusted("{% for i in (1..1000000) %}x{% endfor %}", Model(), html: false))
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Template_so_enxerga_o_modelo_explicito() =>
        EmailTemplateRenderer.RenderUntrusted("[{{ link.GetType }}][{{ System.Environment }}]", Model(), html: false).Should().Be("[][]");

    [Fact]
    public void Template_desconhecido_e_recusado() =>
        FluentActions.Invoking(() => Renderer.Render("nao-existe", Model())).Should().Throw<InvalidOperationException>();

    [Fact]
    public void Link_usa_o_host_primario_do_tenant()
    {
        var holder = new TenantContextHolder();
        holder.Set(new ResolvedTenant(new TenantInfo(Guid.CreateVersion7(), "konebras", TenantStatus.Active), "WSP", "Konebras", "app.konebras.com.br", "cifrado"));

        new TenantLinks(holder).Absolute("/convite/xyz").Should().Be("https://app.konebras.com.br/convite/xyz");
        FluentActions.Invoking(() => new TenantLinks(new TenantContextHolder()).Absolute("/x")).Should().Throw<InvalidOperationException>();
    }
}
