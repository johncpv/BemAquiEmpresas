using System.Globalization;

namespace BemAqui.Inventory.Domain;

/// <summary>Saldo de uma linha do ajuste no momento da conta: físico e reservado da posição (item, local) e o físico do lote.</summary>
public sealed record AdjustmentBalance(decimal PositionOnHand, decimal PositionReserved, decimal LotOnHand);

/// <summary>Uma linha para a conta do ajuste: item, lote e o que foi enviado (contado no modo Counted, diferença no modo Delta).</summary>
public sealed record AdjustmentLineInput(Guid ItemId, Guid? LotId, decimal? Counted, decimal Delta);

/// <summary>Resultado da conta da linha: o saldo do sistema usado, a diferença e o saldo do lote e da posição depois dela.</summary>
public sealed record AdjustmentLineResult(decimal SystemQty, decimal Delta, decimal LotAfter, decimal PositionAfter, decimal Reserved);

/// <summary>Recusa de uma linha (CA 4 e 7): o índice dela no documento e a mensagem.</summary>
public sealed record AdjustmentRefusal(int Index, string Message, decimal After, decimal Reserved);

/// <summary>Regras do ajuste de estoque (EST-01b), sem banco: a diferença nos dois modos, o recálculo da aprovação e o reservado.</summary>
public static class StockAdjustmentRules
{
    public const string DocType = "AJ";
    public const int MaxReasonDetail = 200;
    public const int MaxComment = 500;
    public const int MaxLines = 200;

    /// <summary>
    /// Tipo de documento do ajuste no mapa tipo → permissão do aprovador do motor de alçadas (CA 14, ADR 0024 §4): todo ajuste com
    /// <c>inventory.approve</c>, sem faixa de valor, sem regra ativa e sem chamada ao motor nesta story.
    /// </summary>
    public const string ApprovalDocumentType = "StockAdjustment";

    public const string ApproverPermission = "inventory.approve";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static decimal Round(decimal quantity) => Math.Round(quantity, 4, MidpointRounding.AwayFromZero);

    public static string Quantity(decimal value, string unit) => value.ToString("N4", PtBr) + (string.IsNullOrEmpty(unit) ? string.Empty : " " + unit);

    /// <summary>Diferença da linha (CA 2 e 7): no modo Counted, contado − saldo do sistema de agora; no modo Delta, a diferença enviada.</summary>
    public static decimal Delta(StockAdjustmentMode mode, decimal systemQty, decimal? counted, decimal delta) =>
        mode == StockAdjustmentMode.Counted ? Round((counted ?? 0m) - systemQty) : Round(delta);

    /// <summary>Valor da diferença da linha: |Δ| × custo médio, 2 casas <c>AwayFromZero</c>.</summary>
    public static decimal Value(decimal delta, decimal unitCost) => AverageCostCalculator.LineTotal(Math.Abs(delta), unitCost);

