using BemAqui.Sales.Contracts;

namespace BemAqui.Sales.Domain;

/// <summary>
/// Máquina de estados do pagamento (rascunho C §1, VND-01 CA 7): <c>Created → Pending → Captured → (Voided | Refunded)</c>, com <c>Failed</c> e
/// <c>Unknown</c>. <c>Unknown</c> é a queda entre autorizar e confirmar: sai para capturado, falho ou cancelado depois da consulta. Capturado só
/// sai por estorno (<c>Voided</c> antes da liquidação, <c>Refunded</c> depois). <c>Voided</c>, <c>Refunded</c> e <c>Failed</c> são finais.
/// </summary>
public static class PaymentStateMachine
{
    private static readonly Dictionary<SalePaymentStatus, SalePaymentStatus[]> Next = new()
    {
        [SalePaymentStatus.Created] = [SalePaymentStatus.Pending, SalePaymentStatus.Captured, SalePaymentStatus.Failed, SalePaymentStatus.Unknown, SalePaymentStatus.Voided],
        [SalePaymentStatus.Pending] = [SalePaymentStatus.Captured, SalePaymentStatus.Failed, SalePaymentStatus.Unknown, SalePaymentStatus.Voided],
        [SalePaymentStatus.Unknown] = [SalePaymentStatus.Captured, SalePaymentStatus.Failed, SalePaymentStatus.Voided],
        [SalePaymentStatus.Captured] = [SalePaymentStatus.Voided, SalePaymentStatus.Refunded],
        [SalePaymentStatus.Voided] = [],
        [SalePaymentStatus.Refunded] = [],
        [SalePaymentStatus.Failed] = [],
    };

    public static bool CanMove(SalePaymentStatus from, SalePaymentStatus to) => Next.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static bool IsFinal(SalePaymentStatus status) => Next[status].Length == 0;

    /// <summary>Caminho válido de <c>Created</c> até o estado entregue pelo canal (vazio para <c>Created</c>).</summary>
    public static IReadOnlyList<SalePaymentStatus>? PathTo(SalePaymentStatus target) => target switch
    {
        SalePaymentStatus.Created => [],
        SalePaymentStatus.Pending or SalePaymentStatus.Captured or SalePaymentStatus.Failed or SalePaymentStatus.Unknown or SalePaymentStatus.Voided => [target],
        SalePaymentStatus.Refunded => [SalePaymentStatus.Captured, SalePaymentStatus.Refunded],
        _ => null,
    };
}

/// <summary>Regras da venda (VND-01): dinheiro, validação por canal (CA 6), soma dos pagamentos (CA 5) e a recusa de número de cartão (CA 8).</summary>
public static class SaleRules
{
    public const int MaxLines = 500;

    public const int MaxPayments = 50;

    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal Quantity(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    /// <summary>Canal com caixa: só o PDV. Nele, caixa e sessão são obrigatórios; nos canais sem caixa (online, OS), ficam nulos.</summary>
    public static bool HasRegister(SaleChannel channel) => channel == SaleChannel.Pos;

    /// <summary>Erros da validação por canal (CA 6), por campo.</summary>
    public static IReadOnlyDictionary<string, string[]> ChannelErrors(SaleChannel channel, Guid? registerId, Guid? cashSessionId)
    {
        var errors = new Dictionary<string, string[]>();
        if (HasRegister(channel))
        {
            if (registerId is null)
            {
                errors["registerId"] = ["Venda do PDV exige o caixa."];
            }

            if (cashSessionId is null)
            {
                errors["cashSessionId"] = ["Venda do PDV exige a sessão de caixa."];
            }
        }
        else if (channel is SaleChannel.Online or SaleChannel.ServiceOrder)
        {
            if (registerId is not null)
            {
                errors["registerId"] = ["Venda deste canal não tem caixa."];
            }

            if (cashSessionId is not null)
            {
                errors["cashSessionId"] = ["Venda deste canal não tem sessão de caixa."];
            }
        }

        return errors;
    }

    /// <summary>Capturado líquido do troco: Σ (valor − troco) dos pagamentos capturados.</summary>
    public static decimal CapturedNet(IEnumerable<(SalePaymentStatus Status, decimal Amount, decimal Change)> payments) =>
        Money(payments.Where(p => p.Status == SalePaymentStatus.Captured).Sum(p => p.Amount - p.Change));

    /// <summary>
    /// Parece número de cartão (regra 14 do PRD, revisão de 15/11/2026): uma sequência de 13 a 19 dígitos (espaços e hífens ignorados) que passa
    /// no dígito de Luhn. Campo livre com isso é recusado: o sistema nunca guarda número de cartão.
    /// </summary>
    public static bool LooksLikeCardNumber(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // Cada sequência máxima de dígitos (com espaço ou hífen no meio, como no cartão impresso) é conferida inteira.
        var digits = new List<int>();
        foreach (var c in text.Append('x'))
        {
            if (char.IsAsciiDigit(c))
            {
                digits.Add(c - '0');
                continue;
            }

            if (c is ' ' or '-' && digits.Count > 0)
            {
                continue;
            }

            if (digits.Count is >= 13 and <= 19 && Luhn(digits))
            {
                return true;
            }

            digits.Clear();
        }

        return false;
    }

    private static bool Luhn(List<int> digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Count; i++)
        {
            var d = digits[digits.Count - 1 - i];
            if (i % 2 == 1)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
        }

        return sum % 10 == 0;
    }
}
