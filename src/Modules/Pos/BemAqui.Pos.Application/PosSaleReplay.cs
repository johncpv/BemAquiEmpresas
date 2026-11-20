using System.Globalization;
using System.Text.Json;
using BemAqui.Pos.Domain;

namespace BemAqui.Pos.Application;

/// <summary>Linha da venda refeita no servidor (quantidade em <c>decimal(18,4)</c>, preço em centavos do aparelho).</summary>
public sealed record ReplayLine(Guid LineId, Guid ProductId, string Name, decimal Quantity, long UnitCents, long? PriceVersion, string? Note);

/// <summary>Pagamento refeito pela máquina de estados do servidor (o último estado de cada <c>tenderId</c>).</summary>
public sealed record ReplayTender(
    Guid TenderId, string Status, Guid? MethodId, string? MethodType, long AmountCents, long? ReceivedCents, long ChangeCents, int? Installments, string? CardBrand, string? Nsu,
    string? AuthorizationCode, string? Note, string Integration, Guid? ConfirmedBy, DateTimeOffset At, bool IntegrationFieldsFilled);

/// <summary>Quem precisa ser conferido: o operador numa sessão por PIN do aparelho e, quando há, a permissão no instante.</summary>
public sealed record ReplayCheck(Guid? UserId, DateTimeOffset At, string Role, string? Permission, bool NeedsPinSession);

public sealed record ReplayIncident(OperatorIncidentKind Kind, Guid? OperatorId, Guid? AuthorizedBy, DateTimeOffset At);

/// <summary>A venda refeita a partir dos eventos e tudo o que a conferência achou.</summary>
public sealed class SaleReplayResult
{
    public Guid SaleId { get; init; }

    public string Number { get; init; } = string.Empty;

    public bool Completed { get; set; }

    public bool Discarded { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public Guid? CompletedBy { get; set; }

    public Guid? CashSessionId { get; init; }

    public Guid? CustomerId { get; set; }

    public Guid? DocumentId { get; set; }

    public List<ReplayLine> Lines { get; } = [];

    public List<ReplayTender> Tenders { get; } = [];

    public long TotalCents => Lines.Sum(l => PosSaleReplay.LineCents(l));

    public long CapturedCents => Tenders.Where(t => t.Status == "Captured").Sum(t => t.AmountCents);

    public long ChangeCents => Tenders.Where(t => t.Status == "Captured").Sum(t => t.ChangeCents);

    /// <summary>O que não passou na conferência: a venda vai para a quarentena ("Venda recusada na validação").</summary>
    public List<string> Violations { get; } = [];

    /// <summary>Pagamento que precisa de alguém olhar (ex.: <c>Unknown</c> sem captura): divergência, a venda entra.</summary>
    public List<string> PaymentNotes { get; } = [];

    public List<ReplayCheck> Checks { get; } = [];

    public List<ReplayIncident> Exceptions { get; } = [];
}

/// <summary>
/// Replay da venda no servidor (requisitos de segurança da PDV-02 e da PDV-03): a regra da venda roda no aparelho, que não é confiável; aqui a
/// venda é refeita a partir dos eventos, sem usar o estado nem os totais que o aparelho calculou. Função pura (os dados do servidor entram por
/// parâmetro), testada à parte. Confere: a remoção do último item (<c>last-item</c>) e o desfazer do último lançamento (<c>remove-last</c>) só para
/// o último lançamento de fato e com a remoção livre ligada na loja; redução de outra linha só com quem autorizou; total do pagamento igual ao das
/// linhas; máquina de estados por <c>tenderId</c>; troco coerente; campos de integração vazios no manual; texto sem número de cartão e dentro do
/// tamanho; operador presente em todo evento com dinheiro.
/// </summary>
public static class PosSaleReplay
{
    public const int MaxNoteLength = 140;
    public const int MaxNsuLength = 12;
    public const int MaxAuthorizationLength = 12;
    public const int MaxDiscardReasonLength = 200;

    private static readonly Dictionary<string, string[]> Transitions = new(StringComparer.Ordinal)
    {
        ["Created"] = ["Pending", "Captured", "Failed", "Unknown"],
        ["Pending"] = ["Captured", "Failed", "Unknown"],
        ["Unknown"] = ["Captured", "Failed"],
        ["Captured"] = ["Voided", "Refunded"],
        ["Failed"] = [],
        ["Voided"] = [],
        ["Refunded"] = [],
    };

