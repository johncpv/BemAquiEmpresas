using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>
/// EST-01a, regras sem banco: custo médio ponderado móvel (CA 18, 19, 21) contra uma planilha de referência, arredondamento,
/// regra de valor e de desvio da D7 (CA 9), limites do estorno pelo autor (CA 20), ordem de trava (CA 14), sentido da quantidade,
/// data do saldo inicial (ADR 0016), mensagem de saldo insuficiente (CA 13) e o número <c>MOV</c> (D15).
/// </summary>
public class StockRulesTests
{
    [Fact]
    public void Entrada_pondera_pelo_custo_e_com_fisico_zerado_vale_o_custo_da_entrada()
    {
        AverageCostCalculator.AfterEntry(10m, 5m, 10m, 7m).Should().Be(6m, "(10 × 5 + 10 × 7) ÷ 20");
        AverageCostCalculator.AfterEntry(0m, 5m, 3m, 9.5m).Should().Be(9.5m, "CA 19: físico zerado, o médio é o custo da entrada");
        AverageCostCalculator.AfterEntry(-1m, 5m, 3m, 9.5m).Should().Be(9.5m);
        AverageCostCalculator.Next(StockMovementType.Receipt, 3m, 10m, 1m, 11m, reversesEntry: false).Should().Be(10.25m);
        AverageCostCalculator.Next(StockMovementType.Receipt, 3m, 1m, 6m, 2m, reversesEntry: false).Should().Be(1.666667m, "(3 + 12) ÷ 9, 6 casas AwayFromZero");
    }

    [Fact]
    public void Saida_transferencia_e_ajuste_negativo_nunca_mudam_o_medio()
    {
        AverageCostCalculator.Next(StockMovementType.Issue, 10m, 4.123456m, -3m, 4.123456m, reversesEntry: false).Should().Be(4.123456m);
        AverageCostCalculator.Next(StockMovementType.Transfer, 10m, 4m, -3m, 4m, reversesEntry: false).Should().Be(4m);
        AverageCostCalculator.Next(StockMovementType.Transfer, 7m, 4m, 3m, 99m, reversesEntry: false).Should().Be(4m, "a perna de entrada da transferência não pondera");
        AverageCostCalculator.Next(StockMovementType.Adjustment, 10m, 4m, -1m, 4m, reversesEntry: false).Should().Be(4m);
    }

    [Fact]
    public void Estorno_de_entrada_consumida_protege_o_medio()
    {
        // 10 a R$ 5 + 10 a R$ 7 = médio 6; saem 15; estorno da segunda entrada (10 a R$ 7) com só 5 no físico.
        AverageCostCalculator.AfterEntryReversal(5m, 6m, 10m, 7m).Should().Be(6m, "o físico restante seria negativo: fica o último médio válido");
        AverageCostCalculator.AfterEntryReversal(10m, 6m, 10m, 7m).Should().Be(6m, "o físico restante zera: fica o último médio");
        AverageCostCalculator.AfterEntryReversal(20m, 6m, 10m, 7m).Should().Be(5m, "(20 × 6 − 10 × 7) ÷ 10");
        AverageCostCalculator.AfterEntryReversal(12m, 2m, 10m, 7m).Should().Be(2m, "(24 − 70) ÷ 2 é negativo: fica o último médio");
        AverageCostCalculator.Next(StockMovementType.Receipt, 20m, 6m, -10m, 7m, reversesEntry: true).Should().Be(5m);
    }

    [Fact]
    public void Estorno_de_saida_volta_pelo_custo_da_saida_original()
    {
        AverageCostCalculator.Next(StockMovementType.Issue, 5m, 8m, 5m, 6m, reversesEntry: false).Should().Be(7m, "(5 × 8 + 5 × 6) ÷ 10");
    }

    [Fact]
    public void Total_da_linha_em_2_casas_e_custo_em_6_casas_AwayFromZero()
    {
        AverageCostCalculator.LineTotal(3m, 0.335m).Should().Be(1.01m, "1,005 arredonda para cima, não para o par");
        AverageCostCalculator.LineTotal(-3m, 0.335m).Should().Be(-1.01m);
        AverageCostCalculator.RoundCost(1.0000005m).Should().Be(1.000001m);
        AverageCostCalculator.RoundCost(2.0000004m).Should().Be(2m);
    }

    [Fact]
    public void Sentido_da_quantidade_por_tipo()
    {
        AverageCostCalculator.SignAllowed(StockMovementType.Receipt, 1m).Should().BeTrue();
        AverageCostCalculator.SignAllowed(StockMovementType.Receipt, -1m).Should().BeFalse();
        AverageCostCalculator.SignAllowed(StockMovementType.OpeningBalance, -1m).Should().BeFalse();
        AverageCostCalculator.SignAllowed(StockMovementType.Issue, -1m).Should().BeTrue();
        AverageCostCalculator.SignAllowed(StockMovementType.Issue, 1m).Should().BeFalse();
        AverageCostCalculator.SignAllowed(StockMovementType.Transfer, -1m).Should().BeTrue();
        AverageCostCalculator.SignAllowed(StockMovementType.Adjustment, 1m).Should().BeTrue();
        AverageCostCalculator.SignAllowed(StockMovementType.Receipt, 0m).Should().BeFalse();
    }

