using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>
/// EST-01b, regras sem banco: a diferença nos dois modos e o recálculo na aprovação (CA 2 e 7), o lote negativo e o reservado da
/// posição (CA 4), a regra do autor com o Proprietário único (CA 8), os valores de sobra e falta, o número AJ (D15), o custo médio de
/// sobra e falta pelo motor e o bloqueio do local (CA 18).
/// </summary>
public class StockAdjustmentRulesTests
{
    private static readonly Guid A = Guid.CreateVersion7();
    private static readonly Guid B = Guid.CreateVersion7();
    private static readonly Guid Lot1 = Guid.CreateVersion7();
    private static readonly Guid Lot2 = Guid.CreateVersion7();

    private static AdjustmentBalance Balance(decimal onHand, decimal reserved = 0m, decimal? lot = null) => new(onHand, reserved, lot ?? onHand);

    [Fact]
    public void Quantidade_contada_vira_diferenca_sobre_o_saldo_e_a_diferenca_fica_como_veio()
    {
        StockAdjustmentRules.Delta(StockAdjustmentMode.Counted, 10m, 7m, 0m).Should().Be(-3m, "contado 7, sistema 10: falta 3");
        StockAdjustmentRules.Delta(StockAdjustmentMode.Counted, 10m, 12.5m, 99m).Should().Be(2.5m, "no modo Counted a diferença enviada não conta");
        StockAdjustmentRules.Delta(StockAdjustmentMode.Delta, 10m, null, -3m).Should().Be(-3m);
        StockAdjustmentRules.Delta(StockAdjustmentMode.Delta, 50m, 1m, 2.00005m).Should().Be(2.0001m, "4 casas AwayFromZero; o contado não conta no modo Delta");
        StockAdjustmentRules.Delta(StockAdjustmentMode.Counted, 0m, 0m, 0m).Should().Be(0m);
    }

    [Fact]
    public void Na_aprovacao_o_modo_Counted_recalcula_sobre_o_saldo_de_agora_e_o_Delta_mantem()
    {
        // Enviado: sistema 10, contado 7 (−3). Antes da aprovação entraram 5.
        var counted = StockAdjustmentRules.Evaluate(StockAdjustmentMode.Counted, [new AdjustmentLineInput(A, null, 7m, -3m)], [Balance(15m)], ["UN"]);
        counted.Lines[0].Delta.Should().Be(-8m, "CA 7: o contado vale sobre o saldo de agora");
        counted.Lines[0].PositionAfter.Should().Be(7m);
        var delta = StockAdjustmentRules.Evaluate(StockAdjustmentMode.Delta, [new AdjustmentLineInput(A, null, null, -3m)], [Balance(15m)], ["UN"]);
        delta.Lines[0].Delta.Should().Be(-3m, "CA 7 e 13: a diferença se soma ao saldo de agora");
        delta.Lines[0].PositionAfter.Should().Be(12m);
        delta.Refusal.Should().BeNull();
    }

    [Fact]
    public void Linha_abaixo_do_reservado_e_recusada_com_a_mensagem_do_CA_4()
    {
        var (_, refusal) = StockAdjustmentRules.Evaluate(StockAdjustmentMode.Counted, [new AdjustmentLineInput(A, null, 2m, 0m)], [Balance(10m, reserved: 4m)], ["UN"]);
        refusal.Should().NotBeNull();
        refusal!.Index.Should().Be(0);
        refusal.Message.Should().Be("O saldo ficaria 2,0000 UN, abaixo do reservado (4,0000 UN) para OS.");
        refusal.After.Should().Be(2m);
        refusal.Reserved.Should().Be(4m);

        StockAdjustmentRules.Evaluate(StockAdjustmentMode.Counted, [new AdjustmentLineInput(A, null, 4m, 0m)], [Balance(10m, reserved: 4m)], ["UN"]).Refusal
            .Should().BeNull("igual ao reservado pode: o disponível fica zero");
        StockAdjustmentRules.Evaluate(StockAdjustmentMode.Counted, [new AdjustmentLineInput(A, null, 15m, 0m)], [Balance(10m, reserved: 10m)], ["UN"]).Refusal
            .Should().BeNull("sobra nunca é recusada pelo reservado");
    }

    [Fact]
    public void Lote_negativo_e_recusado_e_a_posicao_soma_os_lotes_do_item()
    {
        var (_, negative) = StockAdjustmentRules.Evaluate(StockAdjustmentMode.Delta, [new AdjustmentLineInput(A, Lot1, null, -6m)], [new AdjustmentBalance(20m, 0m, 5m)], ["UN"]);
        negative!.Message.Should().Be("O saldo do lote ficaria -1,0000 UN, abaixo de zero.");

        // Dois lotes do mesmo item: −4 num e +1 no outro; a posição (10, reservado 7) fica 7: pode.
        var lines = new[] { new AdjustmentLineInput(A, Lot1, null, -4m), new AdjustmentLineInput(A, Lot2, null, 1m) };
        var balances = new[] { new AdjustmentBalance(10m, 7m, 6m), new AdjustmentBalance(10m, 7m, 4m) };
        var both = StockAdjustmentRules.Evaluate(StockAdjustmentMode.Delta, lines, balances, ["UN", "UN"]);
        both.Refusal.Should().BeNull();
        both.Lines.Should().OnlyContain(l => l.PositionAfter == 7m);
        var tooMuch = StockAdjustmentRules.Evaluate(StockAdjustmentMode.Delta, [lines[0], new AdjustmentLineInput(A, Lot2, null, -1m)], balances, ["UN", "UN"]);
        tooMuch.Refusal!.Index.Should().Be(0, "a primeira linha que tira do item é a recusada");
        tooMuch.Refusal.Message.Should().Contain("abaixo do reservado (7,0000 UN)");
    }