    public static long LineCents(ReplayLine line) => (long)Math.Round(line.Quantity * line.UnitCents, 0, MidpointRounding.AwayFromZero);

    /// <summary>Refaz a venda. <paramref name="freeRemoveLast"/> vem da loja no servidor, nunca do aparelho.</summary>
    public static SaleReplayResult Run(JsonElement payload, bool freeRemoveLast)
    {
        var result = new SaleReplayResult
        {
            SaleId = GuidOf(payload, "saleId") ?? Guid.Empty,
            Number = StringOf(payload, "number") ?? string.Empty,
            CashSessionId = GuidOf(payload, "cashSessionId"),
        };
        if (result.SaleId == Guid.Empty)
        {
            result.Violations.Add("A venda veio sem o id.");
            return result;
        }

        var status = StringOf(payload, "status");
        if (status is not ("completed" or "discarded"))
        {
            result.Violations.Add("A venda veio sem a situação (concluída ou descartada).");
        }

        if (!payload.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array || events.GetArrayLength() == 0)
        {
            result.Violations.Add("A venda veio sem os eventos.");
            return result;
        }

        var state = new State(result, freeRemoveLast);
        var started = false;
        foreach (var e in events.EnumerateArray())
        {
            var type = StringOf(e, "type") ?? string.Empty;
            var at = DateOf(e, "at");
            if (GuidOf(e, "saleId") != result.SaleId || at is null)
            {
                result.Violations.Add($"Evento {type} sem a venda ou o instante certos.");
                continue;
            }

            var data = e.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;
            if (!started && type != "SaleStarted")
            {
                result.Violations.Add("A venda não começa pelo SaleStarted.");
            }

            started = true;
            state.Apply(type, at.Value, data);
        }

        if (status == "completed" && !result.Completed)
        {
            result.Violations.Add("A venda concluída veio sem o SaleCompleted.");
        }

        if (status == "discarded" && !result.Discarded)
        {
            result.Violations.Add("A venda descartada veio sem o SaleDiscarded.");
        }

        return result;
    }

    private sealed class State(SaleReplayResult result, bool freeRemoveLast)
    {
        private (Guid LineId, decimal Quantity)? _lastAdd;
        private bool _inPayment;

        public void Apply(string type, DateTimeOffset at, JsonElement data)
        {
            switch (type)
            {
                case "SaleStarted":
                    Operator(data, at, "quem começou a venda", permission: null);
                    break;
                case "LineAdded":
                    AddLine(data, at);
                    break;
                case "LineChanged":
                    ChangeLine(data, at);
                    break;
                case "LineRemoved":
                    RemoveLine(data, at);
                    break;
                case "SaleParked":
                case "SaleResumed":
                    break;
                case "SaleDiscarded":
                    Discard(data, at);
                    break;
                case "CustomerSet":
                    Customer(data);
                    break;
                case "PaymentStarted":
                    _inPayment = true;
                    Operator(data, at, "quem foi para o pagamento", permission: null);
                    if (LongOf(data, "totalCents") != result.TotalCents)
                    {
                        result.Violations.Add($"O total do início do pagamento ({Money(LongOf(data, "totalCents"))}) não bate com o total refeito das linhas ({Money(result.TotalCents)}).");
                    }

                    break;
                case "PaymentCancelled":
                    Operator(data, at, "quem voltou aos itens", permission: null);
                    if (result.CapturedCents > 0)
                    {
                        result.Violations.Add("Voltou aos itens com pagamento capturado.");
                    }

                    _inPayment = false;
                    break;
                case "TenderCaptured":
                case "TenderChanged":
                    Tender(type, data, at);
                    break;
                case "SaleCompleted":
                    Complete(data, at);
                    break;
                default:
                    result.Violations.Add($"Evento desconhecido na venda: {type}.");
                    break;
            }
        }