    /// <summary>CA 18: 30 movimentos (entradas, saídas, transferências e estornos, inclusive de entrada consumida) contra a planilha.</summary>
    [Fact]
    public void Dataset_de_30_movimentos_bate_com_a_planilha_de_referencia()
    {
        var engine = new EngineModel();
        var sheet = new SpreadsheetModel();
        foreach (var step in StockDataset.Steps)
        {
            engine.Apply(step);
            sheet.Apply(step);
            engine.Average.Should().Be(sheet.Average, $"passo {step.Number}: {step.Kind}");
            engine.OnHand.Should().Be(sheet.OnHand, $"passo {step.Number}");
        }

        StockDataset.Steps.Should().HaveCount(30);
        StockDataset.Steps.Should().Contain(s => s.Kind == StepKind.ReverseEntry && s.Consumed, "inclui estorno de entrada já consumida");
        engine.Average.Should().Be(StockDataset.ExpectedFinalAverage);
        engine.OnHand.Should().Be(StockDataset.ExpectedFinalOnHand);
    }

    [Theory]
    [InlineData(ManualPostingKind.Receipt, "1000", "10", "10", null)]
    [InlineData(ManualPostingKind.Receipt, "1000.01", "10", "10", "Acima de R$ 1.000,00")]
    [InlineData(ManualPostingKind.Issue, "1500", null, "10", "Acima de R$ 1.000,00")]
    [InlineData(ManualPostingKind.Issue, "900", null, "10", null)]
    [InlineData(ManualPostingKind.Receipt, "100", "12.5", "10", "Custo 25% acima do médio")]
    [InlineData(ManualPostingKind.Receipt, "100", "7.5", "10", "Custo 25% abaixo do médio")]
    [InlineData(ManualPostingKind.Receipt, "100", "12", "10", null)]
    [InlineData(ManualPostingKind.Receipt, "100", "50", "0", null)]
    [InlineData(ManualPostingKind.Receipt, "2000", "13", "10", "Acima de R$ 1.000,00 · Custo 30% acima do médio")]
    [InlineData(ManualPostingKind.Transfer, "50000", null, "10", null)]
    [InlineData(ManualPostingKind.OpeningBalance, "50000", "99", "10", null)]
    public void Regra_de_valor_e_desvio_da_D7(ManualPostingKind kind, string value, string? cost, string average, string? expected) =>
        ManualPostingRules.ApprovalReason(kind, Dec(value), cost is null ? null : Dec(cost), Dec(average), 1000m, 20m).Should().Be(expected);

    private static decimal Dec(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Autor_estorna_ate_o_limite_e_em_24_horas()
    {
        var posted = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        ManualPostingRules.AuthorMayReverse(1000m, posted, posted.AddHours(24), 1000m).Should().BeTrue();
        ManualPostingRules.AuthorMayReverse(-1000m, posted, posted.AddHours(1), 1000m).Should().BeTrue("vale o total absoluto");
        ManualPostingRules.AuthorMayReverse(1000.01m, posted, posted.AddHours(1), 1000m).Should().BeFalse();
        ManualPostingRules.AuthorMayReverse(10m, posted, posted.AddHours(24).AddSeconds(1), 1000m).Should().BeFalse();
    }

    [Fact]
    public void Ordem_de_trava_por_item_crescente_e_na_ordem_do_documento()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var b = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        StockPosting Leg(Guid item, int leg) => new(StockMovementType.Issue, item, Guid.NewGuid(), null, -1m, null, StockSources.Requisition, Guid.NewGuid(), leg);
        var postings = new[] { Leg(b, 1), Leg(a, 2), Leg(b, 3), Leg(a, 4) };
        StockLedger.PostingOrder(postings).Should().Equal(1, 3, 0, 2);
        var reversed = new[] { Leg(a, 1), Leg(b, 2) };
        var inverse = new[] { Leg(b, 1), Leg(a, 2) };
        StockLedger.PostingOrder(reversed).Select(i => reversed[i].ItemId).Should().Equal(new[] { a, b }, "dois documentos com os mesmos itens travam na mesma ordem");
        StockLedger.PostingOrder(inverse).Select(i => inverse[i].ItemId).Should().Equal(new[] { a, b }, "mesmo com os itens em ordem inversa no documento");
    }

