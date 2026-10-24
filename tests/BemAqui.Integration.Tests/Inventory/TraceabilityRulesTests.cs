using System.Text;
using BemAqui.BuildingBlocks.Export;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>
/// EST-04, regras sem banco: iniciais e nome do cliente PJ/PF com e sem <c>parties.view</c> (CA 9), proteção do CSV (CA 4), conciliação do
/// lote (CA 3), observação dos lotes da OS (CA 2), notas de sem série (CA 3), validade (CA 5) e o termo da busca (CA 10).
/// </summary>
public class TraceabilityRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static PartySummary Party(PartyKind kind, string name, string? document = "12345678909") =>
        new(Guid.CreateVersion7(), kind, name, null, document, [PartyRoleKind.Customer], true, false);

    [Theory]
    [InlineData("João Marcus Ribeiro", "J. M. R.")]
    [InlineData("JOHN MARCUS RICARDO DA MATA", "J. M. R. M.")]
    [InlineData("maria das dores e silva", "M. D. S.")]
    [InlineData("  Ana  ", "A.")]
    [InlineData("Élio d'Ávila", "É. D.")]
    [InlineData("", "Cliente")]
    [InlineData("123 ---", "Cliente")]
    public void Iniciais_do_cliente_pessoa_fisica(string name, string expected) => CustomerNameMasker.Initials(name).Should().Be(expected);

    [Fact]
    public void Cliente_PJ_pelo_nome_PF_pelo_nome_so_com_parties_view_e_sem_documento()
    {
        var pj = Party(PartyKind.PJ, "Elevadores Horizonte Ltda", "11222333000181");
        var pf = Party(PartyKind.PF, "João Marcus Ribeiro");

        CustomerNameMasker.Customer(pj, canViewParties: false).Should().BeEquivalentTo(new TraceCustomer(null, "Elevadores Horizonte Ltda", PartyKind.PJ, false), "CA 9: PJ pelo nome");
        CustomerNameMasker.Customer(pf, canViewParties: false).Should().BeEquivalentTo(new TraceCustomer(null, "J. M. R.", PartyKind.PF, true), "CA 9: PF sem parties.view, só as iniciais");
        CustomerNameMasker.Customer(pf, canViewParties: true).Should().BeEquivalentTo(new TraceCustomer(pf.Id, "João Marcus Ribeiro", PartyKind.PF, false), "CA 9: PF com parties.view, por extenso");
        CustomerNameMasker.Customer(null, true).Should().BeNull();
    }

    [Fact]
    public void DTOs_da_rastreabilidade_nao_tem_documento_nem_custo()
    {
        string[] forbidden = ["Document", "Cpf", "Cnpj", "Cost", "Amount", "Price", "Value", "Average"];
        Type[] dtos = [typeof(TraceCustomer), typeof(SerialTrace), typeof(LotTrace), typeof(TraceConsumption), typeof(TraceConsumedLot), typeof(TraceEntry), typeof(TraceSerial), typeof(TraceOtherLine),
            typeof(TraceSearchResult), typeof(TraceSerialHit), typeof(TraceLotHit), typeof(ItemLotRow), typeof(TraceLotBalance)];
        foreach (var dto in dtos)
        {
            dto.GetProperties().Select(p => p.Name).Where(n => forbidden.Any(f => n.Contains(f, StringComparison.Ordinal)))
                .Should().BeEmpty($"{dto.Name}: CA 8 e 9, sem documento do cliente e sem custo");
        }
    }

    [Theory]
    [InlineData("=SOMA(A1)", "'=SOMA(A1)")]
    [InlineData("+55 31", "'+55 31")]
    [InlineData("-3", "'-3")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("diz \"oi\"", "\"diz \"\"oi\"\"\"")]
    [InlineData("LOTE-1", "LOTE-1")]
    public void Celula_do_CSV_sem_injecao_de_formula(string value, string expected) => SafeCsv.Cell(value).Should().Be(expected, "CA 4");

    [Fact]
    public void Arquivo_CSV_tem_BOM_separador_ponto_e_virgula_e_quebra_CRLF()
    {
        var bytes = SafeCsv.Bytes(SafeCsv.Text(["a", "b"], [["=1", "ç"]]));
        bytes.Take(3).Should().Equal(Encoding.UTF8.Preamble.ToArray(), "CA 4: UTF-8 com BOM");
        Encoding.UTF8.GetString(bytes.AsSpan(3)).Should().Be("a;b\r\n'=1;ç\r\n");
        TraceabilityRules.CsvFileName("L/01 ;=x").Should().Be("rastreio-lote-L_01___x.csv");
    }

    private static LotLedgerLine Line(StockMovementType type, decimal quantity, string source = StockSources.GoodsReceipt, Guid? order = null, Guid? reverses = null, Guid? sourceId = null,
        string? reason = null, int minutes = 0, string? sourceNumber = null) =>
        new(Guid.CreateVersion7(), "MOV-2026-00000" + minutes, type, source, sourceId ?? Guid.CreateVersion7(), sourceNumber, order, reverses, quantity, T0.AddMinutes(minutes), reason);

    [Fact]
    public void Conciliacao_do_lote_fecha_recebido_menos_consumido_mais_outras_com_o_saldo()
    {
        var os1 = Guid.CreateVersion7();
        var os2 = Guid.CreateVersion7();
        var receipt = Line(StockMovementType.Receipt, 100m, sourceNumber: "REC-2026-0001");
        var transfer = Guid.CreateVersion7();
        var ledger = new List<LotLedgerLine>
        {
            receipt,
            Line(StockMovementType.Issue, -30m, StockSources.Requisition, os1, minutes: 1),
            Line(StockMovementType.Return, 5m, StockSources.Requisition, os1, minutes: 2),
            Line(StockMovementType.Issue, -20m, StockSources.Requisition, os2, minutes: 3),
            Line(StockMovementType.Adjustment, -4m, StockSources.Adjustment, reason: "Avaria na doca", minutes: 4, sourceNumber: "AJ-2026-0001"),
            Line(StockMovementType.Issue, -6m, StockSources.ManualPosting, minutes: 5),
            Line(StockMovementType.Transfer, -10m, StockSources.ManualPosting, sourceId: transfer, minutes: 6),
            Line(StockMovementType.Transfer, 10m, StockSources.ManualPosting, sourceId: transfer, minutes: 6),
        };

        var result = TraceabilityRules.Reconcile(ledger, onHand: 45m);
        result.TotalReceived.Should().Be(100m);
        result.TotalConsumed.Should().Be(45m, "CA 3: líquido das baixas menos as devoluções das OS");
        result.Consumptions.Should().HaveCount(2);
        result.Consumptions.Single(c => c.ProductionOrderId == os1).Quantity.Should().Be(25m);
        result.TotalOther.Should().Be(-10m);
        result.Others.Select(o => o.Explanation).Should().Equal(["Ajuste AJ-2026-0001: Avaria na doca", "Saída avulsa MOV-2026-000005"], "a diferença explicada linha a linha; a transferência que fecha fica fora");
        result.Balanced.Should().BeTrue("100 − 45 − 10 = 45");
        result.Entries.Should().ContainSingle().Which.MovementId.Should().Be(receipt.MovementId);

        TraceabilityRules.Reconcile(ledger, onHand: 44m).Balanced.Should().BeFalse("saldo que os movimentos não explicam aparece como diferença");
    }

    [Fact]
    public void Estorno_entra_na_conciliacao_e_marca_a_entrada_estornada()
    {
        var receipt = Line(StockMovementType.Receipt, 10m);
        var reversal = Line(StockMovementType.Receipt, -10m, reverses: receipt.MovementId, minutes: 1);
        var result = TraceabilityRules.Reconcile([receipt, reversal, Line(StockMovementType.Receipt, 4m, minutes: 2)], onHand: 4m);
        result.Balanced.Should().BeTrue();
        result.ReversedIds.Should().Contain(receipt.MovementId);
        result.Entries.Should().HaveCount(2, "a entrada estornada aparece marcada; o estorno não é outra entrada");

        var os = Guid.CreateVersion7();
        var issue = Line(StockMovementType.Issue, -3m, StockSources.Requisition, os);
        var undone = Line(StockMovementType.Issue, 3m, StockSources.Requisition, os, reverses: issue.MovementId);
        TraceabilityRules.Reconcile([Line(StockMovementType.Receipt, 3m), issue, undone], 3m).Consumptions.Should().BeEmpty("baixa estornada não é consumo");
    }

    [Fact]
    public void Lotes_da_OS_com_mais_de_uma_unidade_tem_a_observacao_do_ponto_87()
    {
        TraceabilityRules.SharedLotsNote("OS-2026-0007", 3).Should().Be("Lotes da OS OS-2026-0007 (3 unidades): a OS não separa o material por unidade.", "CA 2");
        TraceabilityRules.SharedLotsNote("OS-2026-0007", 1).Should().BeNull();
    }

    [Fact]
    public void Consumo_sem_serie_explica_o_motivo()
    {
        TraceabilityRules.NoSerialNote(0, ProductionOrderState.Completed, hasEntry: false, entryReversed: false, tracksSerial: true)
            .Should().Be("Sem série: OS concluída antes do registro de séries.", "CA 3: OS concluída antes da PRD-06a2");
        TraceabilityRules.NoSerialNote(0, ProductionOrderState.Completed, true, false, tracksSerial: false).Should().Be(TraceabilityRules.NoSerialProduct);
        TraceabilityRules.NoSerialNote(0, ProductionOrderState.InProgress, false, false, true).Should().Be(TraceabilityRules.NoSerialOpenOrder);
        TraceabilityRules.NoSerialNote(0, ProductionOrderState.InProgress, true, true, true).Should().Be(TraceabilityRules.NoSerialReversed);
        TraceabilityRules.NoSerialNote(2, ProductionOrderState.Completed, true, false, true).Should().BeNull();
    }

    [Fact]
    public void Validade_vencida_ou_vencendo_em_ate_30_dias()
    {
        var today = new DateOnly(2026, 10, 24);
        TraceabilityRules.Expiry(null, today).Should().Be((LotExpiryState.None, (int?)null));
        TraceabilityRules.Expiry(today.AddDays(-1), today).Should().Be((LotExpiryState.Expired, (int?)-1), "CA 5: crítico Vencido");
        TraceabilityRules.Expiry(today, today).State.Should().Be(LotExpiryState.Expiring);
        TraceabilityRules.Expiry(today.AddDays(30), today).Should().Be((LotExpiryState.Expiring, (int?)30), "CA 5: alerta até 30 dias");
        TraceabilityRules.Expiry(today.AddDays(31), today).State.Should().Be(LotExpiryState.Ok);
        TraceabilityRules.ExpiryLabel(LotExpiryState.Expiring, 12).Should().Be("Vence em 12 dias");
        TraceabilityRules.ExpiryLabel(LotExpiryState.Expiring, 1).Should().Be("Vence em 1 dia");
        TraceabilityRules.ExpiryLabel(LotExpiryState.Expiring, 0).Should().Be("Vence hoje");
        TraceabilityRules.ExpiryLabel(LotExpiryState.Expired, -3).Should().Be("Vencido");
        TraceabilityRules.ExpiryLabel(LotExpiryState.Ok, 90).Should().BeNull();
    }

    [Theory]
    [InlineData("ELV-0001-2026-00001", true)]
    [InlineData("CAB-2026-00012", true)]
    [InlineData("L2026-01", false)]
    [InlineData("LOTE-ABC", false)]
    public void Busca_reconhece_o_formato_da_serie(string term, bool serial) => TraceabilityRules.LooksLikeSerial(term).Should().Be(serial);

    [Fact]
    public void Termo_da_busca_e_aparado_e_limitado()
    {
        TraceabilityRules.Term("  L-01 ").Should().Be(("L-01", (string?)null));
        TraceabilityRules.Term("   ").Should().Be(((string?)null, (string?)null));
        TraceabilityRules.Term(new string('x', 61)).Error.Should().Be("Digite até 60 caracteres.");
        TraceabilityRules.Term("%_' OR 1=1 --").Term.Should().Be("%_' OR 1=1 --", "CA 10: o termo vai como parâmetro, sem tratamento de SQL aqui");
    }

    [Fact]
    public void CSV_do_lote_leva_o_cliente_mascarado_as_series_e_as_outras_linhas_sem_documento()
    {
        var pf = CustomerNameMasker.Customer(Party(PartyKind.PF, "João Marcus Ribeiro"), canViewParties: false);
        var trace = new LotTrace(
            Guid.CreateVersion7(), "L-77", Guid.CreateVersion7(), "CABO-1", "Cabo de aço", "M", new DateOnly(2027, 1, 31), LotExpiryState.Ok, 99, Guid.CreateVersion7(), "Aços Minas Ltda", T0,
            [new TraceEntry(Guid.CreateVersion7(), StockSources.GoodsReceipt, Guid.CreateVersion7(), "REC-2026-0001", "Recebimento REC-2026-0001", T0, 50m, false)], 50m,
            [new TraceLotBalance(Guid.CreateVersion7(), "Almoxarifado", 20m)], 20m,
            [
                new TraceConsumption(Guid.CreateVersion7(), "OS-2026-0001", ProductionOrderState.Completed, Guid.CreateVersion7(), "PRJ-2026-0001", "Obra Centro", pf, T0, 20m,
                    [new TraceSerial(Guid.CreateVersion7(), Guid.CreateVersion7(), "ELV-2026-00001", SerialState.Shipped, Guid.CreateVersion7(), "ROM-2026-0001", T0),
                        new TraceSerial(Guid.CreateVersion7(), Guid.CreateVersion7(), "ELV-2026-00002", SerialState.InStock, null, null, null)],
                    null, "Lotes da OS OS-2026-0001 (2 unidades): a OS não separa o material por unidade."),
                new TraceConsumption(Guid.CreateVersion7(), "OS-2025-0009", ProductionOrderState.Completed, null, null, null, null, T0, 8m, [], TraceabilityRules.NoSerialBeforeRegistry, null),
            ], 28m,
            [new TraceOtherLine(Guid.CreateVersion7(), "MOV-2026-000010", StockMovementType.Adjustment, StockSources.Adjustment, Guid.CreateVersion7(), "AJ-2026-0001", T0, -2m, "Ajuste AJ-2026-0001: =HYPERLINK(\"x\")")],
            -2m, true);

        var text = SafeCsv.Text(TraceabilityRules.CsvHeader, TraceabilityRules.CsvRows(trace));
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().StartWith("item;lote;fornecedor;validade;tipo;documento;data");
        lines.Should().HaveCount(1 + 1 + 2 + 1 + 1 + 1, "cabeçalho, entrada, uma linha por série, consumo sem série, ajuste e saldo");
        text.Should().Contain("J. M. R.", "CA 9 no CSV").And.NotContain("João").And.NotContain("12345678909", "nenhum documento");
        text.Should().Contain("ROM-2026-0001").And.Contain("Expedido").And.Contain(TraceabilityRules.NoSerialBeforeRegistry);
        text.Should().Contain("'-2", "CA 4: célula que começa com - recebe '");
        text.Should().Contain("31/01/2027");
    }
}
