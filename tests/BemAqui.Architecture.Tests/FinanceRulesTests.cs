using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// FIN-02 CA 10 e FIN-03a: só o pagamento da OP (e o estorno dele) cria transação Payment, e só os casos de uso da OP mudam
/// o título a pagar para AwaitingApproval, Approved ou Paid.
/// </summary>
public class FinanceRulesTests
{
    private static readonly string[] PaymentWriters =
    [
        "Finance/BemAqui.Finance.Application/PaymentOrderService.Pay.cs",
        "Finance/BemAqui.Finance.Application/PaymentOrderService.Reverse.cs",
    ];

    private static readonly string[] PayableStatusWriters =
    [
        "Finance/BemAqui.Finance.Application/PaymentOrderService.Submit.cs",
        "Finance/BemAqui.Finance.Application/PaymentOrderService.Decide.cs",
        "Finance/BemAqui.Finance.Application/PaymentOrderService.Pay.cs",
        "Finance/BemAqui.Finance.Application/PaymentOrderService.Reverse.cs",
    ];

    private static List<string> FilesContaining(params string[] texts)
    {
        var modules = Path.Combine(Solution.RootDirectory, "src", "Modules");
        var sep = Path.DirectorySeparatorChar;
        return Directory.GetFiles(modules, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
            .Where(f => { var text = File.ReadAllText(f); return texts.Any(t => text.Contains(t, StringComparison.Ordinal)); })
            .Select(f => Path.GetRelativePath(modules, f).Replace(sep, '/'))
            .ToList();
    }

    [Fact]
    public void So_o_pagamento_da_OP_cria_transacao_Payment() =>
        FilesContaining("Type = TransactionType.Payment").Should().BeSubsetOf(PaymentWriters, "pagamento só pela ordem de pagamento aprovada (R-FIN-6)");

    [Fact]
    public void So_a_OP_muda_o_titulo_para_aguardando_aprovado_ou_pago() =>
        FilesContaining("Status = PayableStatus.AwaitingApproval", "Status = PayableStatus.Approved", "? PayableStatus.Paid", "Status = PayableStatus.Paid")
            .Should().BeSubsetOf(PayableStatusWriters, "a situação do título é derivada da OP (ADR 0019 §2)");
}