    [Fact]
    public void Saldo_inicial_fica_em_D_23_59_59_de_Sao_Paulo()
    {
        ManualPostingService.OpeningAt(new DateOnly(2026, 9, 30)).Should().Be(new DateTimeOffset(2026, 10, 1, 2, 59, 59, TimeSpan.Zero));
    }

    [Fact]
    public void Mensagem_de_saldo_insuficiente_com_4_casas()
    {
        var error = StockLedger.Insufficient(5m, "UN", "Chapa 3 mm", "Almoxarifado principal");
        error.Code.Should().Be("insufficient_stock");
        error.Message.Should().Be("Saldo insuficiente: há 5,0000 UN de Chapa 3 mm em Almoxarifado principal.");
    }

    [Fact]
    public void Numero_MOV_com_6_digitos_e_prefixos_unicos()
    {
        NumberFormats.Defaults["MOV"].Format(2026, 1).Should().Be("MOV-2026-000001");
        NumberFormats.Defaults.Values.Select(f => f.Prefix).Should().OnlyHaveUniqueItems("D15: prefixos únicos no tenant");
        NumberFormats.Labels.Should().ContainKey("MOV");
    }

    [Fact]
    public void Dinheiro_em_pt_BR() => ManualPostingRules.Money(1234.5m).Should().Be("R$ 1.234,50");

    /// <summary>O motor: aplica o <see cref="AverageCostCalculator"/> como o <c>StockLedger</c> (médio gravado em 6 casas a cada passo).</summary>
    private sealed class EngineModel
    {
        public decimal OnHand { get; private set; }

        public decimal Average { get; private set; }

        private readonly List<(decimal Quantity, decimal Cost)> _entries = [];

        public void Apply(StockStep step)
        {
            switch (step.Kind)
            {
                case StepKind.Receipt:
                    Average = AverageCostCalculator.Next(StockMovementType.Receipt, OnHand, Average, step.Quantity, step.Cost, false);
                    OnHand += step.Quantity;
                    _entries.Add((step.Quantity, step.Cost));
                    break;
                case StepKind.Issue:
                case StepKind.Transfer:
                    Average = AverageCostCalculator.Next(step.Kind == StepKind.Issue ? StockMovementType.Issue : StockMovementType.Transfer, OnHand, Average, -step.Quantity, Average, false);
                    OnHand -= step.Kind == StepKind.Issue ? step.Quantity : 0m;
                    _entries.Add((-step.Quantity, Average));
                    break;
                case StepKind.ReverseEntry:
                    var entry = _entries[step.Of];
                    Average = AverageCostCalculator.Next(StockMovementType.Receipt, OnHand, Average, -entry.Quantity, entry.Cost, true);
                    OnHand -= entry.Quantity;
                    _entries.Add((0m, 0m));
                    break;
                default:
                    var issue = _entries[step.Of];
                    Average = AverageCostCalculator.Next(StockMovementType.Issue, OnHand, Average, -issue.Quantity, issue.Cost, false);
                    OnHand -= issue.Quantity;
                    _entries.Add((0m, 0m));
                    break;
            }
        }
    }

    /// <summary>A planilha: valor do estoque e quantidade, escrita à parte do motor, com as mesmas regras do <c>04-fluxos</c> §3.1.</summary>
    private sealed class SpreadsheetModel
    {
        public decimal OnHand { get; private set; }

        public decimal Average { get; private set; }

        private readonly List<(decimal Quantity, decimal Cost)> _lines = [];

        public void Apply(StockStep step)
        {
            if (step.Kind == StepKind.Receipt)
            {
                var value = OnHand > 0 ? OnHand * Average + step.Quantity * step.Cost : step.Quantity * step.Cost;
                var quantity = OnHand > 0 ? OnHand + step.Quantity : step.Quantity;
                Average = Math.Round(value / quantity, 6, MidpointRounding.AwayFromZero);
                OnHand += step.Quantity;
                _lines.Add((step.Quantity, step.Cost));
            }
            else if (step.Kind is StepKind.Issue or StepKind.Transfer)
            {
                _lines.Add((-step.Quantity, Average));
                if (step.Kind == StepKind.Issue)
                {
                    OnHand -= step.Quantity;
                }
            }
            else if (step.Kind == StepKind.ReverseEntry)
            {
                var (quantity, cost) = _lines[step.Of];
                var left = OnHand - quantity;
                var candidate = left > 0 ? (OnHand * Average - quantity * cost) / left : 0m;
                Average = left > 0 && candidate > 0 ? Math.Round(candidate, 6, MidpointRounding.AwayFromZero) : Average;
                OnHand = left;
                _lines.Add((0m, 0m));
            }
            else
            {
                var (quantity, cost) = _lines[step.Of];
                var back = -quantity;
                Average = Math.Round(OnHand > 0 ? (OnHand * Average + back * cost) / (OnHand + back) : cost, 6, MidpointRounding.AwayFromZero);
                OnHand += back;
                _lines.Add((0m, 0m));
            }
        }
    }
}

