using BemAqui.Finance.Application.Reconciliation;
using BemAqui.Finance.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// BNK-03b, unidade: motor de sugestão com a ordem do CA 1 e a janela de 3 dias, sem dupla sugestão e sem Alta para linha não verificada;
/// tipo do lançamento sem OP pela regra e pelo tipo do banco (CA 2 e 8); soma 1×N e N×1 (CA 4).
/// </summary>
public class ReconciliationUnitTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);
    private const string Supplier = "47225005000186";

    private static string? Plain(string? text) => text?.Replace((char)0xA0, (char)0x20).Replace((char)0x202F, (char)0x20);

    private static SuggestionLine Line(decimal amount, int day = 0, string description = "PIX", string? document = null, string? externalId = null, bool verified = true) =>
        new(Guid.NewGuid(), Day.AddDays(day), amount, description, document, externalId ?? Guid.NewGuid().ToString("N"), verified);

    private static SuggestionTransaction Tx(decimal amount, int day = 0, string? document = null, string role = "do fornecedor", string[]? references = null, string[]? numbers = null) =>
        new(Guid.NewGuid(), Day.AddDays(day), amount, references ?? [], document, role, numbers ?? []);

    public static TheoryData<int, string?, bool, string?, string?> WindowCases => new()
    {
        // dias de diferença, documento da linha, sugere?, confiança, motivo
        { 0, null, true, "High", "Mesmo valor e mesma data" },
        { 1, Supplier, true, "Medium", "Mesmo valor, 1 dia de diferença, CNPJ do fornecedor" },
        { -3, Supplier, true, "Medium", "Mesmo valor, 3 dias de diferença, CNPJ do fornecedor" },
        { 4, Supplier, false, null, null },
        { 2, null, false, null, null },
    };

    [Theory]
    [MemberData(nameof(WindowCases))]
    public void Sugere_pelo_valor_data_em_ate_3_dias_e_documento(int days, string? document, bool suggests, string? confidence, string? reason)
    {
        var line = Line(-300m, days, "PAGTO FORNECEDOR", document);
        var tx = Tx(-300m, 0, Supplier);

        var result = ReconciliationSuggester.Suggest([line], [tx]);

        if (!suggests)
        {
            result.Should().BeEmpty();
            return;
        }

        result.Should().ContainSingle();
        result[0].TransactionId.Should().Be(tx.Id);
        result[0].Confidence.ToString().Should().Be(confidence);
        result[0].Reason.Should().Be(reason);
    }

    [Fact]
    public void Identificador_do_banco_vem_antes_e_uma_transacao_nunca_vai_para_duas_linhas()
    {
        // Duas linhas iguais de R$ 1.000,00 no mesmo dia; a segunda traz o nosso número do boleto: ela leva o boleto, a primeira a outra.
        var plain = Line(1000m, 0);
        var boleto = Line(1000m, 0, "BOLETO RECEBIDO 00012345678");
        var slipTx = Tx(1000m, 0, references: ["00012345678"]);
        var otherTx = Tx(1000m, 0);

        var result = ReconciliationSuggester.Suggest([plain, boleto], [slipTx, otherTx]);

        result.Should().HaveCount(2);
        result.Single(r => r.LineId == boleto.Id).Should().Match<PairSuggestion>(r => r.TransactionId == slipTx.Id && r.Reason == ReconciliationSuggester.SameBankId && r.Confidence == ReconciliationConfidence.High);
        result.Single(r => r.LineId == plain.Id).TransactionId.Should().Be(otherTx.Id);
        result.Select(r => r.TransactionId).Should().OnlyHaveUniqueItems();

        // Três linhas e só uma transação: só uma linha ganha par.
        ReconciliationSuggester.Suggest([Line(50m), Line(50m), Line(50m)], [Tx(50m)]).Should().ContainSingle();
    }

    [Fact]
    public void Mesma_data_com_varias_transacoes_iguais_fica_media_e_o_documento_desempata()
    {
        var line = Line(-80m, 0, "PIX ENVIADO", Supplier);
        var a = Tx(-80m);
        var b = Tx(-80m, 0, Supplier);
        ReconciliationSuggester.Suggest([line], [a, b]).Single().Should().Match<PairSuggestion>(s => s.TransactionId == b.Id && s.Confidence == ReconciliationConfidence.High
            && s.Reason == "Mesmo valor e mesma data, CNPJ do fornecedor");

        var anonymous = Line(-80m);
        ReconciliationSuggester.Suggest([anonymous], [Tx(-80m), Tx(-80m)]).Single().Confidence.Should().Be(ReconciliationConfidence.Medium);
    }

    [Fact]
    public void Numero_do_titulo_ou_da_OP_na_descricao_conta_como_documento_e_linha_nao_verificada_nunca_tem_Alta()
    {
        var line = Line(-120m, 2, "PAGAMENTO OP-2026-0007");
        var tx = Tx(-120m, numbers: ["CP-2026-0010", "OP-2026-0007"]);
        ReconciliationSuggester.Suggest([line], [tx]).Single().Reason.Should().Be("Mesmo valor, 2 dias de diferença, número OP-2026-0007 na descrição");

        var unverified = Line(500m, 0, "BOLETO 00099887766", verified: false);
        var all = ReconciliationSuggester.Suggest([unverified, Line(10m, 0, verified: false)], [Tx(500m, references: ["00099887766"]), Tx(10m)]);
        all.Should().HaveCount(2).And.OnlyContain(s => s.Confidence == ReconciliationConfidence.Medium && s.Reason.EndsWith("linha não verificada pelo banco", StringComparison.Ordinal));
    }

    [Fact]
    public void Tipo_do_lancamento_sem_OP_pela_regra_e_pelo_tipo_do_banco_e_saida_comum_nunca()
    {
        var rule = new BankStatementRule { Keyword = "pacote", EntryKind = BankEntryKind.BankFee };
        ReconciliationRules.EntryKindFor("TAR PACOTE SERVICOS", StatementLineKind.Other, -12.9m, [rule]).Should().Be((BankEntryKind.BankFee, rule));
        ReconciliationRules.EntryKindFor("TAR PACOTE SERVICOS", StatementLineKind.Other, 12.9m, [rule]).Should().BeNull("o sinal do tipo precisa ser o da linha");
        ReconciliationRules.EntryKindFor("IOF S/ OPERACAO", StatementLineKind.Tax, -3m, []).Should().Be((BankEntryKind.Iof, (BankStatementRule?)null));
        ReconciliationRules.EntryKindFor("RENDIMENTO CDB", StatementLineKind.Yield, 4m, []).Should().Be((BankEntryKind.Yield, (BankStatementRule?)null));
        ReconciliationRules.EntryKindFor("PAGTO BOLETO FORNECEDOR", StatementLineKind.Boleto, -300m, []).Should().BeNull();

        // CA 8: saída não reconhecida não vira lançamento (nem "Outra saída"); entrada só com os tipos de entrada.
        ReconciliationRules.EntryAllowed(BankEntryKind.BankFee, -300m, null).Should().BeFalse();
        ReconciliationRules.EntryAllowed(BankEntryKind.OtherOut, -12.9m, (BankEntryKind.BankFee, rule)).Should().BeFalse();
        ReconciliationRules.EntryAllowed(BankEntryKind.BankFee, -12.9m, (BankEntryKind.BankFee, rule)).Should().BeTrue();
        ReconciliationRules.EntryAllowed(BankEntryKind.OtherIn, 50m, null).Should().BeTrue();
        ReconciliationRules.EntryAllowed(BankEntryKind.BankFee, 50m, null).Should().BeFalse();
    }

    [Fact]
    public void Soma_1xN_e_Nx1_igual_e_a_mensagem_quando_nao_bate()
    {
        ReconciliationRules.SumError([-300m], [-100m, -200m]).Should().BeNull();
        ReconciliationRules.SumError([150m, 50m], [200m]).Should().BeNull();
        Plain(ReconciliationRules.SumError([-300m], [-100m, -150m])).Should().Be("A soma das transações (-R$ 250,00) não bate com a linha (-R$ 300,00).");
        Plain(ReconciliationRules.SumError([150m, 40m], [200m])).Should().Be("A soma das linhas (R$ 190,00) não bate com a transação (R$ 200,00).");
        ReconciliationRules.SumError([1m, 1m], [1m, 1m]).Should().NotBeNull();
    }
}
