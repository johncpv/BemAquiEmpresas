using BemAqui.BuildingBlocks.Logging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>CAD-01a CA 1 (BrazilianDocuments, os mesmos casos do front), CA 25 (marcadores de log) e CA 26 (registro).</summary>
public class BrazilianDocumentsTests
{
    [Theory]
    [InlineData("123.456.789-09", true)]
    [InlineData("12345678909", true)]
    [InlineData("123.456.789-00", false)]
    [InlineData("111.111.111-11", false)]
    [InlineData("1234567890", false)]
    public void Cpf(string value, bool valid) => BrazilianDocuments.IsValidCpf(value).Should().Be(valid);

    [Theory]
    [InlineData("47.225.005/0001-86", true)]
    [InlineData("47225005000186", true)]
    [InlineData("47.225.005/0001-87", false)]
    [InlineData("12.ABC.345/01DE-35", true)]
    [InlineData("12abc34501de35", true)]
    [InlineData("12.ABC.345/01DE-36", false)]
    [InlineData("00.000.000/0000-00", false)]
    [InlineData("12.ABC.345/01DE-3X", false)]
    public void Cnpj_numerico_e_alfanumerico(string value, bool valid) => BrazilianDocuments.IsValidCnpj(value).Should().Be(valid);

    [Fact]
    public void Normaliza_infere_o_tipo_formata_e_mascara_o_cpf()
    {
        BrazilianDocuments.Normalize(" 12.abc.345/01de-35 ").Should().Be("12ABC34501DE35");
        BrazilianDocuments.KindOf("123.456.789-09").Should().Be(DocumentKind.Cpf);
        BrazilianDocuments.KindOf("12ABC34501DE35").Should().Be(DocumentKind.Cnpj);
        BrazilianDocuments.KindOf("123").Should().BeNull();
        BrazilianDocuments.Format("47225005000186").Should().Be("47.225.005/0001-86");
        BrazilianDocuments.Format("12345678909").Should().Be("123.456.789-09");
        BrazilianDocuments.MaskCpf("12345678909").Should().Be("***.456.789-**");
    }

    [Theory]
    [InlineData("Cpf", true)]
    [InlineData("customerDocument", true)]
    [InlineData("ContactEmail", true)]
    [InlineData("phone_number", true)]
    [InlineData("employeeRg", true)]
    [InlineData("pis", true)]
    [InlineData("PixKey", true)]
    [InlineData("Arguments", false)]
    [InlineData("Target", false)]
    [InlineData("RequestPath", false)]
    [InlineData("TenantId", false)]
    public void Marcadores_de_dado_pessoal_no_log(string name, bool sensitive) => SensitivePropertyMask.IsSensitive(name).Should().Be(sensitive);

    [Fact]
    public void Registro_de_auditoria_recusa_nome_curto_repetido_entre_modulos()
    {
        var collision = () => new AuditableEntityRegistry([new AuditableEntity("Address", "parties", "BemAqui.Parties.Domain.Address"), new AuditableEntity("Address", "hr", "BemAqui.HR.Domain.Address")]);
        collision.Should().Throw<InvalidOperationException>().WithMessage("*nome repetido*");

        var same = new AuditableEntityRegistry([new AuditableEntity("Party", "parties", "BemAqui.Parties.Domain.Party"), new AuditableEntity("Party", "parties", "BemAqui.Parties.Domain.Party")]);
        same.OwnerOf("Party").Should().Be("parties");
    }
}