public enum StepKind
{
    Receipt,
    Issue,
    Transfer,
    ReverseEntry,
    ReverseIssue,
}

/// <summary>Um passo do dataset: <c>Of</c> é o índice (0 a 29) do movimento estornado; <c>Consumed</c> marca a entrada já consumida.</summary>
public sealed record StockStep(int Number, StepKind Kind, decimal Quantity = 0m, decimal Cost = 0m, int Of = -1, bool Consumed = false);

/// <summary>Dataset fixo de 30 movimentos (CA 18), usado também pelo teste de integração do motor (planilha de referência).</summary>
public static class StockDataset
{
    public static readonly IReadOnlyList<StockStep> Steps =
    [
        new(1, StepKind.Receipt, 100m, 10m),
        new(2, StepKind.Receipt, 50m, 13m),
        new(3, StepKind.Issue, 30m),
        new(4, StepKind.Transfer, 20m),
        new(5, StepKind.Receipt, 10m, 12.345678m),
        new(6, StepKind.Issue, 45m),
        new(7, StepKind.ReverseIssue, Of: 5),
        new(8, StepKind.Receipt, 25m, 9.99m),
        new(9, StepKind.Issue, 100m),
        new(10, StepKind.ReverseEntry, Of: 7, Consumed: true),
        new(11, StepKind.Receipt, 7.5m, 11.111111m),
        new(12, StepKind.Transfer, 5m),
        new(13, StepKind.Issue, 12.25m),
        new(14, StepKind.Receipt, 40m, 10.5m),
        new(15, StepKind.Issue, 3m),
        new(16, StepKind.ReverseIssue, Of: 14),
        new(17, StepKind.Receipt, 1m, 99.99m),
        new(18, StepKind.ReverseEntry, Of: 16),
        new(19, StepKind.Issue, 20m),
        new(20, StepKind.Receipt, 33.3333m, 8.765432m),
        new(21, StepKind.Transfer, 10m),
        new(22, StepKind.Issue, 15m),
        new(23, StepKind.Receipt, 12m, 14.2m),
        new(24, StepKind.ReverseEntry, Of: 19),
        new(25, StepKind.Issue, 5.0001m),
        new(26, StepKind.Receipt, 60m, 10.01m),
        new(27, StepKind.ReverseIssue, Of: 24),
        new(28, StepKind.Issue, 50m),
        new(29, StepKind.Receipt, 5m, 15m),
        new(30, StepKind.ReverseEntry, Of: 28),
    ];

    /// <summary>Resultado da planilha (conferido à mão nos passos 1, 2, 5 e 10).</summary>
    public static decimal ExpectedFinalAverage => Final.Average;

    public static decimal ExpectedFinalOnHand => Final.OnHand;

    private static (decimal Average, decimal OnHand) Final
    {
        get
        {
            // 1: 100 a 10 → médio 10. 2: (100 × 10 + 50 × 13) ÷ 150 = 11. 3 e 4: saída e transferência, médio 11, físico 120.
            // 5: (120 × 11 + 10 × 12,345678) ÷ 130 = 11,103514. O restante segue a mesma regra; o teste confere passo a passo.
            var onHand = 0m;
            var average = 0m;
            var lines = new List<(decimal Quantity, decimal Cost)>();
            foreach (var step in Steps)
            {
                switch (step.Kind)
                {
                    case StepKind.Receipt:
                        average = Math.Round(onHand > 0 ? (onHand * average + step.Quantity * step.Cost) / (onHand + step.Quantity) : step.Cost, 6, MidpointRounding.AwayFromZero);
                        onHand += step.Quantity;
                        lines.Add((step.Quantity, step.Cost));
                        break;
                    case StepKind.Issue:
                        onHand -= step.Quantity;
                        lines.Add((-step.Quantity, average));
                        break;
                    case StepKind.Transfer:
                        lines.Add((-step.Quantity, average));
                        break;
                    case StepKind.ReverseEntry:
                        var (q, c) = lines[step.Of];
                        var left = onHand - q;
                        var candidate = left > 0 ? (onHand * average - q * c) / left : 0m;
                        average = left > 0 && candidate > 0 ? Math.Round(candidate, 6, MidpointRounding.AwayFromZero) : average;
                        onHand = left;
                        lines.Add((0m, 0m));
                        break;
                    default:
                        var (iq, ic) = lines[step.Of];
                        average = Math.Round(onHand > 0 ? (onHand * average - iq * ic) / (onHand - iq) : ic, 6, MidpointRounding.AwayFromZero);
                        onHand -= iq;
                        lines.Add((0m, 0m));
                        break;
                }
            }

            return (average, onHand);
        }
    }
}
