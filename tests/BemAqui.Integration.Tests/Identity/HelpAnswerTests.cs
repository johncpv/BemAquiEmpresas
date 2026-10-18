using BemAqui.BuildingBlocks.Help;
using BemAqui.Identity.Application.Ai;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Identity;

/// <summary>FND-15 (unidade): ranking com prioridade da tela atual, validação de citação, prompt e aviso de dado da empresa.</summary>
public class HelpAnswerTests
{
    private static HelpDocument Doc(string key, string title, string body) =>
        new(key, title, "/app/" + key, null, true, null, [new HelpSection("Para que serve", body), new HelpSection("Como usar", "Clique em Salvar.")]);

    private static readonly HelpDocument[] Docs =
    [
        Doc("grupos", "Grupos de acesso", "Crie grupos e marque as permissões de cada módulo."),
        Doc("usuarios", "Usuários", "Convide usuários e coloque cada um em um ou mais grupos."),
        Doc("seguranca", "Segurança", "Exija a verificação em duas etapas para todos."),
    ];

    [Fact]
    public void Busca_ignora_acento_e_palavras_vazias_e_prioriza_a_tela_atual()
    {
        HelpSearch.Terms("Como é que eu faço a VERIFICAÇÃO?").Should().Equal("verificacao");
        HelpSearch.Find(Docs, "verificação em duas etapas", null)[0].Document.Key.Should().Be("seguranca");

        HelpSearch.Find(Docs, "grupos", null)[0].Document.Key.Should().Be("grupos");
        HelpSearch.Find(Docs, "grupos", "usuarios")[0].Document.Key.Should().Be("usuarios", "a tela atual ganha +2 quando também tem o termo");
        HelpSearch.Find(Docs, "de para com", null).Should().BeEmpty();
        HelpSearch.Find(Docs, "clique", null).Should().HaveCountLessThanOrEqualTo(HelpSearch.MaxSections);
    }

    [Theory]
    [InlineData("""{"resposta":"Use o botão [a2].","fontes":["a1"],"naoEncontrou":false}""", "Use o botão.", new[] { 0, 1 })]
    [InlineData("""Claro! {"resposta":"Use o botão.","fontes":["a1","a1"]}""", "Use o botão.", new[] { 0 })]
    public void Citacao_valida_e_aceita_e_o_marcador_sai_do_texto(string raw, string text, int[] cited)
    {
        var answer = HelpPrompt.Validate(raw, 2);
        answer.Should().NotBeNull();
        answer!.Text.Should().Be(text);
        answer.Cited.Should().Equal(cited);
    }

    [Theory]
    [InlineData("""{"resposta":"Use o botão.","fontes":[]}""")]
    [InlineData("""{"resposta":"Use o botão.","fontes":["a3"]}""")]
    [InlineData("""{"resposta":"Use o botão.","fontes":["b1"]}""")]
    [InlineData("""{"resposta":"Use o botão [a0].","fontes":["a1"]}""")]
    [InlineData("""{"resposta":"Não sei.","fontes":["a1"],"naoEncontrou":true}""")]
    [InlineData("""{"resposta":"","fontes":["a1"]}""")]
    [InlineData("sem json")]
    [InlineData("{ quebrado")]
    public void Sem_citacao_ou_com_citacao_fora_dos_trechos_e_descartada(string raw) =>
        HelpPrompt.Validate(raw, 2).Should().BeNull();

    [Fact]
    public void Prompt_tem_so_a_pergunta_e_os_trechos_numerados()
    {
        var hits = HelpSearch.Find(Docs, "grupos", null);
        var prompt = HelpPrompt.User("Como crio grupos?", hits);

        prompt.Should().StartWith("Pergunta: Como crio grupos?").And.Contain("[a1] Tela \"Grupos de acesso\" — Para que serve");
        prompt.Should().Contain("[a" + hits.Count + "]").And.NotContain("[a" + (hits.Count + 1) + "]");
        HelpPrompt.System.Should().Contain("SOMENTE os trechos").And.Contain("naoEncontrou");
    }

    [Theory]
    [InlineData("O cliente 123.456.789-09 não aparece", true)]
    [InlineData("Boleto do CNPJ 47.225.005/0001-86", true)]
    [InlineData("Mandar para joao@empresa.com.br", true)]
    [InlineData("Ligar para (11) 98765-4321", true)]
    [InlineData("Título de R$ 1.500,00 vencido", true)]
    [InlineData("Como lançar uma conta a pagar em 2026?", false)]
    [InlineData("Como criar um grupo com 3 permissões?", false)]
    public void Aviso_de_dado_da_empresa(string question, bool expected) =>
        BusinessDataGuard.LooksLikeBusinessData(question).Should().Be(expected);
}