    [Fact]
    public void Itens_diferentes_sao_conferidos_cada_um_na_sua_posicao()
    {
        var lines = new[] { new AdjustmentLineInput(A, null, 9m, 0m), new AdjustmentLineInput(B, null, 1m, 0m) };
        var (results, refusal) = StockAdjustmentRules.Evaluate(StockAdjustmentMode.Counted, lines, [Balance(10m, 2m), Balance(5m, 3m)], ["UN", "KG"]);
        results.Select(r => r.Delta).Should().Equal(-1m, -4m);
        refusal!.Index.Should().Be(1);
        refusal.Message.Should().Be("O saldo ficaria 1,0000 KG, abaixo do reservado (3,0000 KG) para OS.");
    }

    [Fact]
    public void Sobras_e_faltas_em_reais_com_duas_casas_por_linha()
    {
        StockAdjustmentRules.Value(-3m, 12.345678m).Should().Be(37.04m, "|Δ| × custo, 2 casas AwayFromZero");
        var (surplus, shortage) = StockAdjustmentRules.Totals([(2m, 10m), (-3m, 12.345678m), (0m, 99m), (0.5m, 0.01m)]);
        surplus.Should().Be(20.01m, "20,00 + 0,005 → 0,01");
        shortage.Should().Be(37.04m);
    }

    [Fact]
    public void Autor_nao_decide_salvo_o_Proprietario_unico()
    {
        var author = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        StockAdjustmentRules.MayDecide(author, author, decidingAlone: false).Should().BeFalse("CA 8: quem lança o ajuste não o aprova");
        StockAdjustmentRules.MayDecide(author, author, decidingAlone: true).Should().BeTrue("Proprietário único: Autoaprovado");
        StockAdjustmentRules.MayDecide(author, other, decidingAlone: false).Should().BeTrue();
        SoleOwnerRule.NoOtherEligible([author], author, [author]).Should().BeTrue("só o autor aprova no Estoque");
        SoleOwnerRule.NoOtherEligible([author, other], author, [author]).Should().BeFalse("há outro aprovador elegível");
    }

    [Fact]
    public void Numero_AJ_motivos_modos_e_o_mapa_do_aprovador()
    {
        NumberFormats.Defaults[StockAdjustmentRules.DocType].Format(2026, 1).Should().Be("AJ-2026-0001", "D15");
        NumberFormats.Defaults.Values.Select(f => f.Prefix).Should().OnlyHaveUniqueItems("prefixos únicos no tenant");
        Enum.GetValues<StockAdjustmentReason>().Select(StockAdjustmentRules.ReasonLabel).Should().Equal("Perda/avaria", "Erro de lançamento", "Sobra encontrada", "Contagem", "Outro");
        StockAdjustmentRules.ModeLabel(StockAdjustmentMode.Counted).Should().Be("Quantidade contada");
        StockAdjustmentRules.ModeLabel(StockAdjustmentMode.Delta).Should().Be("Diferença");
        (StockAdjustmentRules.ApprovalDocumentType, StockAdjustmentRules.ApproverPermission).Should().Be(("StockAdjustment", "inventory.approve"), "CA 14: tipo previsto no mapa do motor de alçadas");
        StockAdjustmentRules.MovementReason("AJ-2026-0001", StockAdjustmentReason.Other, new string('x', 300)).Length.Should().BeLessThanOrEqualTo(200);
    }

    [Fact]
    public void Sobra_entra_e_falta_sai_pelo_custo_medio_sem_mudar_o_medio()
    {
        // O motor usa o custo médio do momento como custo da sobra (StockPosting sem custo): a média não muda.
        AverageCostCalculator.Next(StockMovementType.Adjustment, 10m, 7.5m, 3m, 7.5m, reversesEntry: false).Should().Be(7.5m);
        AverageCostCalculator.Next(StockMovementType.Adjustment, 10m, 7.5m, -3m, 7.5m, reversesEntry: false).Should().Be(7.5m);
        AverageCostCalculator.SignAllowed(StockMovementType.Adjustment, 2m).Should().BeTrue();
        AverageCostCalculator.SignAllowed(StockMovementType.Adjustment, -2m).Should().BeTrue();
        ReversePolicy.SourceLink(StockSources.Adjustment, A).Should().Be("/app/estoque/ajustes/" + A, "o movimento do ajuste leva ao documento");
    }

    [Fact]
    public async Task Bloqueio_do_local_responde_409_com_o_link_do_inventario()
    {
        var count = Guid.CreateVersion7();
        (await new NoLocationCountLock().CheckAsync([A])).Should().BeNull("até a EST-05 nada bloqueia");
        var error = LocationCountLocks.Blocked(new LocationCountBlock(A, "Almoxarifado", count, "INV-2026-0001"));
        error.Code.Should().Be("location_counting");
        error.Message.Should().Be("Há um inventário em contagem em Almoxarifado: INV-2026-0001. Ajustes e estornos manuais ficam bloqueados até fechar ou cancelar.");
        error.Data!["link"].Should().Be("/app/estoque/inventarios/" + count);
    }
}
