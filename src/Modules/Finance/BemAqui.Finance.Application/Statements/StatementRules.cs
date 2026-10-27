using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application.Statements;

/// <summary>
/// Seção <c>BankStatement</c> (BNK-03a): o que é regra de operação fica configurável, com o padrão da story. Prazo e tamanhos do OFX,
/// sobreposição da API, dias de saldo e o intervalo do "Buscar agora".
/// </summary>
public sealed class BankStatementOptions
{
    public const string Section = "BankStatement";

    /// <summary>"Buscar agora" no máximo uma vez neste intervalo, por conta (CA 1).</summary>
    public TimeSpan FetchCooldown { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Dias de sobreposição com a última linha importada (CA 1).</summary>
    public int OverlapDays { get; set; } = 3;

    /// <summary>Primeira busca sem data de corte nem linha anterior: estes dias para trás.</summary>
    public int InitialDays { get; set; } = 30;

    /// <summary>Janela de cada pedido ao banco (o período maior é dividido).</summary>
    public int WindowDays { get; set; } = 30;

    /// <summary>Dias fechados (até ontem) cujo saldo do banco é consultado a cada importação pela API (CA 3).</summary>
    public int BalanceDays { get; set; } = 5;

    /// <summary>Diferença de saldo mais antiga que isto gera aviso a quem tem <c>finance.edit</c> (CA 3).</summary>
    public int DifferenceAlertDays { get; set; } = 2;

    /// <summary>Tamanho da página do extrato do Inter.</summary>
    public int InterPageSize { get; set; } = 1000;

    public long OfxMaxBytes { get; set; } = 5L * 1024 * 1024;

    public int OfxMaxTransactions { get; set; } = 20_000;

    public TimeSpan OfxParseTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Prazo máximo da contingência do OFX numa conta com API (D13).</summary>
    public int ContingencyMaxDays { get; set; } = 30;

    public OfxLimits OfxLimits => new(OfxMaxBytes, OfxMaxTransactions, OfxParseTimeout);
}

/// <summary>Tipo do lançamento a partir do tipo do Inter ou do <c>TRNTYPE</c> e do memo do OFX.</summary>
public static class StatementKinds
{
    /// <summary><c>tipoTransacao</c> do Inter (PIX, BOLETO_COBRANCA, PAGAMENTO, TARIFA, ...), com o título como desempate.</summary>
    public static StatementLineKind FromInter(string? type, string? title)
    {
        var text = ((type ?? string.Empty) + " " + (title ?? string.Empty)).ToUpperInvariant();
        return FromText(text) ?? StatementLineKind.Other;
    }

    /// <summary>Memo e nome primeiro (o banco brasileiro escreve "PIX RECEBIDO", "TAR PACOTE"); depois o <c>TRNTYPE</c>.</summary>
    public static StatementLineKind FromOfx(string? transactionType, string? memo, string? name)
    {
        if (FromText(((memo ?? string.Empty) + " " + (name ?? string.Empty)).ToUpperInvariant()) is { } byText)
        {
            return byText;
        }

        return (transactionType ?? string.Empty).ToUpperInvariant() switch
        {
            "FEE" or "SRVCHG" => StatementLineKind.Fee,
            "INT" => StatementLineKind.Interest,
            "DIV" => StatementLineKind.Yield,
            "CHECK" => StatementLineKind.Check,
            "DEP" or "DIRECTDEP" => StatementLineKind.Deposit,
            "XFER" => StatementLineKind.Transfer,
            "POS" or "ATM" => StatementLineKind.Card,
            _ => StatementLineKind.Other,
        };
    }

    private static StatementLineKind? FromText(string text)
    {
        if (text.Contains("ESTORNO", StringComparison.Ordinal) || text.Contains("DEVOLU", StringComparison.Ordinal))
        {
            return StatementLineKind.Reversal;
        }

        if (text.Contains("PIX", StringComparison.Ordinal))
        {
            return StatementLineKind.Pix;
        }

        if (text.Contains("BOLETO", StringComparison.Ordinal) || text.Contains("COBRANCA", StringComparison.Ordinal) || text.Contains("COBRANÇA", StringComparison.Ordinal)
            || text.Contains("TITULO", StringComparison.Ordinal) || text.Contains("TÍTULO", StringComparison.Ordinal))
        {
            return StatementLineKind.Boleto;
        }

        if (text.Contains("TARIFA", StringComparison.Ordinal) || text.Contains("TAR ", StringComparison.Ordinal) || text.StartsWith("TAR", StringComparison.Ordinal))
        {
            return StatementLineKind.Fee;
        }

        if (text.Contains("IOF", StringComparison.Ordinal) || text.Contains("IMPOSTO", StringComparison.Ordinal) || text.Contains("DARF", StringComparison.Ordinal))
        {
            return StatementLineKind.Tax;
        }

        if (text.Contains("TED", StringComparison.Ordinal))
        {
            return StatementLineKind.Ted;
        }

        if (text.Contains("DOC ", StringComparison.Ordinal))
        {
            return StatementLineKind.Doc;
        }

        if (text.Contains("RENDIMENTO", StringComparison.Ordinal) || text.Contains("INVESTIMENTO", StringComparison.Ordinal) || text.Contains("CDB", StringComparison.Ordinal))
        {
            return StatementLineKind.Yield;
        }

        if (text.Contains("JUROS", StringComparison.Ordinal))
        {
            return StatementLineKind.Interest;
        }

        if (text.Contains("CARTAO", StringComparison.Ordinal) || text.Contains("CARTÃO", StringComparison.Ordinal))
        {
            return StatementLineKind.Card;
        }

        if (text.Contains("CHEQUE", StringComparison.Ordinal))
        {
            return StatementLineKind.Check;
        }

        if (text.Contains("TRANSFERENCIA", StringComparison.Ordinal) || text.Contains("TRANSFERÊNCIA", StringComparison.Ordinal))
        {
            return StatementLineKind.Transfer;
        }

        return text.Contains("DEPOSITO", StringComparison.Ordinal) || text.Contains("DEPÓSITO", StringComparison.Ordinal) ? StatementLineKind.Deposit : null;
    }
}

/// <summary>
/// Contraparte para quem só tem <c>finance.view</c> (BNK-03a CA 8, ponto 31): o nome só com a inicial e o documento com os 3
/// primeiros e os 2 últimos dígitos (3+2).
/// </summary>
public static class CounterpartyMask
{
    public static string? Name(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var first = name.Trim()[0];
        return char.ToUpperInvariant(first) + "***";
    }

    public static string? Document(string? document)
    {
        var digits = BrazilianDocuments.Normalize(document);
        if (digits.Length == 0)
        {
            return null;
        }

        if (digits.Length < 6)
        {
            return new string('*', digits.Length);
        }

        var masked = digits[..3] + new string('*', digits.Length - 5) + digits[^2..];
        return digits.Length switch
        {
            11 => $"{masked[..3]}.{masked[3..6]}.{masked[6..9]}-{masked[9..]}",
            14 => $"{masked[..2]}.{masked[2..5]}.{masked[5..8]}/{masked[8..12]}-{masked[12..]}",
            _ => masked,
        };
    }
}
