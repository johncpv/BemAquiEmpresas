using System.Globalization;
using System.Text.Json;
using BemAqui.Pos.Domain;

namespace BemAqui.Pos.Application;

/// <summary>Linha da venda refeita no servidor (quantidade em <c>decimal(18,4)</c>, preço em centavos do aparelho).</summary>
/// <remarks>
/// PDV-04: <c>ListUnitCents</c> é o preço da tabela quando houve troca de preço (o praticado fica em <c>UnitCents</c>); <c>DiscountCents</c> é o
/// desconto da linha (o do total é rateado só na gravação).
/// </remarks>
public sealed record ReplayLine(
    Guid LineId, Guid ProductId, string Name, decimal Quantity, long UnitCents, long? PriceVersion, string? Note, long? ListUnitCents = null, long DiscountCents = 0,
    string? DiscountReason = null, string? PriceReason = null)
{
    /// <summary>O preço da tabela com que a linha entrou (conferido com o catálogo do servidor).</summary>
    public long TableCents => ListUnitCents ?? UnitCents;
}

/// <summary>PDV-04: aprovação de supervisor citada por um evento da venda; a subida confere com <c>pos.supervisor_approvals</c> e marca o uso.</summary>
public sealed record ReplayApproval(Guid ApprovalId, SupervisorAction Action, Guid? OperatorId, Guid? SupervisorId, Guid? LineId, DateTimeOffset At);

/// <summary>Pagamento refeito pela máquina de estados do servidor (o último estado de cada <c>tenderId</c>).</summary>
public sealed record ReplayTender(
    Guid TenderId, string Status, Guid? MethodId, string? MethodType, long AmountCents, long? ReceivedCents, long ChangeCents, int? Installments, string? CardBrand, string? Nsu,
    string? AuthorizationCode, string? Note, string Integration, Guid? ConfirmedBy, DateTimeOffset At, bool IntegrationFieldsFilled);

