using BemAqui.BuildingBlocks.Help;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>FND-14 CA 1: toda ajuda em Ajuda/ tem cabeçalho e seções obrigatórias (o CI falha antes do deploy).</summary>
public class HelpFilesTests
{
    [Fact]
    public void Todas_as_ajudas_sao_validas_e_as_chaves_unicas()
    {
        var files = Directory.GetFiles(Path.Combine(Solution.RootDirectory, "Ajuda"), "*.md");
        files.Should().NotBeEmpty();
        var documents = files.Select(f => HelpCatalog.Parse(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f))).ToList();
        documents.GroupBy(d => (d.Route, d.Tab)).Where(g => g.Count() > 1).Select(g => g.Key).Should().BeEmpty("cada rota + aba tem uma ajuda");
        documents.GroupBy(d => d.Route).Where(g => g.Count(d => d.IsDefault) > 1).Select(g => g.Key).Should().BeEmpty("no máximo uma ajuda padrão por rota");
    }

    [Theory]
    [InlineData("---\ntitulo: X\nrota: /x\naba:\npadrao: true\nmodulo:\n---\n## Para que serve\nA\n## Como usar\nB\n")]
    [InlineData("---\nrota: /x\naba:\npadrao: true\nmodulo:\n---\n## Para que serve\nA\n## Como usar\nB\n## Perguntas frequentes\nC\n")]
    public void Ajuda_sem_secao_ou_cabecalho_obrigatorio_e_recusada(string text)
    {
        var act = () => HelpCatalog.Parse("x", text);
        act.Should().Throw<InvalidOperationException>();
    }
}
