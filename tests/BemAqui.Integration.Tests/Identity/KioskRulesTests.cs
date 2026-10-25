using System.Net;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Identity.Application.Kiosk;
using BemAqui.Identity.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Identity;

/// <summary>
/// Unidade (PNL-01a, plano de testes): geração e hash do token (CA 3), redes permitidas em CIDR (CA 16), escopo de visões, rotação e
/// nome (CA 3 e 10), vencimento de 30 dias sem uso e 180 absolutos (CA 7), pareamento de uso único (CA 6) e o principal de quiosque.
/// </summary>
public class KioskRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Token_tem_256_bits_e_no_banco_ficam_so_o_SHA_256_e_o_prefixo()
    {
        var token = KioskRules.NewToken();

        Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(token.Value).Should().HaveCount(32);
        token.Hash.Should().Be(OpaqueToken.Hash(token.Value)).And.HaveLength(64).And.NotContain(token.Value);
        token.Prefix.Should().Be(token.Value[..6]);
        KioskRules.NewToken().Value.Should().NotBe(token.Value);
        KioskRules.IsPlausible(token.Value).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curto")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void Token_fora_do_formato_nem_e_procurado(string? value) => KioskRules.IsPlausible(value).Should().BeFalse();

    [Fact]
    public void Link_leva_o_token_no_fragmento_nunca_no_caminho()
    {
        var link = KioskRules.Link("app.konebras.com.br", "abc123");

        link.Should().Be("https://app.konebras.com.br/tv#abc123");
        new Uri(link).AbsolutePath.Should().Be("/tv", "o fragmento não vai ao servidor nem ao log do nginx");
    }

    [Fact]
    public void Redes_em_CIDR_ou_IP_unico_ficam_na_forma_canonica()
    {
        var (networks, problem) = KioskRules.NormalizeNetworks(["192.168.10.7/24", " 10.0.0.5 ", "192.168.10.0/24", "", "2001:db8::/32"]);

        problem.Should().BeNull();
        networks.Should().Equal("192.168.10.0/24", "10.0.0.5/32", "2001:db8::/32");
    }

    [Theory]
    [InlineData("192.168.10.0/33")]
    [InlineData("fabrica")]
    [InlineData("0.0.0.0/0")]
    public void Rede_invalida_ou_qualquer_rede_e_recusada(string network)
    {
        var (_, problem) = KioskRules.NormalizeNetworks([network]);

        problem.Should().Contain(network);
    }

    [Fact]
    public void No_maximo_5_redes()
    {
        var (_, problem) = KioskRules.NormalizeNetworks(["10.0.0.1", "10.0.0.2", "10.0.0.3", "10.0.0.4", "10.0.0.5", "10.0.0.6"]);

        problem.Should().Be("Informe no máximo 5 redes.");
    }

    [Theory]
    [InlineData("192.168.10.40", true)]
    [InlineData("192.168.11.40", false)]
    [InlineData("::ffff:192.168.10.40", true)]
    public void IP_real_dentro_de_alguma_rede_permitida(string ip, bool allowed) =>
        KioskRules.IsAllowed(IPAddress.Parse(ip), ["192.168.10.0/24"]).Should().Be(allowed);

    [Fact]
    public void Sem_redes_qualquer_IP_passa_e_sem_IP_nada_passa_quando_ha_redes()
    {
        KioskRules.IsAllowed(null, []).Should().BeTrue();
        KioskRules.IsAllowed(IPAddress.Parse("203.0.113.9"), []).Should().BeTrue();
        KioskRules.IsAllowed(null, ["192.168.10.0/24"]).Should().BeFalse();
    }

    [Fact]
    public void Visoes_vazias_sao_todas_na_ordem_do_painel_e_desconhecida_e_erro()
    {
        KioskRules.NormalizeViews(null).Views.Should().Equal(KioskViews.All);
        KioskRules.NormalizeViews([KioskViews.Late, KioskViews.Board, KioskViews.Late]).Views.Should().Equal(KioskViews.Board, KioskViews.Late);
        KioskRules.NormalizeViews(["financeiro"]).Problem.Should().NotBeNull();
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(20, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void Rotacao_de_10_a_120_segundos(int seconds, bool valid) => (KioskRules.RotationProblem(seconds) is null).Should().Be(valid);

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("TV da Montagem", true)]
    public void Nome_obrigatorio_ate_60_letras(string name, bool valid) => (KioskRules.NameProblem(name) is null).Should().Be(valid);

    [Fact]
    public void Nome_de_61_letras_e_recusado() => KioskRules.NameProblem(new string('a', 61)).Should().NotBeNull();

    [Fact]
    public void Sessao_vence_com_30_dias_sem_uso()
    {
        var session = KioskSession.Open(Guid.NewGuid(), "hash", "10.0.0.5", Now);

        session.ExpiresAt.Should().Be(Now.AddDays(30));
        session.AbsoluteExpiresAt.Should().Be(Now.AddDays(180));
        session.IsActive(Now.AddDays(29)).Should().BeTrue();
        session.IsActive(Now.AddDays(30)).Should().BeFalse("30 dias sem uso");
    }

    [Fact]
    public void Uso_renova_os_30_dias_no_maximo_1_vez_por_minuto_e_nunca_passa_dos_180()
    {
        var session = KioskSession.Open(Guid.NewGuid(), "hash", null, Now);

        session.Touch(Now.AddSeconds(30)).Should().BeFalse("no máximo 1 vez por minuto (CA 19)");
        session.Touch(Now.AddMinutes(1)).Should().BeTrue();
        session.ExpiresAt.Should().Be(Now.AddMinutes(1).AddDays(30));

        for (var day = 20; day <= 170; day += 20)
        {
            session.Touch(Now.AddDays(day)).Should().BeTrue();
        }

        session.ExpiresAt.Should().Be(Now.AddDays(180), "o prazo absoluto não é renovado");
        session.IsActive(Now.AddDays(179)).Should().BeTrue();
        session.IsActive(Now.AddDays(180)).Should().BeFalse("180 dias desde o pareamento, mesmo em uso");
    }

    [Fact]
    public void Sessao_revogada_nao_vale()
    {
        var session = KioskSession.Open(Guid.NewGuid(), "hash", null, Now);

        session.Revoke(Now.AddMinutes(5));

        session.IsActive(Now.AddMinutes(6)).Should().BeFalse();
    }

    [Fact]
    public void Tela_aguarda_conexao_fica_ativa_ao_parear_e_novo_link_volta_a_aguardar()
    {
        var token = KioskRules.NewToken();
        var screen = KioskScreen.Create("TV da Montagem", null, KioskViews.All, 20, [], token.Hash, token.Prefix, Guid.NewGuid(), Now);

        screen.Status.Should().Be(KioskScreenStatus.AwaitingConnection);
        screen.Consume(Now.AddMinutes(1));
        screen.Status.Should().Be(KioskScreenStatus.Active);
        screen.ConsumedAt.Should().NotBeNull("o token virou sessão e não abre outra (CA 6)");

        var next = KioskRules.NewToken();
        screen.ReplaceToken(next.Hash, next.Prefix);
        screen.Status.Should().Be(KioskScreenStatus.AwaitingConnection);
        screen.TokenHash.Should().Be(next.Hash);

        var by = Guid.NewGuid();
        screen.Revoke(by, Now.AddMinutes(2));
        screen.Revoke(Guid.NewGuid(), Now.AddMinutes(3));
        screen.Status.Should().Be(KioskScreenStatus.Revoked);
        screen.RevokedBy.Should().Be(by, "a primeira revogação vale");
    }

    [Fact]
    public void Principal_de_quiosque_tem_so_tenant_tela_sessao_e_escopo()
    {
        var tenantId = Guid.NewGuid();
        var info = new KioskSessionInfo(Guid.NewGuid(), Guid.NewGuid(), "TV da Montagem", "Linha 1", [KioskViews.Board], 30);

        var principal = KioskPrincipal.Create(tenantId, info);

        principal.Identity!.AuthenticationType.Should().Be(KioskClaims.Scheme);
        principal.FindFirst("sub").Should().BeNull("não é usuário");
        principal.Claims.Select(c => c.Type).Distinct().Should().BeEquivalentTo(
            [KioskClaims.Tenant, KioskClaims.KioskId, KioskClaims.SessionId, KioskClaims.Name, KioskClaims.Rotation, KioskClaims.Line, KioskClaims.View]);
        KioskPrincipal.From(principal).Should().BeEquivalentTo(info);
    }
}
