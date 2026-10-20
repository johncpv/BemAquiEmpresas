using BemAqui.Finance.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-02 CA 6: linha digitável (FEBRABAN) com dígitos conferidos, valor e vencimento lidos.</summary>
public class BoletoLineTests
{
    /// <summary>Linha de 47 dígitos a partir do código de barras (banco 077, moeda 9), com os dígitos verificadores certos.</summary>
    public static string BankLine(DateOnly due, decimal amount, string free = "1234567890123456789012345")
    {
        var factor = due.DayNumber - new DateOnly(1997, 10, 7).DayNumber;
        while (factor > 9999)
        {
            factor -= 9000;
        }

        var value = ((long)(amount * 100)).ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
        var head = "0779";
        var tail = factor.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + value + free;
        var dv = BoletoLine.BankGeneralDv(head + tail);
        var barcode = head + dv + tail;
        string Field(string s) => s + BoletoLine.Mod10(s);
        return Field(barcode[..4] + barcode[19..24]) + Field(barcode[24..34]) + Field(barcode[34..44]) + barcode[4] + barcode[5..19];
    }

    [Fact]
    public void Linha_bancaria_valida_traz_valor_e_vencimento_e_digito_errado_e_recusado()
    {
        var today = new DateOnly(2026, 10, 20);
        var line = BankLine(new DateOnly(2026, 11, 10), 1234.56m);
        line.Should().HaveLength(47);
        var info = BoletoLine.Parse(line, today);
        info.Should().NotBeNull();
        info!.Amount.Should().Be(1234.56m);
        info.DueDate.Should().Be(new DateOnly(2026, 11, 10), "fator depois do recomeço de 2025");

        var wrong = line[..9] + ((line[9] - '0' + 1) % 10) + line[10..];
        BoletoLine.Parse(wrong, today).Should().BeNull("dígito do primeiro campo errado");
        var general = line[..32] + ((line[32] - '0' + 1) % 10) + line[33..];
        BoletoLine.Parse(general, today).Should().BeNull("dígito geral errado");
        BoletoLine.Parse("123", today).Should().BeNull();
    }

    [Fact]
    public void Linha_de_arrecadacao_de_48_digitos_no_modulo_10()
    {
        var barcode = "8" + "2" + "6" + "0" + "00000012345" + new string((char)48, 29);
        barcode = barcode[..44];
        var blocks = Enumerable.Range(0, 4).Select(i => barcode.Substring(i * 11, 11)).Select(b => b + BoletoLine.Mod10(b));
        var line = string.Concat(blocks);
        var info = BoletoLine.Parse(line, new DateOnly(2026, 10, 20));
        info.Should().NotBeNull();
        info!.Amount.Should().Be(123.45m);
    }
}
