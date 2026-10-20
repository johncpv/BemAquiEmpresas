using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>FIN-02 CA 10: título a pagar não tem baixa; só o caso de uso de pagamento da OP (FIN-03a) cria transação Payment.</summary>
public class FinanceRulesTests
{
    /// <summary>Arquivos que podem criar transação de pagamento: o pagamento da OP entra aqui na FIN-03a.</summary>
    private static readonly string[] PaymentWriters = [];

    [Fact]
    public void So_o_pagamento_da_OP_cria_transacao_Payment()
    {
        var modules = Path.Combine(Solution.RootDirectory, "src", "Modules");
        var writers = Directory.GetFiles(modules, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("Type = TransactionType.Payment", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(modules, f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();
        writers.Should().BeSubsetOf(PaymentWriters, "pagamento só pela ordem de pagamento aprovada (R-FIN-6)");
    }
}
