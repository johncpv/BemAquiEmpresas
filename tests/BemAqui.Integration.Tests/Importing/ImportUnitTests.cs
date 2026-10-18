using BemAqui.Migrations.Application;
using BemAqui.Migrations.Application.Snapshot;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Importing;

/// <summary>MIG-01a: regras puras da importação (conversões CA 20, hash CA 11, CSV e máscara CA 18, de-para).</summary>
public class ImportUnitTests
{
    private static SourceRow Row(params (string Column, object? Value)[] values) =>
        new(SnapshotTables.Societe, "1", values.ToDictionary(v => v.Column, v => v.Value));

    [Fact]
    public void Quantidade_vira_decimal_com_4_casas_e_arredondamento_para_longe_do_zero()
    {
        DolibarrConvert.Quantity(0.1).Should().Be(0.1m);
        DolibarrConvert.Quantity(2.00005).Should().Be(2.0001m);
        DolibarrConvert.Quantity(-2.00005).Should().Be(-2.0001m);
        DolibarrConvert.Quantity(1.5f).Should().Be(1.5m);
        DolibarrConvert.Quantity("3,25").Should().Be(3.25m);
        DolibarrConvert.Quantity(null).Should().BeNull();
    }

    [Fact]
    public void Datas_sem_hora_ficam_como_data_e_com_hora_saem_de_Sao_Paulo_para_UTC()
    {
        DolibarrConvert.Date(new DateTime(2026, 10, 18, 23, 59, 0)).Should().Be(new DateOnly(2026, 10, 18));
        DolibarrConvert.LocalDateTime(new DateTime(2026, 10, 18, 9, 0, 0)).Should().Be(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Texto_e_normalizado_sem_perder_acento_e_sem_controle_nem_bidi()
    {
        DolibarrConvert.Text("  João\u0000 da‮ Silva  ").Should().Be("João da Silva");
        DolibarrConvert.Text("JoÃ£o AÃ§o").Should().Be("João Aço", "texto gravado duas vezes em latin1");
        DolibarrConvert.Text("SÃO PAULO").Should().Be("SÃO PAULO", "maiúscula legítima não é confundida");
        DolibarrConvert.Text(new byte[] { 0x41, 0xE7, 0x6F }).Should().Be("Aço", "bytes em latin1");
        DolibarrConvert.Text("Linha 1\nLinha 2").Should().Be("Linha 1\nLinha 2");
        DolibarrConvert.Text("   ").Should().BeNull();
    }

    [Fact]
    public void Hash_e_estavel_e_ignora_a_data_de_alteracao()
    {
        var a = Row(("nom", "Alfa"), ("rowid", 1), ("tms", new DateTime(2026, 1, 1)));
        var b = Row(("rowid", 1), ("nom", "Alfa"), ("tms", new DateTime(2026, 5, 5)));
        var c = Row(("rowid", 1), ("nom", "Alfa Ltda"));

        SourceHash.Of(a).Should().Be(SourceHash.Of(b)).And.HaveLength(64);
        SourceHash.Of(c).Should().NotBe(SourceHash.Of(a));
    }

    [Theory]
    [InlineData("=SOMA(A1)", "'=SOMA(A1)")]
    [InlineData("+55", "'+55")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tx", "'\tx")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("diz \"oi\"", "\"diz \"\"oi\"\"\"")]
    [InlineData("texto", "texto")]
    public void Celula_do_CSV_nao_vira_formula(string value, string expected) => ImportReportWriter.Cell(value).Should().Be(expected);

    [Fact]
    public void Mascara_esconde_CPF_email_e_telefone_e_deixa_o_CNPJ()
    {
        PersonalMask.Mask("siren", "529.982.247-25").Should().Be("***.982.247-**");
        PersonalMask.Mask("email", "maria@empresa.com.br").Should().Be("m***@empresa.com.br");
        PersonalMask.Mask("phone_mobile", "(31) 99876-5432").Should().Be("***5432");
        PersonalMask.Mask("phone", "31998765432").Should().Be("***5432", "celular com DDD tem 11 dígitos e não é CPF");
        PersonalMask.Mask("siren", "47.225.005/0001-86").Should().Be("47225005000186");
        PersonalMask.Mask("note_private", "Rua das Flores 12").Should().Be(PersonalMask.Hidden);
        PersonalMask.Mask("address", "ABCDEFGHIJKL12").Should().Be(PersonalMask.Hidden, "14 caracteres que não são CNPJ válido");
        PersonalMask.Payload(Row(("nom", "Alfa"), ("email", "a@b.com"))).Should().Contain("Alfa").And.Contain("a***@b.com");
    }

    [Fact]
    public void Arquivo_de_de_para_e_validado()
    {
        var ok = ImportMapping.Parse("{\"maps\":{\"units\":{\"1\":\"UN\",\"2\":\"KG\"}}}");
        ok.IsSuccess.Should().BeTrue();
        ok.Value.Get("units", "2").Should().Be("KG");
        ok.Value.Get("units", "9").Should().BeNull();

        ImportMapping.Parse("{\"mapa\":{}}").Error!.Message.Should().Contain("propriedade desconhecida");
        ImportMapping.Parse("{\"maps\":{\"units\":{\"1\":\"\"}}}").Error!.Message.Should().Contain("não vazio");
        ImportMapping.Parse("{").Error!.Message.Should().Contain("JSON inválido");
    }
}
