namespace BemAqui.Integration.Tests;

/// <summary>
/// Marca os testes de carga (dezenas de milhares de linhas). O CI roda todos; o <c>scripts/test-docker.sh</c> pula estes,
/// a menos que <c>BQ_CARGA=1</c>.
/// </summary>
public static class LoadTest
{
    public const string Trait = "Categoria";

    public const string Value = "Carga";
}
