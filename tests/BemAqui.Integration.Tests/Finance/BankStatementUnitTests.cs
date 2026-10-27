using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.Finance.Application.Statements;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Finance.Infrastructure.Inter;
using FluentAssertions;
using static BemAqui.Integration.Tests.Finance.OfxSamples;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>BNK-03a, unidade: parser OFX 1.x e 2.x, sem FITID, entidade externa, limites e tempo (CA 4, 5), conta do arquivo, máscara da contraparte (CA 8) e o extrato do Inter.</summary>
public class BankStatementUnitTests
{
    private static readonly DateOnly Day = new(2026, 10, 20);

    private static Tx[] Three() =>
    [
        new("F1", Day, -12.90m, "TARIFA PACOTE SERVICOS", "DEBIT"),
        new("F2", Day.AddDays(1), 1500m, "PIX RECEBIDO JOAO", "CREDIT", "JOAO DA SILVA"),
        new("F3", Day.AddDays(2), -300.55m, "PAGTO BOLETO FORNECEDOR", "DEBIT"),
    ];

    [Fact]
    public void OFX_1x_SGML_le_conta_periodo_lancamentos_e_saldo()
    {
        var result = OfxParser.Parse(Sgml("0341", "12345-6", Three(), ledger: 987.65m));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        var s = result.Value;
        s.BankId.Should().Be("0341");
        s.AccountId.Should().Be("12345-6");
        s.Start.Should().Be(Day);
        s.End.Should().Be(Day.AddDays(2));
        s.Transactions.Select(t => t.Amount).Should().Equal(-12.90m, 1500m, -300.55m);
        s.Transactions.Select(t => t.FitId).Should().Equal("F1", "F2", "F3");
        s.Transactions[1].Name.Should().Be("JOAO DA SILVA");
        s.LedgerBalance.Should().Be(987.65m);
        StatementKinds.FromOfx(s.Transactions[0].TransactionType, s.Transactions[0].Memo, null).Should().Be(StatementLineKind.Fee);
        StatementKinds.FromOfx(s.Transactions[1].TransactionType, s.Transactions[1].Memo, null).Should().Be(StatementLineKind.Pix);
        StatementKinds.FromOfx(s.Transactions[2].TransactionType, s.Transactions[2].Memo, null).Should().Be(StatementLineKind.Boleto);
    }

    [Fact]
    public void OFX_2x_XML_le_os_mesmos_dados()
    {
        var result = OfxParser.Parse(Xml("077", "1234567-8", Three()));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Transactions.Should().HaveCount(3);
        result.Value.AccountId.Should().Be("1234567-8");
        result.Value.LedgerBalance.Should().Be(100m);
    }

    [Fact]
    public void Entidade_externa_e_DTD_sao_recusadas()
    {
        var xxe = Xml("077", "1", Three(), "<!DOCTYPE OFX [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>\n");
        OfxParser.Parse(xxe).Error!.Code.Should().Be("ofx_dtd");
        var sgml = Sgml("341", "1", Three(), extraHeader: "<!ENTITY x SYSTEM \"file:///etc/passwd\">\r\n");
        OfxParser.Parse(sgml).Error!.Code.Should().Be("ofx_dtd");
    }

    [Fact]
    public void Limites_de_tamanho_de_lancamentos_e_de_tempo()
    {
        var content = Sgml("341", "1", Three());
        OfxParser.Parse(content, new OfxLimits(content.Length - 1, 100, TimeSpan.FromSeconds(10))).Error!.Code.Should().Be("ofx_size");
        OfxParser.Parse(content, new OfxLimits(1 << 20, 2, TimeSpan.FromSeconds(10))).Error!.Message.Should().Contain("mais de 2 lançamentos");
        OfxParser.Parse(content, new OfxLimits(1 << 20, 100, TimeSpan.Zero)).Error!.Code.Should().Be("ofx_timeout");
        OfxParser.Parse("não é ofx"u8.ToArray()).Error!.Code.Should().Be("ofx_invalid");
    }

    [Fact]
    public void Sem_FITID_usa_hash_estavel_de_data_valor_descricao_e_ordem()
    {
        var parsed = OfxParser.Parse(Sgml("341", "1", [new(null, Day, -10m, "TARIFA"), new(null, Day, -10m, "TARIFA")])).Value;
        var first = OfxParser.ExternalId(parsed.Transactions[0]);
        var again = OfxParser.ExternalId(OfxParser.Parse(Sgml("341", "1", [new(null, Day, -10m, "TARIFA"), new(null, Day, -10m, "TARIFA")])).Value.Transactions[0]);
        first.Should().StartWith("sem-fitid:").And.Be(again);
        OfxParser.ExternalId(parsed.Transactions[1]).Should().NotBe(first, "a ordem no arquivo separa dois lançamentos iguais");
    }

