using System.Globalization;
using System.Text.RegularExpressions;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Nome do cliente na rastreabilidade (EST-04 CA 9, ponto 88): pessoa jurídica pelo nome; pessoa física pelo nome só para quem tem
/// <c>parties.view</c>, e pelas iniciais ("J. M. R.") para quem não tem. Nenhum documento (CPF ou CNPJ) sai daqui: o DTO não tem o campo.
/// A regra vale no servidor, no DTO e no CSV.
/// </summary>
public static class CustomerNameMasker
{
    public const string Fallback = "Cliente";

    /// <summary>Partículas que não viram inicial ("João da Silva" → "J. S.").</summary>
    private static readonly HashSet<string> Particles = new(StringComparer.OrdinalIgnoreCase) { "da", "de", "do", "das", "dos", "e", "di", "du", "del", "van", "von" };

    /// <summary>Iniciais com ponto e espaço, em maiúscula: "João Marcus Ribeiro" → "J. M. R."; sem letra, "Cliente".</summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fallback;
        }

        var initials = name.Split([' ', '\t', '\n', '\r', '.', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Particles.Contains(w))
            .Select(w => w.FirstOrDefault(char.IsLetter))
            .Where(c => c != default)
            .Select(c => char.ToUpper(c, CultureInfo.GetCultureInfo("pt-BR")) + ".")
            .ToList();
        return initials.Count == 0 ? Fallback : string.Join(' ', initials);
    }

    /// <summary>
    /// Cliente para o DTO e o CSV: PJ pelo nome; PF pelo nome com <paramref name="canViewParties"/> e pelas iniciais sem. O id do terceiro
    /// (para o link) só vai para quem tem <c>parties.view</c>. Null se o terceiro não existe.
    /// </summary>
    public static TraceCustomer? Customer(PartySummary? party, bool canViewParties)
    {
        if (party is null)
        {
            return null;
        }

        var masked = party.Kind == PartyKind.PF && !canViewParties;
        return new TraceCustomer(canViewParties ? party.Id : null, masked ? Initials(party.LegalName) : party.LegalName, party.Kind, masked);
    }
}

/// <summary>Situação da validade do lote (CA 5): sem validade, em dia, vence em até 30 dias (alerta) ou vencido (crítico).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<LotExpiryState>))]
public enum LotExpiryState
{
    None,
    Ok,
    Expiring,
    Expired,
}

/// <summary>Uma linha da razão do lote (os movimentos com o <c>lot_id</c>), já lida do banco, para a conciliação sem banco (CA 3).</summary>
public sealed record LotLedgerLine(
    Guid MovementId, string Number, StockMovementType Type, string SourceType, Guid SourceId, string? SourceNumber, Guid? ProductionOrderId, Guid? ReversesMovementId,
    decimal Quantity, DateTimeOffset At, string? Reason);

/// <summary>Consumo do lote por uma OS: o líquido das baixas menos as devoluções (e os estornos) e a última data.</summary>
public sealed record LotOrderConsumption(Guid ProductionOrderId, decimal Quantity, DateTimeOffset At);

/// <summary>
/// Conciliação do lote (CA 3): recebido (entradas, saldo inicial e produção, com os estornos) − consumido pelas OS + outras linhas (ajuste,
/// saída avulsa, expedição, inventário, transferência que não fecha) = saldo atual. As outras linhas explicam a diferença, uma a uma.
/// </summary>
public sealed record LotReconciliation(
    IReadOnlyList<LotLedgerLine> Entries, decimal TotalReceived, IReadOnlyList<LotOrderConsumption> Consumptions, decimal TotalConsumed, IReadOnlyList<TraceOtherLine> Others,
    decimal TotalOther, decimal OnHand, bool Balanced, IReadOnlySet<Guid> ReversedIds)
{
    /// <summary>Recebido − consumido + outras: o saldo que os movimentos explicam.</summary>
    public decimal Explained => TotalReceived - TotalConsumed + TotalOther;
}

/// <summary>Regras da rastreabilidade sem banco (EST-04): busca, notas, validade, conciliação e o CSV.</summary>
public static partial class TraceabilityRules
{
    public const int MaxResults = 50;
    public const int MaxTermLength = 60;
    public const int ExpiringDays = 30;
    public const string NoSerialBeforeRegistry = "Sem série: OS concluída antes do registro de séries.";
    public const string NoSerialProduct = "Sem série: o produto não controla série.";
    public const string NoSerialOpenOrder = "Sem série ainda: a OS não foi concluída.";
    public const string NoSerialReversed = "Sem série: a conclusão da OS foi estornada.";

    private static readonly StockMovementType[] EntryTypes = [StockMovementType.Receipt, StockMovementType.OpeningBalance, StockMovementType.Production];