        private void AddLine(JsonElement data, DateTimeOffset at)
        {
            if (Locked("lançou item"))
            {
                return;
            }

            var line = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("line", out var l) ? l : default;
            var lineId = GuidOf(line, "lineId");
            var productId = GuidOf(line, "itemId");
            var quantity = QuantityOf(line, "quantity");
            var unit = LongOf(line, "unitCents");
            var note = StringOf(line, "note");
            if (lineId is null || productId is null || quantity is not > 0 || unit is not >= 0 || result.Lines.Any(x => x.LineId == lineId))
            {
                result.Violations.Add("Item lançado sem linha, produto, quantidade maior que zero ou preço válido.");
                return;
            }

            if (TextProblem(note, MaxNoteLength, "observação do item") is { } problem)
            {
                result.Violations.Add(problem);
                note = null;
            }

            result.Lines.Add(new ReplayLine(lineId.Value, productId.Value, StringOf(line, "name") ?? string.Empty, quantity.Value, unit.Value, LongOf(line, "priceTableVersion"), note));
            _lastAdd = (lineId.Value, quantity.Value);
            _ = at;
        }

        private void ChangeLine(JsonElement data, DateTimeOffset at)
        {
            var lineId = GuidOf(data, "lineId");
            var reason = StringOf(data, "reason");
            var index = result.Lines.FindIndex(l => l.LineId == lineId);
            if (index < 0)
            {
                result.Violations.Add("Mudança numa linha que não está na venda.");
                return;
            }

            var line = result.Lines[index];
            var operatorId = Operator(data, at, "quem mudou a linha", permission: null);
            if (reason == "note")
            {
                var note = StringOf(data, "note");
                if (TextProblem(note, MaxNoteLength, "observação do item") is { } problem)
                {
                    result.Violations.Add(problem);
                    note = null;
                }

                result.Lines[index] = line with { Note = note };
                return;
            }

            if (Locked("mudou a quantidade"))
            {
                return;
            }

            var quantity = QuantityOf(data, "quantity");
            if (quantity is not > 0)
            {
                result.Violations.Add("Quantidade nova inválida.");
                return;
            }

            var authorizedBy = GuidOf(data, "authorizedBy");
            if (quantity.Value > line.Quantity)
            {
                // Aumentar é livre; o mesmo item lançado de novo vira o último lançamento.
                _lastAdd = reason == "same-item" ? (line.LineId, quantity.Value - line.Quantity) : _lastAdd?.LineId == line.LineId ? null : _lastAdd;
            }
            else if (quantity.Value < line.Quantity)
            {
                var reduced = line.Quantity - quantity.Value;
                var free = freeRemoveLast && _lastAdd is { } last && last.LineId == line.LineId && reduced <= last.Quantity;
                if (reason == "remove-last" && !(free && reduced == _lastAdd!.Value.Quantity))
                {
                    NeedsAuthorization(authorizedBy, at, "o desfazer do último lançamento não era o último lançamento de fato");
                }
                else if (reason != "remove-last" && !free)
                {
                    NeedsAuthorization(authorizedBy, at, "redução de quantidade de uma linha que não é o último lançamento");
                }

                result.Exceptions.Add(new ReplayIncident(OperatorIncidentKind.QuantityReduced, operatorId, authorizedBy, at));
                _lastAdd = null;
            }
            else if (reason == "remove-last")
            {
                result.Violations.Add("Desfazer do último lançamento sem mudar a quantidade.");
            }

            result.Lines[index] = line with { Quantity = quantity.Value };
        }

        private void RemoveLine(JsonElement data, DateTimeOffset at)
        {
            if (Locked("removeu item"))
            {
                return;
            }

            var lineId = GuidOf(data, "lineId");
            var index = result.Lines.FindIndex(l => l.LineId == lineId);
            if (index < 0)
            {
                result.Violations.Add("Remoção de uma linha que não está na venda.");
                return;
            }

            var line = result.Lines[index];
            var reason = StringOf(data, "reason");
            var operatorId = Operator(data, at, "quem removeu o item", permission: null);
            var authorizedBy = GuidOf(data, "authorizedBy");
            var isLast = freeRemoveLast && _lastAdd is { } last && last.LineId == line.LineId && last.Quantity == line.Quantity;
            if (reason == "last-item" && !isLast)
            {
                NeedsAuthorization(authorizedBy, at, "a remoção do último item não era do último lançamento de fato");
            }
            else if (reason != "last-item")
            {
                NeedsAuthorization(authorizedBy, at, "remoção de item que não é o último lançado");
            }

            result.Exceptions.Add(new ReplayIncident(OperatorIncidentKind.ItemRemoved, operatorId, authorizedBy, at));
            result.Lines.RemoveAt(index);
            _lastAdd = null;
        }