    [Fact]
    public void Valor_com_virgula_e_folha_vazia_no_SGML()
    {
        var text = Encoding.Latin1.GetString(Sgml("341", "1", [new("A", Day, -5m, "X")])).Replace("<TRNAMT>-5.00", "<TRNAMT>-1.234,56<CHECKNUM>", StringComparison.Ordinal);
        var parsed = OfxParser.Parse(Encoding.Latin1.GetBytes(text));
        parsed.IsSuccess.Should().BeTrue(parsed.Error?.Message);
        parsed.Value.Transactions[0].Amount.Should().Be(-1234.56m);
        parsed.Value.Transactions[0].FitId.Should().Be("A", "a folha vazia <CHECKNUM> não engole as seguintes");
    }

    [Fact]
    public void Magic_bytes_do_OFX_so_no_dono_que_declara()
    {
        var content = Sgml("341", "1", Three());
        AttachmentPolicy.Match("extrato.ofx", content).Should().BeNull("o OFX não está na lista geral (ADR 0009 adendo)");
        AttachmentPolicy.Match("extrato.ofx", content, [AttachmentPolicy.Ofx]).Should().Be(AttachmentPolicy.Ofx);
        AttachmentPolicy.Match("extrato.ofx", "<html><script>x</script>"u8, [AttachmentPolicy.Ofx]).Should().BeNull();
        AttachmentPolicy.Match("extrato.pdf", content, [AttachmentPolicy.Ofx]).Should().BeNull();
    }

    [Theory]
    [InlineData("341", "12345-6", true)]
    [InlineData("0341", "0000123456", true)]
    [InlineData("341", "12345", true)]
    [InlineData("341", "1234123456", true)]
    [InlineData("341", "99999-1", false)]
    [InlineData("237", "12345-6", false)]
    public void Conta_do_arquivo_conferida_com_a_escolhida(string bankId, string accountId, bool ok)
    {
        var account = new BankAccount { Name = "Itaú", Kind = BankAccountKind.Bank, BankCode = "341", Agency = "1234", Account = "12345-6" };
        var statement = OfxParser.Parse(Sgml(bankId, accountId, Three())).Value;
        var error = BankStatementImportService.AccountCheck(account, statement);
        if (ok)
        {
            error.Should().BeNull();
        }
        else
        {
            error!.Message.Should().StartWith("Este extrato é da conta final ").And.EndWith(", não da conta escolhida.");
        }
    }

    [Fact]
    public void Contraparte_mascarada_com_a_inicial_e_3_mais_2()
    {
        CounterpartyMask.Name("Maria da Silva").Should().Be("M***");
        CounterpartyMask.Document("529.982.247-25").Should().Be("529.***.***-25");
        CounterpartyMask.Document("47225005000186").Should().Be("47.2**.***/****-86");
        CounterpartyMask.Name(null).Should().BeNull();
    }

    [Fact]
    public void Lancamento_do_Inter_com_sinal_tipo_e_contraparte()
    {
        using var credit = JsonDocument.Parse(new FakeInterTransaction("T1", Day, 250.10m).Json());
        var line = InterStatementProvider.Line(credit.RootElement)!;
        line.Amount.Should().Be(250.10m);
        line.Kind.Should().Be(StatementLineKind.Pix);
        line.CounterpartyName.Should().Be("Maria da Silva");
        line.CounterpartyDocument.Should().Be("52998224725");
        using var debit = JsonDocument.Parse(new FakeInterTransaction("T2", Day, -9.90m, "TARIFA", "Tarifa", "TARIFA MENSAL", null).Json());
        InterStatementProvider.Line(debit.RootElement)!.Should().Match<ProviderStatementLine>(l => l.Amount == -9.90m && l.Kind == StatementLineKind.Fee && l.CounterpartyName == null);
        using var broken = JsonDocument.Parse("{\"dataTransacao\":\"2026-10-20\",\"valor\":\"1.00\",\"tipoOperacao\":\"C\"}");
        InterStatementProvider.Line(broken.RootElement).Should().BeNull("sem idTransacao a linha é recusada");
    }
}
