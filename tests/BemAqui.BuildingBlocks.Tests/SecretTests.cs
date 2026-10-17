using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

public class SecretTests
{
    [Fact]
    public void ToString_nunca_mostra_o_valor()
    {
        var secret = new Secret("s3nh4-real");

        secret.ToString().Should().Be("***");
        $"senha={secret}".Should().Be("senha=***");
        secret.Reveal().Should().Be("s3nh4-real");
    }

    [Fact]
    public void Segredo_vazio_e_recusado()
    {
        var criar = () => new Secret(string.Empty);

        criar.Should().Throw<ArgumentException>();
    }
}
