using System.Globalization;
using System.Text.Json;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.Sales.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>
/// Projeção de cada tipo de evento do caixa (OFF-02 CA 7 e requisitos herdados). Toda conferência roda <b>antes</b> de gravar: se algo não passa, a
/// projeção devolve a quarentena e nada foi gravado nos outros módulos. Falha depois de gravar vira <see cref="ProjectionFailedException"/> (a transação
/// volta ao ponto de salvamento).
/// </summary>
public sealed partial class PosEventIngestion
{
    private async Task<Projection> ProjectSaleAsync(SyncContext context, Guid eventId, JsonElement root, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var replay = PosSaleReplay.Run(root, context.Store.FreeRemoveLast);
        var projection = new Projection { SaleId = replay.SaleId == Guid.Empty ? null : replay.SaleId, SaleNumber = replay.Number, OperatorId = replay.CompletedBy };
        projection.OperatorId ??= replay.Checks.Select(c => c.UserId).LastOrDefault(u => u is not null);
        var violations = new List<string>(replay.Violations);

        // Número do caixa: {loja}-{caixa}.{época}-{seq} (PDV-01), da época atual do caixa.
        var prefix = PosSaleNumber.Prefix(context.Store.Code, context.Register.Number, context.Device.Epoch);
        if (!replay.Number.StartsWith(prefix, StringComparison.Ordinal) || replay.Number.Length != prefix.Length + 6 || !replay.Number[prefix.Length..].All(char.IsAsciiDigit))
        {
            violations.Add($"Número da venda fora do formato do caixa ({prefix}000001).");
        }

        if (!context.Trusted)
        {
            await CheckPeopleAsync(context, replay.Checks, violations, ct);
        }

        if (violations.Count > 0 && !context.Trusted)
        {
            projection.Quarantine = new Finding(SyncDivergenceKind.Validation, "Venda recusada na conferência: " + string.Join(" ", violations.Distinct()), new { problems = violations.Distinct().ToList() });
            return projection;
        }

        if (replay.Discarded)
        {
            projection.Exceptions.AddRange(replay.Exceptions);
            events.Publish(new PosSaleDiscarded(replay.SaleId, Cut(replay.Number, 40), context.Store.Id, context.Register.Id, replay.CashSessionId,
                replay.Exceptions.LastOrDefault(e => e.Kind == OperatorIncidentKind.SaleDiscarded)?.OperatorId, occurredAt));
            return projection;
        }

        if (!replay.Completed || replay.CompletedAt is not { } completedAtDevice)
        {
            projection.Quarantine = new Finding(SyncDivergenceKind.Validation, "Venda sem conclusão nem descarte.");
            return projection;
        }

        // Sessão de caixa: a venda do PDV sempre tem uma, e do caixa do aparelho.
        if (replay.CashSessionId is not { } sessionId
            || await db.CashSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct) is not { } session || session.RegisterId != context.Register.Id)
        {
            projection.Quarantine = new Finding(SyncDivergenceKind.CashSession, "Venda sem sessão de caixa conhecida deste caixa: a abertura não chegou ou é de outro caixa.");
            return projection;
        }

        // CX-02: nada entra depois do "Fechar caixa" nem na sessão fechada (nem liberando da quarentena: reabra o caixa antes).
        if (AfterCountStarted(session, completedAtDevice) is { } late)
        {
            projection.Quarantine = new Finding(SyncDivergenceKind.CashSession, late);
            return projection;
        }

        // Modo não fiscal aceito na versão vigente no instante da venda (revisão de segurança da IMP-01).
        if (!context.Trusted && !await NonFiscalAcceptedAtAsync(context.Store.Id, completedAtDevice, ct))
        {
            projection.Quarantine = new Finding(SyncDivergenceKind.FiscalDocument,
                $"A loja não tinha o modo \"só cupom não fiscal\" aceito na versão vigente às {Local(completedAtDevice)}: a venda espera o Proprietário.");
            return projection;
        }