    [GeneratedRegex(@"^.+-\d{4}-\d+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SerialPattern();

    /// <summary>Termo da busca (CA 10): sem espaços nas pontas; vazio = null; mais de 60 caracteres é recusado (null + erro).</summary>
    public static (string? Term, string? Error) Term(string? q)
    {
        var term = q?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            return (null, null);
        }

        return term.Length > MaxTermLength ? (null, $"Digite até {MaxTermLength} caracteres.") : (term, null);
    }

    /// <summary>A busca reconhece a série pelo formato D15 <c>{código}-{AAAA}-{seq}</c> (propositivo); o resto é tratado como lote.</summary>
    public static bool LooksLikeSerial(string term) => SerialPattern().IsMatch(term);

    /// <summary>CA 2 (ponto 87): a série herda os lotes da OS inteira quando a OS tem mais de uma unidade.</summary>
    public static string? SharedLotsNote(string? orderNumber, int units) =>
        units > 1 ? $"Lotes da OS {orderNumber ?? "de origem"} ({units} unidades): a OS não separa o material por unidade." : null;

    /// <summary>Por que o consumo não tem série (CA 3): OS concluída antes das séries, produto sem série, OS aberta ou conclusão estornada.</summary>
    public static string? NoSerialNote(int serials, ProductionOrderState? status, bool hasEntry, bool entryReversed, bool tracksSerial)
    {
        if (serials > 0)
        {
            return null;
        }

        if (status is not ProductionOrderState.Completed)
        {
            return hasEntry && entryReversed ? NoSerialReversed : NoSerialOpenOrder;
        }

        if (!hasEntry)
        {
            return NoSerialBeforeRegistry;
        }

        return tracksSerial ? NoSerialBeforeRegistry : NoSerialProduct;
    }

    /// <summary>Validade (CA 5): vencido antes de hoje; vence em até 30 dias (alerta, inclusive hoje); depois disso em dia.</summary>
    public static (LotExpiryState State, int? Days) Expiry(DateOnly? expiry, DateOnly today)
    {
        if (expiry is not { } date)
        {
            return (LotExpiryState.None, null);
        }

        var days = date.DayNumber - today.DayNumber;
        return days < 0 ? (LotExpiryState.Expired, days) : days <= ExpiringDays ? (LotExpiryState.Expiring, days) : (LotExpiryState.Ok, days);
    }

    /// <summary>Texto do chip da validade: "Vencido", "Vence hoje", "Vence em 1 dia", "Vence em {n} dias"; null em dia ou sem validade.</summary>
    public static string? ExpiryLabel(LotExpiryState state, int? days) => state switch
    {
        LotExpiryState.Expired => "Vencido",
        LotExpiryState.Expiring when days == 0 => "Vence hoje",
        LotExpiryState.Expiring when days == 1 => "Vence em 1 dia",
        LotExpiryState.Expiring => $"Vence em {days} dias",
        _ => null,
    };

    /// <summary>Rótulo da entrada do lote: recebimento, entrada manual, saldo inicial ou produção, com o número do documento.</summary>
    public static string EntryLabel(string sourceType, StockMovementType type, string? number)
    {
        var label = type switch
        {
            StockMovementType.OpeningBalance => "Saldo inicial",
            StockMovementType.Production => "Produção",
            _ => sourceType switch
            {
                StockSources.GoodsReceipt => "Recebimento",
                StockSources.ManualPosting => "Entrada manual",
                StockSources.Adjustment => "Ajuste",
                StockSources.InventoryCount => "Inventário",
                _ => "Entrada",
            },
        };
        return number is null ? label : $"{label} {number}";
    }

    /// <summary>Explicação de uma linha que não é entrada nem consumo de OS (CA 3, "linha a linha").</summary>
    public static string OtherLabel(LotLedgerLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var number = line.SourceNumber ?? line.Number;
        var label = line.Type switch
        {
            StockMovementType.Adjustment => $"Ajuste {number}",
            StockMovementType.Shipment => $"Expedição {number}",
            StockMovementType.Transfer => $"Transferência {number}",
            StockMovementType.Issue when line.SourceType == StockSources.ManualPosting => $"Saída avulsa {number}",
            StockMovementType.Issue => $"Saída {number}",
            StockMovementType.Return => $"Devolução {number}",
            _ when line.SourceType == StockSources.InventoryCount => $"Inventário {number}",
            _ => $"Movimento {number}",
        };
        if (line.ReversesMovementId is not null)
        {
            label = "Estorno: " + label;
        }

        return string.IsNullOrWhiteSpace(line.Reason) ? label : $"{label}: {line.Reason}";
    }

    /// <summary>Monta a conciliação do lote a partir da razão e do saldo atual (Σ <c>stock_balances</c> do lote).</summary>
    public static LotReconciliation Reconcile(IReadOnlyList<LotLedgerLine> ledger, decimal onHand)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var reversed = ledger.Where(l => l.ReversesMovementId is not null).Select(l => l.ReversesMovementId!.Value).ToHashSet();
        var entries = ledger.Where(l => EntryTypes.Contains(l.Type)).ToList();
        var consumption = ledger.Where(IsOrderConsumption).ToList();
        var transfers = ledger.Where(l => l.Type == StockMovementType.Transfer).GroupBy(l => l.SourceId).Where(g => g.Sum(l => l.Quantity) != 0).SelectMany(g => g).ToList();
        var others = ledger.Where(l => !EntryTypes.Contains(l.Type) && !IsOrderConsumption(l) && l.Type != StockMovementType.Transfer).Concat(transfers)
            .OrderBy(l => l.At).ThenBy(l => l.Number, StringComparer.Ordinal).ToList();
        var orders = consumption.GroupBy(l => l.ProductionOrderId!.Value)
            .Select(g => new LotOrderConsumption(g.Key, -g.Sum(l => l.Quantity), g.Max(l => l.At)))
            .Where(c => c.Quantity != 0).OrderBy(c => c.At).ToList();
        var totalReceived = entries.Sum(l => l.Quantity);
        var totalConsumed = -consumption.Sum(l => l.Quantity);
        var totalOther = others.Sum(l => l.Quantity);
        var otherLines = others.Select(l => new TraceOtherLine(l.MovementId, l.Number, l.Type, l.SourceType, l.SourceId, l.SourceNumber, l.At, l.Quantity, OtherLabel(l))).ToList();
        return new LotReconciliation(
            entries.Where(l => l.ReversesMovementId is null).OrderBy(l => l.At).ToList(), totalReceived, orders, totalConsumed, otherLines, totalOther, onHand,
            totalReceived - totalConsumed + totalOther == onHand, reversed);
    }

