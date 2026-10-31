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
    public void So_o_extrato_cria_transferencia_e_lancamento_sem_OP() =>
        FilesContaining("Type = TransactionType.BankEntry", "Type = TransactionType.Transfer").Should().BeSubsetOf(
            ["Finance/BemAqui.Finance.Application/BankMovementService.Transfers.cs", "Finance/BemAqui.Finance.Application/BankMovementService.Entries.cs"], "FIN-04 CA 15");

    [Fact]
    public void So_o_pagamento_da_OP_cria_transacao_Payment() =>
        FilesContaining("Type = TransactionType.Payment").Should().BeSubsetOf(PaymentWriters, "pagamento só pela ordem de pagamento aprovada (R-FIN-6)");

    /// <summary>
    /// BNK-03b CA 8 (R-FIN-5): a conciliação nunca cria pagamento nem transação. Ela só vincula, chama a baixa de recebimento (FIN-01) e o
    /// lançamento sem OP (FIN-04); saída sem par vai para "Vincular a uma OP paga" ou "Abrir OP para regularizar".
    /// </summary>
    [Fact]
    public void A_conciliacao_nao_chama_o_pagamento_nem_cria_transacao()
    {
        var folder = Path.Combine(Solution.RootDirectory, "src", "Modules", "Finance", "BemAqui.Finance.Application", "Reconciliation");
        var files = Directory.GetFiles(folder, "*.cs");
        files.Should().NotBeEmpty();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            text.Should().NotContainAny(new[] { "PaymentOrderService", "PayAsync(", "new FinancialTransaction", "TransactionType.Payment", "BankTransfer" }, $"{Path.GetFileName(file)} não paga nem cria transação");
        }
    }

    [Fact]
    public void So_a_OP_muda_o_titulo_para_aguardando_aprovado_ou_pago() =>
        FilesContaining("Status = PayableStatus.AwaitingApproval", "Status = PayableStatus.Approved", "? PayableStatus.Paid", "Status = PayableStatus.Paid")
            .Should().BeSubsetOf(PayableStatusWriters, "a situação do título é derivada da OP (ADR 0019 §2)");
}