/// <summary>Quem precisa ser conferido: o operador numa sessão por PIN do aparelho e, quando há, a permissão no instante.</summary>
/// <summary><c>Soft</c>: a falha vira divergência (a venda entra), não quarentena (sessão por PIN de quem autorizou, revisão de segurança M2).</summary>
public sealed record ReplayCheck(Guid? UserId, DateTimeOffset At, string Role, string? Permission, bool NeedsPinSession, bool Soft = false);

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

    /// <summary>Total a pagar: as linhas líquidas do desconto de cada uma, menos o desconto no total.</summary>
    public long TotalCents => Lines.Sum(PosSaleReplay.NetCents) - SaleDiscountCents;

    /// <summary>PDV-04: desconto no total da venda (rateado nas linhas na gravação) e o motivo.</summary>
    public long SaleDiscountCents { get; set; }

    public string? SaleDiscountReason { get; set; }

    /// <summary>PDV-04 CA 10: as aprovações de supervisor que os eventos citam.</summary>
    public List<ReplayApproval> Approvals { get; } = [];

    public long CapturedCents => Tenders.Where(t => t.Status == "Captured").Sum(t => t.AmountCents);

    public long ChangeCents => Tenders.Where(t => t.Status == "Captured").Sum(t => t.ChangeCents);

    /// <summary>O que não passou na conferência: a venda vai para a quarentena ("Venda recusada na validação").</summary>
    public List<string> Violations { get; } = [];

    /// <summary>Pagamento que precisa de alguém olhar (ex.: <c>Unknown</c> sem captura): divergência, a venda entra.</summary>
    public List<string> PaymentNotes { get; } = [];

    public List<ReplayCheck> Checks { get; } = [];

    /// <summary>Autorização que pede supervisor feita pelo próprio operador (M2): divergência, até a PDV-04.</summary>
    public List<string> AuthorizationNotes { get; } = [];

    /// <summary>Os instantes dos eventos da venda, na ordem (M1: perto do evento e sem voltar no tempo).</summary>
    public List<DateTimeOffset> Times { get; } = [];

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

    /// <summary>Limites de sanidade (B2): R$ 9.999.999,99 por preço ou pagamento e 99.999 unidades por linha.</summary>
    public const long MaxCents = 999_999_999;
    public const decimal MaxQuantity = 99_999m;

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

    public const int MaxReasonLength = 140;

    public static long LineCents(ReplayLine line) => (long)Math.Round(line.Quantity * line.UnitCents, 0, MidpointRounding.AwayFromZero);

    public static long NetCents(ReplayLine line) => LineCents(line) - line.DiscountCents;

    /// <summary>
    /// PDV-04 CA 1 e 2: o desconto passa do limite da loja (em %, PP-38)? Em centavos, sem arredondar o percentual: desconto × 100 &gt; limite × base.
    /// Base zero com desconto passa sempre.
    /// </summary>
    public static bool AboveLimit(long discountCents, long baseCents, decimal limitPercent) =>
        discountCents > 0 && (baseCents <= 0 || discountCents * 100m > limitPercent * baseCents);

    /// <summary>
    /// Desconto em % convertido em centavos sobre a base, arredondado para o centavo mais próximo (meio centavo para cima): o mesmo cálculo do aparelho.
    /// </summary>
    public static long PercentToCents(long baseCents, decimal percent) =>
        (long)Math.Round(baseCents * percent / 100m, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Rateio do desconto no total pelas linhas, proporcional ao líquido de cada uma (maiores restos levam o centavo que sobra; empate pela ordem):
    /// a soma das partes é sempre o desconto. Linha de líquido zero não recebe parte.
    /// </summary>
    public static long[] Prorate(IReadOnlyList<long> netCents, long discountCents)
    {
        ArgumentNullException.ThrowIfNull(netCents);
        var shares = new long[netCents.Count];
        var total = netCents.Where(n => n > 0).Sum();
        if (discountCents <= 0 || total <= 0)
        {
            return shares;
        }

        var remainders = new (decimal Fraction, int Index)[netCents.Count];
        for (var i = 0; i < netCents.Count; i++)
        {
            var exact = netCents[i] > 0 ? (decimal)discountCents * netCents[i] / total : 0m;
            shares[i] = (long)Math.Floor(exact);
            remainders[i] = (exact - shares[i], i);
        }

        var left = discountCents - shares.Sum();
        foreach (var (_, index) in remainders.Where(r => netCents[r.Index] > 0).OrderByDescending(r => r.Fraction).ThenBy(r => r.Index).Take((int)left))
        {
            shares[index]++;
        }

        return shares;
    }

    /// <summary>
    /// Refaz a venda. <paramref name="freeRemoveLast"/> e <paramref name="discountLimitPercent"/> vêm da loja no servidor, nunca do aparelho.
    /// </summary>
    public static SaleReplayResult Run(JsonElement payload, bool freeRemoveLast, decimal discountLimitPercent = 0m)
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

        var state = new State(result, freeRemoveLast, discountLimitPercent);
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
            result.Times.Add(at.Value);
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

    private sealed class State(SaleReplayResult result, bool freeRemoveLast, decimal discountLimitPercent)
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
                case "DiscountApplied":
                    Discount(data, at);
                    break;
                case "PriceOverridden":
                    OverridePrice(data, at);
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
            if (lineId is null || productId is null || quantity is not > 0 || quantity > MaxQuantity || unit is not >= 0 || unit > MaxCents || result.Lines.Any(x => x.LineId == lineId))
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
            ClearSaleDiscount();
            _ = at;
        }

        /// <summary>PDV-04: mudar os itens tira o desconto no total (o aparelho faz o mesmo e avisa); o operador dá de novo, se quiser.</summary>
        private void ClearSaleDiscount()
        {
            result.SaleDiscountCents = 0;
            result.SaleDiscountReason = null;
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
            if (quantity is not > 0 || quantity > MaxQuantity)
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
                    NeedsAuthorization(authorizedBy, at, "o desfazer do último lançamento não era o último lançamento de fato", operatorId: operatorId, approvalId: GuidOf(data, "approvalId"), lineId: line.LineId);
                }
                else if (reason != "remove-last" && !free)
                {
                    NeedsAuthorization(authorizedBy, at, "redução de quantidade de uma linha que não é o último lançamento", operatorId: operatorId, approvalId: GuidOf(data, "approvalId"), lineId: line.LineId);
                }

                result.Exceptions.Add(new ReplayIncident(OperatorIncidentKind.QuantityReduced, operatorId, authorizedBy, at));
                _lastAdd = null;
            }
            else if (reason == "remove-last")
            {
                result.Violations.Add("Desfazer do último lançamento sem mudar a quantidade.");
            }

            // PDV-04: mudar a quantidade tira o desconto da linha e o do total (o aparelho faz o mesmo).
            result.Lines[index] = line with { Quantity = quantity.Value, DiscountCents = 0, DiscountReason = null };
            ClearSaleDiscount();
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
                NeedsAuthorization(authorizedBy, at, "a remoção do último item não era do último lançamento de fato", operatorId: operatorId, approvalId: GuidOf(data, "approvalId"), lineId: line.LineId);
            }
            else if (reason != "last-item")
            {
                NeedsAuthorization(authorizedBy, at, "remoção de item que não é o último lançado", operatorId: operatorId, approvalId: GuidOf(data, "approvalId"), lineId: line.LineId);
            }

            result.Exceptions.Add(new ReplayIncident(OperatorIncidentKind.ItemRemoved, operatorId, authorizedBy, at));
            result.Lines.RemoveAt(index);
            ClearSaleDiscount();
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

            if (previous >= 0 && from == status && Differs(result.Tenders[previous], GuidOf(tender, "methodId"), LongOf(tender, "amountCents"), LongOf(tender, "receivedCents"), LongOf(tender, "changeCents") ?? 0, confirmedBy))
            {
                // B3: o mesmo pagamento de novo no mesmo estado só vale com os mesmos valores (Captured → Captured com outro valor é adulteração).
                result.Violations.Add($"Pagamento repetido como {status} com outros valores.");
                return;
            }

            if (from == "Captured" && status is "Voided" or "Refunded")
            {
                NeedsAuthorization(authorizedBy, at, "estorno de pagamento capturado", "pos.cancel_sale");
            }

            if (amount is not > 0 or > MaxCents || change is < 0 or > MaxCents || received is < 0 or > MaxCents)
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

        /// <summary>
        /// Ação que pede a permissão (PDV-04). Três caminhos:
        /// <list type="bullet">
        /// <item>com <c>approvalId</c>: a aprovação por PIN do supervisor (CA 10), conferida na subida contra <c>pos.supervisor_approvals</c> (ação,
        /// venda, linha, operador e supervisor; uso único). O supervisor nunca é o operador (CA 11);</item>
        /// <item>o próprio operador (CA 5: "com a permissão, removo com motivo"): precisa da permissão no instante, senão quarentena;</item>
        /// <item>outra pessoa sem a aprovação (sem rede, até a OFF-03): a permissão no instante e, sem sessão por PIN dela neste aparelho, a
        /// pendência "Autorização sem supervisor" (revisão de segurança M2 da OFF-02).</item>
        /// </list>
        /// </summary>
        private void NeedsAuthorization(Guid? authorizedBy, DateTimeOffset at, string what, string permission = "pos.cancel_item", Guid? operatorId = null,
            Guid? approvalId = null, Guid? lineId = null)
        {
            if (authorizedBy is null)
            {
                result.Violations.Add($"Sem quem autorizou: {what}.");
                return;
            }

            if (approvalId is { } approval)
            {
                if (authorizedBy == operatorId || operatorId is null || SupervisorActions.FromPermission(permission) is not { } action)
                {
                    result.Violations.Add($"{Capitalize(what)}: aprovação de supervisor com o próprio operador ou sem o operador.");
                    return;
                }

                result.Approvals.Add(new ReplayApproval(approval, action, operatorId, authorizedBy, lineId, at));
                return;
            }

            var role = "quem autorizou (" + what + ")";
            result.Checks.Add(new ReplayCheck(authorizedBy, at, role, permission, NeedsPinSession: false));
            if (authorizedBy != operatorId)
            {
                result.Checks.Add(new ReplayCheck(authorizedBy, at, role, null, NeedsPinSession: true, Soft: true));
            }
        }

        /// <summary>
        /// PDV-04 CA 1 e 2: desconto na linha (<c>lineId</c>) ou no total (sem <c>lineId</c>), em centavos (o aparelho converte o % com
        /// <see cref="PercentToCents"/>). Zero tira o desconto. Até o limite da loja passa com o motivo; acima, pede <c>pos.discount</c> (do operador ou
        /// do supervisor). Um desconto novo substitui o anterior do mesmo alvo.
        /// </summary>
        private void Discount(JsonElement data, DateTimeOffset at)
        {
            if (Locked("deu desconto"))
            {
                return;
            }

            var operatorId = Operator(data, at, "quem deu o desconto", permission: null);
            var lineId = GuidOf(data, "lineId");
            var cents = LongOf(data, "discountCents");
            var reason = StringOf(data, "reason")?.Trim();
            var index = lineId is null ? -1 : result.Lines.FindIndex(l => l.LineId == lineId);
            if (lineId is not null && index < 0)
            {
                result.Violations.Add("Desconto numa linha que não está na venda.");
                return;
            }

            var baseCents = index >= 0 ? LineCents(result.Lines[index]) : result.Lines.Sum(NetCents);
            if (cents is not >= 0 || cents > baseCents)
            {
                result.Violations.Add("Desconto negativo ou maior que o valor do item ou da venda.");
                return;
            }

            if (cents > 0 && (string.IsNullOrWhiteSpace(reason) || TextProblem(reason, MaxReasonLength, "motivo do desconto") is not null))
            {
                result.Violations.Add("Desconto sem motivo, longo demais ou com sequência que parece número de cartão.");
                return;
            }

            if (AboveLimit(cents.Value, baseCents, discountLimitPercent))
            {
                NeedsAuthorization(GuidOf(data, "authorizedBy"), at, "desconto acima do limite da loja", "pos.discount", operatorId, GuidOf(data, "approvalId"), lineId);
            }

            var text = cents > 0 ? reason : null;
            if (index >= 0)
            {
                result.Lines[index] = result.Lines[index] with { DiscountCents = cents.Value, DiscountReason = text };
                ClearSaleDiscount();
            }
            else
            {
                result.SaleDiscountCents = cents.Value;
                result.SaleDiscountReason = text;
            }
        }

        /// <summary>
        /// PDV-04 CA 3: troca do preço de uma linha (para mais ou para menos), sempre com <c>pos.change_price</c> (do operador ou do supervisor) e
        /// motivo. A linha guarda o preço da tabela com que entrou e o praticado; o desconto da linha e o do total saem.
        /// </summary>
        private void OverridePrice(JsonElement data, DateTimeOffset at)
        {
            if (Locked("trocou o preço"))
            {
                return;
            }

            var operatorId = Operator(data, at, "quem trocou o preço", permission: null);
            var lineId = GuidOf(data, "lineId");
            var index = result.Lines.FindIndex(l => l.LineId == lineId);
            var unit = LongOf(data, "unitCents");
            var reason = StringOf(data, "reason")?.Trim();
            if (index < 0 || unit is not >= 0 || unit > MaxCents)
            {
                result.Violations.Add("Troca de preço sem a linha ou com preço inválido.");
                return;
            }

            if (string.IsNullOrWhiteSpace(reason) || TextProblem(reason, MaxReasonLength, "motivo da troca de preço") is not null)
            {
                result.Violations.Add("Troca de preço sem motivo, longa demais ou com sequência que parece número de cartão.");
                return;
            }

            NeedsAuthorization(GuidOf(data, "authorizedBy"), at, "troca de preço", "pos.change_price", operatorId, GuidOf(data, "approvalId"), lineId);
            var line = result.Lines[index];
            result.Lines[index] = line with
            {
                ListUnitCents = line.ListUnitCents ?? line.UnitCents, UnitCents = unit.Value, PriceReason = reason, DiscountCents = 0, DiscountReason = null,
            };
            ClearSaleDiscount();
        }

        private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        private static bool Differs(ReplayTender previous, Guid? methodId, long? amount, long? received, long change, Guid? confirmedBy) =>
            previous.MethodId != methodId || previous.AmountCents != amount || previous.ReceivedCents != received || previous.ChangeCents != change || previous.ConfirmedBy != confirmedBy;
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