    /// <summary>
    /// Conta das linhas sobre os saldos dados (o do envio ou o de agora): a diferença de cada uma, o lote depois dela e a posição do item
    /// depois de todas as linhas do mesmo item. A primeira linha que deixaria o lote negativo, ou a posição abaixo do reservado, é a
    /// recusa (CA 4): "O saldo ficaria {x}, abaixo do reservado ({y}) para OS.". Com <paramref name="releasesReservations"/> (o ajuste
    /// do inventário, EST-05), ficar abaixo do reservado não é recusa: a aprovação reduz antes as reservas das OS (decisão do PO de
    /// 23/10/2026); o lote negativo continua recusado.
    /// </summary>
    public static (IReadOnlyList<AdjustmentLineResult> Lines, AdjustmentRefusal? Refusal) Evaluate(
        StockAdjustmentMode mode, IReadOnlyList<AdjustmentLineInput> lines, IReadOnlyList<AdjustmentBalance> balances, IReadOnlyList<string> units,
        bool releasesReservations = false)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(balances);
        ArgumentNullException.ThrowIfNull(units);
        var results = new List<AdjustmentLineResult>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var system = balances[i].LotOnHand;
            var delta = Delta(mode, system, lines[i].Counted, lines[i].Delta);
            results.Add(new AdjustmentLineResult(system, delta, Round(system + delta), 0m, balances[i].PositionReserved));
        }

        // A posição soma todas as linhas do item (vários lotes no mesmo local).
        AdjustmentRefusal? refusal = null;
        foreach (var group in Enumerable.Range(0, lines.Count).GroupBy(i => lines[i].ItemId))
        {
            var first = group.First();
            var after = Round(balances[first].PositionOnHand + group.Sum(i => results[i].Delta));
            foreach (var i in group)
            {
                results[i] = results[i] with { PositionAfter = after };
            }
        }

        for (var i = 0; i < lines.Count && refusal is null; i++)
        {
            var r = results[i];
            if (r.Delta >= 0)
            {
                continue;
            }

            if (r.LotAfter < 0)
            {
                refusal = new AdjustmentRefusal(i, $"O saldo do lote ficaria {Quantity(r.LotAfter, units[i])}, abaixo de zero.", r.LotAfter, r.Reserved);
            }
            else if (!releasesReservations && r.PositionAfter < r.Reserved)
            {
                refusal = new AdjustmentRefusal(i, $"O saldo ficaria {Quantity(r.PositionAfter, units[i])}, abaixo do reservado ({Quantity(r.Reserved, units[i])}) para OS.", r.PositionAfter, r.Reserved);
            }
        }

        return (results, refusal);
    }

    /// <summary>Sobras e faltas em R$ (2 casas por linha), sobre as diferenças e os custos dados.</summary>
    public static (decimal Surplus, decimal Shortage) Totals(IEnumerable<(decimal Delta, decimal UnitCost)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        decimal surplus = 0m, shortage = 0m;
        foreach (var (delta, cost) in lines)
        {
            if (delta > 0)
            {
                surplus += Value(delta, cost);
            }
            else if (delta < 0)
            {
                shortage += Value(delta, cost);
            }
        }

        return (surplus, shortage);
    }

    /// <summary>
    /// Quem decide (CA 8): quem lançou não aprova nem estorna o próprio ajuste, salvo o Proprietário único (<c>SoleOwnerRule</c>, que o
    /// chamador avalia e passa em <paramref name="decidingAlone"/>).
    /// </summary>
    public static bool MayDecide(Guid? author, Guid actor, bool decidingAlone) => author != actor || decidingAlone;

    /// <summary>
    /// Quem decide com os envolvidos (EST-05 CA 11): no ajuste do inventário, além do autor, quem abriu, contou ou recontou não aprova
    /// nem estorna, salvo o Proprietário único.
    /// </summary>
    public static bool MayDecide(IReadOnlyCollection<Guid> involved, Guid actor, bool decidingAlone)
    {
        ArgumentNullException.ThrowIfNull(involved);
        return !involved.Contains(actor) || decidingAlone;
    }

    /// <summary>
    /// Quanto reduzir das reservas do item no local para o ajuste do inventário caber (decisão do PO de 23/10/2026): o reservado − o
    /// físico depois de todas as linhas do item, quando positivo.
    /// </summary>
    public static decimal ReservationExcess(decimal positionAfter, decimal reserved) => positionAfter < reserved ? Round(reserved - Math.Max(0m, positionAfter)) : 0m;

    public static string ReasonLabel(StockAdjustmentReason reason) => reason switch
    {
        StockAdjustmentReason.Loss => "Perda/avaria",
        StockAdjustmentReason.EntryError => "Erro de lançamento",
        StockAdjustmentReason.Surplus => "Sobra encontrada",
        StockAdjustmentReason.Count => "Contagem",
        _ => "Outro",
    };

    public static string ModeLabel(StockAdjustmentMode mode) => mode == StockAdjustmentMode.Counted ? "Quantidade contada" : "Diferença";

    /// <summary>Motivo gravado no movimento (até 200): o rótulo e, quando houver, a descrição.</summary>
    public static string MovementReason(string number, StockAdjustmentReason reason, string? detail)
    {
        var text = $"Ajuste {number}: {ReasonLabel(reason)}" + (string.IsNullOrWhiteSpace(detail) ? string.Empty : " — " + detail.Trim());
        return text.Length <= 200 ? text : text[..199] + "…";
    }
}