        private void Discard(JsonElement data, DateTimeOffset at)
        {
            var operatorId = Operator(data, at, "quem descartou a venda", permission: null);
            var reason = StringOf(data, "reason");
            if (string.IsNullOrWhiteSpace(reason) || TextProblem(reason, MaxDiscardReasonLength, "motivo do descarte") is not null)
            {
                result.Violations.Add("Descarte sem motivo, longo demais ou com sequência que parece número de cartão.");
            }

            if (result.CapturedCents > 0)
            {
                result.Violations.Add("Venda descartada com pagamento capturado.");
            }

            result.Discarded = true;
            result.Exceptions.Add(new ReplayIncident(OperatorIncidentKind.SaleDiscarded, operatorId, null, at));
        }

        private void Customer(JsonElement data)
        {
            var customer = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("customer", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
            result.CustomerId = GuidOf(customer, "customerId");
            result.DocumentId = GuidOf(customer, "documentId");
        }

        private void Tender(string type, JsonElement data, DateTimeOffset at)
        {
            var tender = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("tender", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;
            var id = GuidOf(tender, "tenderId");
            var status = StringOf(tender, "status") ?? string.Empty;
            if (id is null || !Transitions.ContainsKey(status))
            {
                result.Violations.Add("Pagamento sem id ou com situação desconhecida.");
                return;
            }

            if (!_inPayment)
            {
                result.Violations.Add("Pagamento registrado fora da tela de pagamento.");
            }

            if (type == "TenderCaptured" && status != "Captured")
            {
                result.Violations.Add("TenderCaptured com situação diferente de Captured.");
            }

            var confirmedBy = GuidOf(tender, "confirmedBy");
            if (confirmedBy is null)
            {
                result.Violations.Add("Pagamento sem quem confirmou.");
            }
            else
            {
                result.Checks.Add(new ReplayCheck(confirmedBy, at, "quem confirmou o pagamento", "pos.create", NeedsPinSession: true));
            }

            var amount = LongOf(tender, "amountCents");
            var received = LongOf(tender, "receivedCents");
            var change = LongOf(tender, "changeCents") ?? 0;
            var installments = (int?)LongOf(tender, "installments");
            var integration = StringOf(tender, "integration") ?? "Manual";
            var authorizedBy = GuidOf(tender, "authorizedBy");
            var previous = result.Tenders.FindIndex(x => x.TenderId == id);
            var from = previous < 0 ? "Created" : result.Tenders[previous].Status;
            if (from != status && !Transitions[from].Contains(status))
            {
                result.Violations.Add($"Pagamento foi de {from} para {status}, o que a máquina de estados não permite.");
                return;
            }

            if (from == "Captured" && status is "Voided" or "Refunded")
            {
                NeedsAuthorization(authorizedBy, at, "estorno de pagamento capturado", "pos.cancel_sale");
            }

            if (amount is not > 0 || change < 0 || received is < 0)
            {
                result.Violations.Add("Pagamento com valor inválido (centavos inteiros, maior que zero; troco e recebido não negativos).");
                return;
            }

            if (received is { } got && (got < amount || change != got - amount))
            {
                result.Violations.Add("Troco incoerente: o troco tem de ser o recebido menos o valor, e o recebido não pode ser menor que o valor.");
            }
            else if (received is null && change != 0)
            {
                result.Violations.Add("Troco sem o valor recebido.");
            }

            if (status == "Captured" && from != "Captured")
            {
                var remaining = result.TotalCents - result.CapturedCents;
                if (amount > remaining)
                {
                    result.Violations.Add($"Pagamento de {Money(amount)} passa do que faltava ({Money(remaining)}).");
                }
            }

            var nsu = StringOf(tender, "nsu");
            var note = StringOf(tender, "note");
            var authorization = StringOf(tender, "authorizationCode");
            foreach (var problem in new[] { TextProblem(nsu, MaxNsuLength, "NSU"), TextProblem(note, MaxNoteLength, "observação do pagamento"), TextProblem(authorization, MaxAuthorizationLength, "código de autorização") })
            {
                if (problem is not null)
                {
                    result.Violations.Add(problem);
                }
            }

            var integrationFilled = integration == "Manual"
                && new[] { StringOf(tender, "txid"), StringOf(tender, "endToEndId"), StringOf(tender, "provider"), StringOf(tender, "acquirerId"), authorization }.Any(v => !string.IsNullOrWhiteSpace(v));
            if (integration != "Manual")
            {
                result.Violations.Add($"Integração de pagamento {integration} ainda não existe no caixa (só o modo manual).");
            }
            else if (integrationFilled)
            {
                result.Violations.Add("Pagamento manual com campos de integração (txid, endToEndId, provedor, adquirente ou autorização) preenchidos.");
            }

            var replay = new ReplayTender(id.Value, status, GuidOf(tender, "methodId"), StringOf(tender, "methodType"), amount.Value, received, change, installments,
                StringOf(tender, "cardBrand"), PosText.LooksLikeCardNumber(nsu) ? null : nsu, null, PosText.LooksLikeCardNumber(note) ? null : note, integration, confirmedBy, at,
                integrationFilled);
            if (previous < 0)
            {
                result.Tenders.Add(replay);
            }
            else
            {
                result.Tenders[previous] = replay;
            }
        }

        private void Complete(JsonElement data, DateTimeOffset at)
        {
            var operatorId = Operator(data, at, "quem concluiu a venda", "pos.create");
            if (GuidOf(data, "recoveredBy") is { } recoveredBy)
            {
                result.Checks.Add(new ReplayCheck(recoveredBy, at, "quem reabriu o caixa e concluiu a venda", "pos.create", NeedsPinSession: true));
            }

            foreach (var unknown in result.Tenders.Where(t => t.Status is "Unknown" or "Pending"))
            {
                result.PaymentNotes.Add($"Pagamento {unknown.TenderId} ficou {unknown.Status} sem captura: confira com a maquininha ou o banco.");
            }

            if (result.CapturedCents != result.TotalCents)
            {
                result.PaymentNotes.Add($"Os pagamentos capturados ({Money(result.CapturedCents)}) não fecham o total refeito das linhas ({Money(result.TotalCents)}).");
            }

            result.Completed = true;
            result.CompletedAt = at;
            result.CompletedBy = operatorId;
        }

        private bool Locked(string action)
        {
            if (_inPayment)
            {
                result.Violations.Add($"A venda estava no pagamento e {action}.");
                return true;
            }

            return false;
        }

        private Guid? Operator(JsonElement data, DateTimeOffset at, string role, string? permission)
        {
            var id = GuidOf(data, "operatorId");
            if (id is null)
            {
                result.Violations.Add($"Evento sem o operador ({role}).");
                return null;
            }

            result.Checks.Add(new ReplayCheck(id, at, role, permission, NeedsPinSession: true));
            return id;
        }

        private void NeedsAuthorization(Guid? authorizedBy, DateTimeOffset at, string what, string permission = "pos.cancel_item")
        {
            if (authorizedBy is null)
            {
                result.Violations.Add($"Sem quem autorizou: {what}.");
                return;
            }

            result.Checks.Add(new ReplayCheck(authorizedBy, at, "quem autorizou (" + what + ")", permission, NeedsPinSession: false));
        }
    }

    /// <summary>Texto livre dentro do tamanho e sem sequência de cartão; null quando está bom.</summary>
    public static string? TextProblem(string? text, int max, string field) =>
        string.IsNullOrEmpty(text) ? null
        : PosText.LooksLikeCardNumber(text) ? $"O campo {field} tinha sequência que parece número de cartão (o sistema nunca guarda número de cartão)."
        : text.Length > max ? $"O campo {field} passou de {max} caracteres."
        : null;

    public static string Money(long? cents) => ((cents ?? 0) / 100m).ToString("C", CultureInfo.GetCultureInfo("pt-BR"));

    public static Guid? GuidOf(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) && g != Guid.Empty ? g : null;

    public static string? StringOf(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static long? LongOf(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    public static DateTimeOffset? DateOf(JsonElement e, string name) =>
        StringOf(e, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;

    /// <summary>Quantidade em texto com ponto (o aparelho guarda assim) ou número; até 4 casas.</summary>
    public static decimal? QuantityOf(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            return null;
        }

        decimal value;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
        }
        else if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var number))
        {
            value = number;
        }
        else
        {
            return null;
        }

        return decimal.Round(value, 4) == value ? value : null;
    }
}