        var methods = await context.MethodsAsync(db, ct);
        var paymentProblems = new List<string>();
        foreach (var tender in replay.Tenders)
        {
            if (tender.MethodId is not { } methodId)
            {
                paymentProblems.Add("Pagamento sem a forma.");
                continue;
            }

            if (!methods.TryGetValue(methodId, out var method))
            {
                projection.Divergences.Add(new Finding(SyncDivergenceKind.Payment, $"A forma de pagamento do pagamento de {PosSaleReplay.Money(tender.AmountCents)} não está ativa hoje na loja.",
                    new { methodId }));
                continue;
            }

            if (!string.Equals(tender.MethodType, method.Type.ToString(), StringComparison.Ordinal))
            {
                paymentProblems.Add($"O tipo do pagamento ({tender.MethodType}) não é o da forma {method.Name} ({method.Type}).");
            }

            if (tender.ChangeCents > 0 && !method.AllowsChange)
            {
                paymentProblems.Add($"Troco na forma {method.Name}, que não dá troco.");
            }

            var card = PosCardTerms.IsCard(method.Type);
            var max = method.MaxInstallments ?? PosCardTerms.MaxInstallments(method.Type, []);
            if (tender.Installments is > 1 && (!card || tender.Installments > max))
            {
                paymentProblems.Add($"Parcelas ({tender.Installments}) fora do permitido na forma {method.Name} (até {max}).");
            }

            if (!string.IsNullOrEmpty(tender.CardBrand))
            {
                var known = Enum.TryParse<CardBrand>(tender.CardBrand, ignoreCase: true, out var brand) && brand != CardBrand.Any;
                var brands = method.CardBrands ?? [];
                if (!card || !known || (brand != CardBrand.Other && brands.Count > 0 && !brands.Contains(brand)))
                {
                    paymentProblems.Add($"Bandeira {tender.CardBrand} fora da tabela da forma {method.Name}.");
                }
            }
        }

        if (paymentProblems.Count > 0 && !context.Trusted)
        {
            projection.Quarantine = new Finding(SyncDivergenceKind.Validation, "Pagamento recusado na conferência: " + string.Join(" ", paymentProblems), new { problems = paymentProblems });
            return projection;
        }

        // Preço desatualizado (CA 9): vale o preço do aparelho; a divergência leva o vendido e o vigente.
        var items = await context.ItemsAsync(db, replay.Lines.Select(l => l.ProductId).Distinct().ToList(), ct);
        foreach (var line in replay.Lines)
        {
            if (items.TryGetValue(line.ProductId, out var item) && item.PriceListVersion is { } current && line.PriceVersion is { } sold && sold < current
                && item.PriceCents is { } currentCents && currentCents != line.UnitCents)
            {
                projection.Divergences.Add(new Finding(SyncDivergenceKind.PriceOutdated,
                    $"{Cut(line.Name, 80)}: vendido a {PosSaleReplay.Money(line.UnitCents)}; vigente {PosSaleReplay.Money(currentCents)}.",
                    new { productId = line.ProductId, soldCents = line.UnitCents, currentCents, soldVersion = sold, currentVersion = current }));
            }
        }

        // CA 10: com o relógio fora da tolerância, o financeiro do MVP usa a hora do servidor (o instante do aparelho corrigido pelo desvio medido).
        var completedAt = context.ClockOff ? completedAtDevice - context.ClockSkew : completedAtDevice;
        var now = clock.GetUtcNow();
        if (completedAt > now)
        {
            completedAt = now;
        }

        var request = new SaleIngestionRequest(
            replay.SaleId, SaleChannel.Pos, replay.Number, "pos.device_event", eventId, completedAt,
            replay.Lines.Select(l => new SaleLineInput(l.LineId, l.ProductId, l.Quantity, l.UnitCents / 100m, 0, l.Note)).ToList(),
            replay.Tenders.Where(t => t.MethodId is not null).Select(t => new SalePaymentInput(
                t.TenderId, t.MethodId!.Value, (t.ReceivedCents ?? t.AmountCents) / 100m, ToStatus(t.Status), PaymentIntegration.Manual, t.ChangeCents / 100m, null,
                t.Installments is > 0 ? t.Installments.Value : 1, null, null, NormalizeBrand(t.CardBrand), t.Nsu, null, null, null, t.ConfirmedBy)).ToList(),
            null, context.Store.Id, context.Register.Id, sessionId, context.Store.StockLocationId, replay.CustomerId, replay.CompletedBy, 0, FromDevice: true,
            CompletedAtDevice: completedAtDevice, PriceListVersion: replay.Lines.Select(l => l.PriceVersion).Where(v => v is not null).Min());
        var ingested = await sales.IngestAsync(request, ct);
        if (ingested.IsFailure)
        {
            throw new ProjectionFailedException(SyncDivergenceKind.Validation, "A venda não entrou no núcleo de vendas: " + ingested.Error!.Message);
        }

        if (ingested.Value.Outcome == SaleIngestionOutcome.AcceptedWithDivergence && replay.PaymentNotes.Count == 0)
        {
            projection.Divergences.Add(new Finding(SyncDivergenceKind.Payment,
                $"Pagamentos diferentes do total da venda em {PosSaleReplay.Money((long)(ingested.Value.PaymentDifference * 100))}.", new { difference = ingested.Value.PaymentDifference }));
        }

