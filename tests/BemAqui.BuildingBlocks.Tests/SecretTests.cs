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

public class SecretJsonTests
{
    private sealed record SetSmtpPassword(string Host, Secret? Password);

    [Fact]
    public void Dto_de_escrita_recebe_o_segredo()
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<SetSmtpPassword>("""{"Host":"smtp","Password":"s3nh4"}""")!;

        dto.Password!.Reveal().Should().Be("s3nh4");
    }

    [Fact]
    public void Serializar_segredo_e_bug_e_falha()
    {
        var serializar = () => System.Text.Json.JsonSerializer.Serialize(new SetSmtpPassword("smtp", new Secret("s3nh4")));

        serializar.Should().Throw<InvalidOperationException>().WithMessage("*write-only*");
    }

    [Fact]
    public void Mascara_devolvida_num_PUT_e_recusada()
    {
        var ler = () => System.Text.Json.JsonSerializer.Deserialize<SetSmtpPassword>("""{"Host":"smtp","Password":"***"}""");

        ler.Should().Throw<System.Text.Json.JsonException>().WithMessage("*mascarado*");
    }

    [Fact]
    public void Mascara_nao_vira_segredo()
    {
        var criar = () => new Secret(Secret.Mask);

        criar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Igualdade_e_hash_nao_dependem_do_valor_exposto()
    {
        new Secret("abc").Should().Be(new Secret("abc"));
        new Secret("abc").Should().NotBe(new Secret("abd"));
        new Secret("abc").GetHashCode().Should().Be(new Secret("zzz").GetHashCode());
    }

    [Fact]
    public void ToString_de_record_com_segredo_mascara()
    {
        new SetSmtpPassword("smtp", new Secret("s3nh4")).ToString().Should().NotContain("s3nh4");
    }
}