    private static bool IsOrderConsumption(LotLedgerLine l) => l.ProductionOrderId is not null && l.Type is StockMovementType.Issue or StockMovementType.Return;

    /// <summary>Cabeçalho do CSV do destino do lote (CA 4).</summary>
    public static readonly IReadOnlyList<string> CsvHeader =
        ["item", "lote", "fornecedor", "validade", "tipo", "documento", "data", "quantidade", "unidade", "os", "projeto", "cliente", "serie", "situacao_serie", "romaneio", "observacao"];

    /// <summary>
    /// Linhas do CSV (CA 4): as entradas, cada consumo por OS com uma linha por série (ou a nota de sem série), as outras linhas da
    /// conciliação e o saldo atual por local. O cliente já vem mascarado do <see cref="CustomerNameMasker"/>; nenhum documento entra.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string?>> CsvRows(LotTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var item = $"{trace.ItemCode} {trace.ItemName}";
        var expiry = trace.Expiry?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var rows = new List<IReadOnlyList<string?>>();
        string?[] Row(string kind, string? document, DateTimeOffset? at, decimal? quantity, string? order = null, string? project = null, string? customer = null, string? serial = null,
            string? serialStatus = null, string? shipment = null, string? note = null) =>
            [item, trace.LotNumber, trace.SupplierName, expiry, kind, document, at is { } d ? Local(d) : null, quantity is { } q ? Quantity(q) : null, trace.UnitCode, order, project, customer, serial,
                serialStatus, shipment, note];

        foreach (var entry in trace.Entries)
        {
            rows.Add(Row("Entrada", entry.Label, entry.At, entry.Quantity, note: entry.Reversed ? "Estornada" : null));
        }

        foreach (var use in trace.Consumptions)
        {
            var project = use.ProjectNumber is null ? null : $"{use.ProjectNumber} {use.ProjectName}".Trim();
            if (use.Serials.Count == 0)
            {
                rows.Add(Row("Consumo", use.OrderNumber, use.At, use.Quantity, use.OrderNumber, project, use.Customer?.Name, note: use.NoSerialNote));
                continue;
            }

            foreach (var serial in use.Serials)
            {
                rows.Add(Row("Consumo", use.OrderNumber, use.At, use.Quantity, use.OrderNumber, project, use.Customer?.Name, serial.SerialNumber, SerialStatus(serial.Status), serial.ShipmentNumber,
                    use.SharedLotsNote));
            }
        }

        foreach (var other in trace.Others)
        {
            rows.Add(Row("Outra saída ou ajuste", other.SourceNumber ?? other.Number, other.At, other.Quantity, note: other.Explanation));
        }

        foreach (var balance in trace.Balances)
        {
            rows.Add(Row("Saldo atual", balance.LocationName, null, balance.OnHand));
        }

        return rows;
    }

    public static string SerialStatus(SerialState status) => status switch
    {
        SerialState.InStock => "Em estoque",
        SerialState.Shipped => "Expedido",
        _ => "Cancelada",
    };

    /// <summary>Nome do arquivo: <c>rastreio-lote-{número}.csv</c>, só com letras, números, ponto e hífen.</summary>
    public static string CsvFileName(string lotNumber)
    {
        ArgumentNullException.ThrowIfNull(lotNumber);
        var safe = new string(lotNumber.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' ? c : '_').ToArray());
        return $"rastreio-lote-{safe}.csv";
    }

    private static string Quantity(decimal value) => value.ToString("0.####", CultureInfo.GetCultureInfo("pt-BR"));

    private static string Local(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, BemAqui.BuildingBlocks.Time.BusinessTime.Zone).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