        foreach (var note in replay.PaymentNotes)
        {
            projection.Divergences.Add(new Finding(SyncDivergenceKind.Payment, note));
        }

        if (!ingested.Value.Replayed)
        {
            var cashKept = replay.Tenders.Where(t => t.Status == "Captured" && t.MethodId is { } m && methods.TryGetValue(m, out var method) && method.Type == PaymentMethodType.Cash)
                .Sum(t => t.AmountCents);
            db.SessionSales.Add(new SessionSale { SaleId = replay.SaleId, SessionId = sessionId, CashAmount = cashKept / 100m, CreatedAt = now });
            await closing.OnSaleArrivedAsync(sessionId, ct); // CX-02: a sessão contada esperava esta venda para fechar
        }

        projection.Exceptions.AddRange(replay.Exceptions);
        return projection;
    }

    private async Task<Projection> ProjectCashAsync(SyncContext context, JsonElement root, CancellationToken ct)
    {
        var type = PosSaleReplay.StringOf(root, "type");
        var operatorId = PosSaleReplay.GuidOf(root, "operatorId");
        var sessionId = PosSaleReplay.GuidOf(root, "sessionId");
        var at = PosSaleReplay.DateOf(root, "at");
        var projection = new Projection { OperatorId = operatorId };
        var problems = new List<string>();
        if (operatorId is null || sessionId is null || at is null)
        {
            problems.Add("Evento do caixa sem operador, sessão ou instante.");
        }

        if (PosSaleReplay.GuidOf(root, "registerId") is { } claimed && claimed != context.Register.Id)
        {
            problems.Add("O evento diz ser de outro caixa.");
        }

        var permission = type switch
        {
            "CashSessionOpened" => "pos.create",
            "CashOutRecorded" => "pos.cash_out",
            "CashInRecorded" => "pos.cash_in",
            "CashSessionClosed" => "pos.create",
            CountStartedType or CountedType => "pos.create",
            _ => null,
        };
        if (permission is null)
        {
            problems.Add($"Evento do caixa desconhecido: {type}.");
        }

        if (problems.Count == 0 && !context.Trusted)
        {
            await CheckPeopleAsync(context, [new ReplayCheck(operatorId, at!.Value, "operador do caixa", permission, NeedsPinSession: true)], problems, ct);
        }

        if (problems.Count > 0)
        {
            return Hold(projection, SyncDivergenceKind.Validation, "Movimento do caixa recusado na conferência: " + string.Join(" ", problems));
        }

        var session = await db.CashSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (type == "CashSessionOpened")
        {
            var opening = PosSaleReplay.LongOf(root, "openingCents");
            if (opening is not >= 0 or > MaxCents || PosSaleReplay.GuidOf(root, "openingMovementId") is not { } openingMovement)
            {
                return Hold(projection, SyncDivergenceKind.Validation, "Abertura com fundo de troco inválido (centavos inteiros, de zero a R$ 9.999.999,99).");
            }

            if (PosSaleReplay.LongOf(root, "deviceEpoch") is { } epoch && epoch != context.Register.DeviceEpoch)
            {
                return Hold(projection, SyncDivergenceKind.CashSession, "Abertura de uma época anterior do caixa (outro aparelho).");
            }

            var opened = await cash.OpenAsync(new CashSessionOpenInput(sessionId!.Value, context.Register.Id, context.Register.DeviceEpoch, operatorId!.Value, opening.Value / 100m,
                at!.Value, openingMovement), ct);
            return opened.IsSuccess ? projection : throw CashFailure(opened.Error!);
        }

        if (session is null)
        {
            return Hold(projection, SyncDivergenceKind.CashSession, "Movimento de uma sessão de caixa desconhecida (a abertura não chegou).");
        }

        if (session.RegisterId != context.Register.Id)
        {
            return Hold(projection, SyncDivergenceKind.CashSession, "Movimento de uma sessão de outro caixa.");
        }

        if (type is CountStartedType or CountedType)
        {
            return await ProjectCountAsync(context, root, type, session, operatorId!.Value, at!.Value, projection, ct);
        }

        if (session.Status != CashSessionStatus.Open)
        {
            return Hold(projection, SyncDivergenceKind.CashSession, "Movimento depois do fechamento da sessão de caixa.");
        }

        if (type == "CashSessionClosed")
        {
            if (!context.Trusted && operatorId != session.OperatorId && !await context.HadPermissionAsync(operators, operatorId!.Value, "sales.approve", at!.Value, ct))
            {
                return Hold(projection, SyncDivergenceKind.Validation, "Quem fechou não é o operador da sessão nem supervisor.");
            }

            var closed = await cash.CloseAsync(session.Id, operatorId!.Value, at!.Value, ct);
            return closed.IsSuccess ? projection : throw CashFailure(closed.Error!);
        }

        var amount = PosSaleReplay.LongOf(root, "amountCents");
        var reason = PosSaleReplay.StringOf(root, "reason")?.Trim();
        if (amount is not > 0 or > MaxCents || PosSaleReplay.GuidOf(root, "movementId") is not { } movementId)
        {
            return Hold(projection, SyncDivergenceKind.Validation, "Sangria ou suprimento com valor inválido (centavos inteiros, maior que zero, até R$ 9.999.999,99).");
        }

        if (string.IsNullOrEmpty(reason) || reason.Length > CashMovement.MaxReasonLength || PosText.LooksLikeCardNumber(reason))
        {
            return Hold(projection, SyncDivergenceKind.Validation, $"Motivo obrigatório, de 1 a {CashMovement.MaxReasonLength} caracteres, sem número de cartão.");
        }

        var kind = type == "CashOutRecorded" ? CashMovementKind.CashOut : CashMovementKind.CashIn;
        var above = kind == CashMovementKind.CashOut && amount.Value / 100m > await ExpectedCashAsync(session, ct);
        var recorded = await cash.RecordMovementAsync(new CashMovementInput(movementId, session.Id, kind, amount.Value / 100m, reason, operatorId!.Value, at!.Value, above), ct);
        if (recorded.IsFailure)
        {
            throw CashFailure(recorded.Error!);
        }

        if (above)
        {
            projection.Divergences.Add(new Finding(SyncDivergenceKind.CashSession,
                $"Sangria de {PosSaleReplay.Money(amount)} acima do dinheiro calculado da sessão: a sessão vai para conferência no fechamento.", new { sessionId = session.Id, amountCents = amount }));
        }

        return projection;
    }

    private async Task<Projection> ProjectPrintAsync(SyncContext context, JsonElement root, CancellationToken ct)
    {
        var operatorId = PosSaleReplay.GuidOf(root, "operatorId");
        var printedAt = PosSaleReplay.DateOf(root, "printedAt");
        var projection = new Projection { OperatorId = operatorId, SaleId = PosSaleReplay.GuidOf(root, "saleId"), SaleNumber = PosSaleReplay.StringOf(root, "saleNumber") };
        var problems = new List<string>();
        if (operatorId is null || printedAt is null || PosSaleReplay.GuidOf(root, "id") is not { } id
            || !Enum.TryParse<PrintRecordKind>(PosSaleReplay.StringOf(root, "kind"), ignoreCase: false, out var kind) || PosSaleReplay.LongOf(root, "copy") is not { } copy)
        {
            return Hold(projection, SyncDivergenceKind.Validation, "Impressão sem operador, instante, id, tipo ou via.");
        }

        if (PosSaleReplay.GuidOf(root, "registerId") is { } claimed && claimed != context.Register.Id)
        {
            problems.Add("A impressão diz ser de outro caixa.");
        }

        if (!context.Trusted)
        {
            await CheckPeopleAsync(context, [new ReplayCheck(operatorId, printedAt.Value, "quem imprimiu", "pos.create", NeedsPinSession: true)], problems, ct);
        }

        if (problems.Count > 0)
        {
            return Hold(projection, SyncDivergenceKind.Validation, "Impressão recusada na conferência: " + string.Join(" ", problems));
        }

        var recorded = await printers.RecordPrintAsync(context.Register.Id, operatorId.Value,
            new PrintRecordInput(id, PosSaleReplay.GuidOf(root, "printerId"), kind, projection.SaleId, projection.SaleNumber, (int)Math.Clamp(copy, 0, 1000), printedAt.Value), ct);
        return recorded.IsSuccess ? projection : throw new ProjectionFailedException(SyncDivergenceKind.Validation, "Impressão recusada: " + recorded.Error!.Message);
    }

    private const long MaxCents = 999_999_999;

    private static Projection Hold(Projection projection, SyncDivergenceKind kind, string summary)
    {
        projection.Quarantine = new Finding(kind, summary);
        return projection;
    }

    /// <summary>
    /// Recusas do <see cref="CashSessionIngestion"/> viram quarentena (requisito da CX-01): janela de horário = "Relógio do aparelho"; segunda
    /// abertura e sessão desconhecida ou fechada = "Sessão de caixa", com a mensagem de quem abriu e desde quando.
    /// </summary>
    private static ProjectionFailedException CashFailure(BemAqui.SharedKernel.Error error) => error.Code switch
    {
        "invalid_time" => new ProjectionFailedException(SyncDivergenceKind.DeviceClock, error.Message),
        "cash_session_open" or "cash_session_conflict" or "cash_session_not_found" or "cash_session_closed" or "cash_movement_conflict"
            => new ProjectionFailedException(SyncDivergenceKind.CashSession, error.Message),
        _ => new ProjectionFailedException(SyncDivergenceKind.Validation, error.Message),
    };

    /// <summary>
    /// Operador presente numa sessão por PIN deste aparelho no instante e com a permissão no instante (requisitos da PDV-02, PDV-03 e CX-01). Quem
    /// autorizou (supervisor) é conferido pela permissão; a sessão por PIN do supervisor chega com a PDV-04.
    /// </summary>
    private async Task CheckPeopleAsync(SyncContext context, IEnumerable<ReplayCheck> checks, List<string> problems, CancellationToken ct)
    {
        foreach (var check in checks)
        {
            if (check.UserId is not { } user)
            {
                problems.Add($"Sem {check.Role}.");
                continue;
            }

            if (check.NeedsPinSession && !await context.HadSessionAsync(operators, user, check.At, ct))
            {
                problems.Add($"{Capital(check.Role)} não tinha sessão por PIN neste caixa às {Local(check.At)}.");
                continue;
            }

            if (check.Permission is { } permission && !await context.HadPermissionAsync(operators, user, permission, check.At, ct))
            {
                problems.Add($"{Capital(check.Role)} não tinha a permissão {permission} às {Local(check.At)}.");
            }
        }
    }

    /// <summary>
    /// Dinheiro esperado na gaveta antes deste movimento (requisito da CX-01: o <c>aboveExpected</c> do aparelho é só indício): fundo de troco +
    /// dinheiro das vendas da sessão (líquido do troco) + suprimentos − sangrias, pela ordem em que chegaram (os eventos do caixa sobem em série).
    /// </summary>
    private async Task<decimal> ExpectedCashAsync(CashSession session, CancellationToken ct)
    {
        var sales = await db.SessionSales.AsNoTracking().Where(s => s.SessionId == session.Id).SumAsync(s => s.CashAmount, ct);
        var movements = await db.CashMovements.AsNoTracking().Where(m => m.SessionId == session.Id && m.Kind != CashMovementKind.Opening)
            .Select(m => new { m.Kind, m.Amount }).ToListAsync(ct);
        return session.OpeningAmount + sales + movements.Where(m => m.Kind == CashMovementKind.CashIn).Sum(m => m.Amount)
            - movements.Where(m => m.Kind == CashMovementKind.CashOut).Sum(m => m.Amount);
    }

    /// <summary>
    /// A loja estava no modo "só cupom não fiscal" com o aceite da versão vigente no instante (revisão de segurança da IMP-01): refeito pelo
    /// histórico da auditoria da loja (<c>fiscalMode</c> e <c>nonFiscalTermsVersion</c>), não pelo estado de hoje.
    /// </summary>
    private async Task<bool> NonFiscalAcceptedAtAsync(Guid storeId, DateTimeOffset at, CancellationToken ct)
    {
        var history = await audit.HistoryAsync(nameof(Store), storeId.ToString(), at, ct);
        string? mode = null;
        int? version = null;
        foreach (var change in history)
        {
            using var document = JsonDocument.Parse(change.Changes);
            if (document.RootElement.TryGetProperty("fiscalMode", out var m) && m.TryGetProperty("new", out var newMode))
            {
                mode = newMode.ValueKind switch
                {
                    JsonValueKind.String => newMode.GetString(),
                    JsonValueKind.Number => newMode.GetInt32() == (int)StoreFiscalMode.NonFiscal ? nameof(StoreFiscalMode.NonFiscal) : nameof(StoreFiscalMode.Nfce),
                    _ => null,
                };
            }

            if (document.RootElement.TryGetProperty("nonFiscalTermsVersion", out var v) && v.TryGetProperty("new", out var newVersion))
            {
                version = newVersion.ValueKind == JsonValueKind.Number ? newVersion.GetInt32() : null;
            }
        }

        return string.Equals(mode, nameof(StoreFiscalMode.NonFiscal), StringComparison.Ordinal) && version == NonFiscalTerms.VersionAt(at);
    }

    private static SalePaymentStatus ToStatus(string status) => Enum.TryParse<SalePaymentStatus>(status, out var parsed) ? parsed : SalePaymentStatus.Unknown;

    private static string? NormalizeBrand(string? brand) => string.IsNullOrWhiteSpace(brand) ? null : Cut(brand.Trim(), 20);

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("pt-BR")) + text[1..];
}
