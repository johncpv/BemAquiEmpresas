using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// OFF-02: subida idempotente dos eventos do caixa com PostgreSQL real. Lote assinado pelo aparelho, registro imutável, projeção da venda pelo
/// <c>ISaleIngestion</c> com o outbox na mesma transação, conflitos (preço, relógio, sequência, versão, aparelho substituído), CPF fora dos eventos,
/// as Pendências do PDV e os requisitos de segurança herdados (operador na sessão por PIN, autorização, cartão, sessão de caixa, modo não fiscal).
/// </summary>
public partial class PosTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>O caixa pronto para vender: loja no modo não fiscal, aparelho, operador por PIN, produto, a forma Dinheiro e a cadeia da época.</summary>
    private sealed class Till(Device device, Guid store, Guid register, string prefix, int epoch, Guid deviceId, Guid operatorId, Guid productId, long priceCents, long priceVersion, Guid cashId)
    {
        public Device Device { get; } = device;
        public Guid Store { get; } = store;
        public Guid Register { get; } = register;
        public string Prefix { get; } = prefix;
        public int Epoch { get; } = epoch;
        public Guid DeviceId { get; } = deviceId;
        public Guid OperatorId { get; } = operatorId;
        public Guid ProductId { get; } = productId;
        public long PriceCents { get; } = priceCents;
        public long PriceVersion { get; } = priceVersion;
        public Guid CashId { get; } = cashId;
        public long Seq { get; set; }
        public string? Head { get; set; }
        public int SaleSeq { get; set; }
    }

    private async Task<(HttpClient Owner, Till Till)> TillAsync(bool nonFiscal = true, params string[] extraActions)
    {
        await CommerceAsync(true);
        var (ownerId, owner) = await OwnerAsync(otp: true);
        _ = ownerId;
        var (owner2, store, device) = await RegisterAsync(owner);
        if (nonFiscal)
        {
            var version = (await StoreOfAsync(owner2, store)).GetProperty("version").GetUInt32();
            await OkAsync(await NonFiscalAsync(owner2, store, true, true, NonFiscalTerms.Version, version));
        }

        var group = await CashierGroupAsync(["pos.cancel_item", "pos.cash_out", "pos.cash_in", .. extraActions]);
        var operatorId = await CreateOperationUserAsync(owner2, group, store, "Caio Sync");
        await OkAsync(await PinLoginAsync(device, operatorId, InitialPin, OwnPin));
        var (_, drawer, _, _) = await SetupAsync();
        var cashMethod = (await OkAsync(await owner2.PostAsJsonAsync("/api/v1/finance/payment-methods",
            new { name = Unique("Dinheiro "), type = "Cash", destinationAccountId = drawer, acquirerPartyId = (Guid?)null, nfePaymentCode = (string?)null }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var product = await ProductAsync(owner2, Unique("Arroz "), 10m);
        await DrainOutboxAsync();
        var snapshot = await SnapshotAsync(device);
        var handshake = await OkAsync(await HandshakeAsync(device));
        var item = Item(snapshot.GetProperty("items"), product)!.Value;
        var cash = snapshot.GetProperty("paymentMethods").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == cashMethod).GetProperty("id").GetGuid();
        return (owner2, new Till(device, store, handshake.GetProperty("registerId").GetGuid(), handshake.GetProperty("salePrefix").GetString()!, handshake.GetProperty("epoch").GetInt32(),
            handshake.GetProperty("deviceId").GetGuid(), operatorId, product, item.GetProperty("priceCents").GetInt64(), item.GetProperty("priceListVersion").GetInt64(), cash));
    }

    /// <summary>Evento da fila com o selo (seq e hash encadeado), como o aparelho manda.</summary>
    private static object Seal(Till till, string kind, object payload, DateTimeOffset at, long? seq = null, int schemaVersion = 1, string? prevHash = null, bool chain = true)
    {
        var eventId = Guid.CreateVersion7();
        var text = JsonSerializer.Serialize(payload, Web);
        var occurredAt = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var s = seq ?? till.Seq + 1;
        var previous = prevHash ?? till.Head;
        var hash = PosEventChain.Hash(eventId, till.DeviceId, till.Register, till.Epoch, s, schemaVersion, kind, occurredAt, previous, text);
        if (chain)
        {
            till.Seq = s;
            till.Head = hash;
        }

        return new { eventId, deviceId = till.DeviceId, registerId = till.Register, deviceEpoch = till.Epoch, seq = s, schemaVersion, kind, occurredAt, prevHash = previous, hash, payload = text };
    }

    private static object OpenCash(Till till, Guid session, DateTimeOffset at, long openingCents = 10000) =>
        new { type = "CashSessionOpened", sessionId = session, registerId = till.Register, deviceEpoch = till.Epoch, operatorId = till.OperatorId, openingCents, openingMovementId = Guid.CreateVersion7(), at };

    /// <summary>Venda concluída: <paramref name="quantity"/> do produto, paga em dinheiro com troco; <paramref name="extra"/> entra antes do pagamento.</summary>
    private static (Guid SaleId, object Payload) SaleOf(Till till, Guid session, DateTimeOffset at, int quantity = 2, long? unitCents = null, long? priceVersion = null,
        Guid? operatorId = null, Guid? documentId = null, Func<Guid, Guid, List<object>>? extra = null, string? nsu = null, Guid? fixedSaleId = null,
        Guid? fixedLineId = null)
    {
        var saleId = fixedSaleId ?? Guid.CreateVersion7();
        var op = operatorId ?? till.OperatorId;
        var lineId = fixedLineId ?? Guid.CreateVersion7();
        var unit = unitCents ?? till.PriceCents;
        var events = new List<object>
        {
            Ev(saleId, "SaleStarted", at, new { operatorId = op }),
            Ev(saleId, "LineAdded", at, new { line = new { lineId, itemId = till.ProductId, name = "Arroz", unit = "UN", fractional = false, quantity = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), unitCents = unit, priceTableVersion = priceVersion ?? till.PriceVersion, code = (string?)null, note = (string?)null } }),
        };
        long total = unit * quantity;
        if (extra is not null)
        {
            var more = extra(saleId, lineId);
            events.AddRange(more);
        }

        if (documentId is { } doc)
        {
            events.Add(Ev(saleId, "CustomerSet", at, new { customer = new { customerId = (Guid?)null, documentId = doc, documentKind = "cpf" } }));
        }

        var received = total + 500;
        events.Add(Ev(saleId, "PaymentStarted", at, new { operatorId = op, totalCents = total }));
        events.Add(Ev(saleId, "TenderCaptured", at, new
        {
            tender = new
            {
                tenderId = Guid.CreateVersion7(), partId = "main", status = "Captured", methodId = till.CashId, methodName = "Dinheiro", methodType = "Cash", amountCents = total,
                receivedCents = received, changeCents = 500L, installments = (int?)null, provider = (string?)null, acquirerId = (string?)null, cardBrand = (string?)null, nsu,
                authorizationCode = (string?)null, txid = (string?)null, endToEndId = (string?)null, integration = "Manual", operatorAttested = true, needsReconciliation = false,
                note = (string?)null, confirmedBy = op, at,
            },
        }));
        events.Add(Ev(saleId, "SaleCompleted", at, new { operatorId = op, totalCents = total, paidCents = total, changeCents = 500L }));
        till.SaleSeq++;
        var number = till.Prefix + till.SaleSeq.ToString("000000", System.Globalization.CultureInfo.InvariantCulture);
        return (saleId, new { saleId, number, status = "completed", totalCents = total, changeCents = 500L, cashSessionId = session, events });
    }

    private static object Ev(Guid saleId, string type, DateTimeOffset at, object data) => new { id = Guid.CreateVersion7(), saleId, type, at, data };

    private async Task<HttpResponseMessage> PostEventsAsync(Device device, object body, TimeSpan? skew = null)
    {
        const string path = "/api/v1/pos/sync/events";
        var text = JsonSerializer.Serialize(body, Web);
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        var timestamp = (DateTimeOffset.UtcNow + (skew ?? TimeSpan.Zero)).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var payload = DeviceSignatureHeaders.Payload("POST", path, timestamp, Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
        var signature = device.Key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        request.Headers.Add("Cookie", PosDeviceCookie.Name + "=" + device.Secret);
        request.Headers.Add(DeviceSignatureHeaders.Timestamp, timestamp);
        request.Headers.Add(DeviceSignatureHeaders.Signature, Convert.ToBase64String(signature));
        return await Anonymous().SendAsync(request);
    }

    private static Dictionary<Guid, string> Outcomes(JsonElement result) =>
        result.GetProperty("results").EnumerateArray().ToDictionary(r => r.GetProperty("eventId").GetGuid(), r => r.GetProperty("outcome").GetString()!);

    private static Guid IdOf(object sealedEvent) => (Guid)sealedEvent.GetType().GetProperty("eventId")!.GetValue(sealedEvent)!;

    private Task<List<SyncDivergence>> DivergencesAsync(Guid eventId) =>
        WithDbAsync(db => db.SyncDivergences.AsNoTracking().Where(d => d.EventId == eventId).ToListAsync());

    [Fact]
    public async Task Mesmo_lote_50_vezes_em_paralelo_grava_cada_evento_e_cada_venda_uma_vez_com_o_outbox_e_o_CPF_a_parte()
    {
        var (_, till) = await TillAsync();
        using var device = till.Device;
        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var (saleId, sale) = SaleOf(till, session, now, documentId: documentId);
        var open = Seal(till, PosEventIngestion.CashKind, OpenCash(till, session, now.AddMinutes(-1)), now.AddMinutes(-1));
        var sold = Seal(till, PosEventIngestion.SaleKind, sale, now);
        const string cpf = "52998224725";
        var body = new { events = new[] { open, sold }, documents = new[] { new { saleId, kind = "cpf", value = cpf } }, oldestPendingAt = now.AddMinutes(-1) };

        // CA 4: 50 reenvios em paralelo do mesmo lote.
        var responses = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => PostEventsAsync(device, body)));
        foreach (var response in responses)
        {
            var result = await OkAsync(response);
            Outcomes(result).Values.Should().OnlyContain(o => o == "Accepted");
            result.GetProperty("acks")[0].GetProperty("seq").GetInt64().Should().Be(2, "CA 5: até o seq 2 sem lacuna");
        }

        (await WithDbAsync(db => db.DeviceEvents.CountAsync(e => e.RegisterId == till.Register))).Should().Be(2);
        (await WithDbAsync(db => db.Sales.CountAsync(s => s.Id == saleId))).Should().Be(1);
        var stored = await WithDbAsync(db => db.Sales.AsNoTracking().SingleAsync(s => s.Id == saleId));
        stored.CashSessionId.Should().Be(session);
        stored.FromDevice.Should().BeTrue();
        stored.Number.Should().Be(till.Prefix + "000001");
        stored.Total.Should().Be(till.PriceCents * 2 / 100m);
        stored.SellerUserId.Should().Be(till.OperatorId);
        (await WithDbAsync(db => db.SalePayments.AsNoTracking().Where(p => p.SaleId == saleId).Select(p => p.ConfirmedBy).SingleAsync())).Should().Be(till.OperatorId);

        // CA 7: o SaleCompleted foi para o outbox junto com a venda.
        var completed = await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains("SaleCompleted")).Select(m => m.Payload).ToListAsync());
        completed.Count(p => p.Contains(saleId.ToString(), StringComparison.Ordinal)).Should().Be(1);

        // CA 17: o CPF está cifrado à parte, com índice cego, e não aparece nos eventos nem no outbox.
        var doc = await WithDbAsync(db => db.SaleCustomerDocuments.AsNoTracking().SingleAsync(d => d.SaleId == saleId));
        doc.DocumentCipher.Should().NotContain(cpf);
        doc.BlindIndex.Should().HaveLength(64);
        (await WithDbAsync(db => db.DeviceEvents.AsNoTracking().AnyAsync(e => e.Payload.Contains(cpf)))).Should().BeFalse();
        (await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.OccurredAt >= now.AddMinutes(-5)).Select(m => m.Payload).ToListAsync())).Should().NotContain(p => p.Contains(cpf, StringComparison.Ordinal));
        await using (var scope = await ScopeAsync())
        {
            (await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<BemAqui.Sales.Contracts.ISaleCustomerDocuments>(scope.ServiceProvider)
                .FindSalesAsync("529.982.247-25")).Should().Contain(saleId);
        }

        // CA 3: o registro é imutável (UPDATE e DELETE falham no banco).
        var eventId = IdOf(sold);
        var update = () => WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE pos.device_events SET kind = 'x' WHERE event_id = {eventId}"));
        var delete = () => WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos.device_events WHERE event_id = {eventId}"));
        await update.Should().ThrowAsync<Npgsql.PostgresException>();
        await delete.Should().ThrowAsync<Npgsql.PostgresException>();
    }

    [Fact]
    public async Task Conflitos_preco_relogio_sequencia_e_versao_viram_divergencia_ou_quarentena_sem_descartar()
    {
        var (_, till) = await TillAsync();
        using var device = till.Device;
        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        var open = Seal(till, PosEventIngestion.CashKind, OpenCash(till, session, now.AddMinutes(-1)), now.AddMinutes(-1));
        await OkAsync(await PostEventsAsync(device, new { events = new[] { open } }));

        // CA 9: a tabela da loja mudou; o aparelho vendeu com a versão antiga. Vale o preço do aparelho e nasce "Preço desatualizado".
        await WithDbAsync(async db =>
        {
            var row = await db.CatalogItems.SingleAsync(i => i.StoreId == till.Store && i.Kind == CatalogItemKind.Product && i.EntityId == till.ProductId);
            var item = JsonSerializer.Deserialize<PosItemPayload>(row.Payload, PosCatalogProjector.Json)!;
            row.Payload = JsonSerializer.Serialize(item with { PriceCents = 1250, PriceListVersion = till.PriceVersion + 1 }, PosCatalogProjector.Json);
            row.PriceListVersion = till.PriceVersion + 1;
            return await db.SaveChangesAsync();
        });
        var (saleId, sale) = SaleOf(till, session, now);
        var priced = Seal(till, PosEventIngestion.SaleKind, sale, now);

        // CA 10: o relógio do aparelho 3 min adiantado (dentro dos 5 min da assinatura, fora dos 2 min da tolerância).
        var result = await OkAsync(await PostEventsAsync(device, new { events = new[] { priced } }, TimeSpan.FromMinutes(3)));
        Outcomes(result)[IdOf(priced)].Should().Be("AcceptedWithDivergence");
        var divergences = await DivergencesAsync(IdOf(priced));
        divergences.Select(d => d.Kind).Should().BeEquivalentTo([SyncDivergenceKind.PriceOutdated, SyncDivergenceKind.DeviceClock]);
        divergences.Single(d => d.Kind == SyncDivergenceKind.PriceOutdated).Summary.Should().Contain("vendido a R$ 10,00").And.Contain("vigente R$ 12,50");
        (await WithDbAsync(db => db.SaleLines.AsNoTracking().Where(l => l.SaleId == saleId).Select(l => l.UnitPrice).SingleAsync())).Should().Be(10m);
        var sold = await WithDbAsync(db => db.Sales.AsNoTracking().SingleAsync(s => s.Id == saleId));
        sold.CompletedAtDevice.Should().NotBeNull("as duas datas ficam gravadas");
        sold.CompletedAt.Should().BeBefore(sold.CompletedAtDevice!.Value, "o financeiro do MVP usa a hora do servidor");

        // CA 11: lacuna de seq (o 3 não chegou): o 4 e o 5 entram em quarentena "Sequência quebrada", nada é descartado.
        till.Seq++;
        var (_, gapSale) = SaleOf(till, session, now);
        var gap = Seal(till, PosEventIngestion.SaleKind, gapSale, now, prevHash: new string('a', 64));
        var (_, nextSale) = SaleOf(till, session, now);
        var next = Seal(till, PosEventIngestion.SaleKind, nextSale, now);
        result = await OkAsync(await PostEventsAsync(device, new { events = new[] { gap, next } }));
        Outcomes(result).Values.Should().OnlyContain(o => o == "Quarantined");
        result.GetProperty("acks")[0].GetProperty("seq").GetInt64().Should().Be(2);
        (await DivergencesAsync(IdOf(next))).Should().ContainSingle(d => d.Kind == SyncDivergenceKind.BrokenSequence && d.Quarantined);
        (await WithDbAsync(db => db.DeviceEvents.CountAsync(e => e.RegisterId == till.Register))).Should().Be(4);

        // CA 13: versão do formato fora do prazo de suporte vai para a quarentena com o motivo.
        var other = await TillAsync();
        using var otherDevice = other.Till.Device;
        var old = Seal(other.Till, PosEventIngestion.CashKind, OpenCash(other.Till, Guid.CreateVersion7(), now), now, schemaVersion: 99);
        Outcomes(await OkAsync(await PostEventsAsync(otherDevice, new { events = new[] { old } })))[IdOf(old)].Should().Be("Quarantined");
        (await DivergencesAsync(IdOf(old))).Should().ContainSingle(d => d.Kind == SyncDivergenceKind.SchemaVersion);

        // M1: o relógio não volta no tempo em relação ao evento aceito anterior da época.
        var third = await TillAsync();
        using var thirdDevice = third.Till.Device;
        var thirdSession = Guid.CreateVersion7();
        var opened = Seal(third.Till, PosEventIngestion.CashKind, OpenCash(third.Till, thirdSession, now), now);
        var past = now.AddMinutes(-10);
        var back = Seal(third.Till, PosEventIngestion.CashKind, new { type = "CashInRecorded", sessionId = thirdSession, movementId = Guid.CreateVersion7(), amountCents = 100L, reason = "Troco", operatorId = third.Till.OperatorId, aboveExpected = false, at = past }, past);
        var backResult = await OkAsync(await PostEventsAsync(thirdDevice, new { events = new[] { opened, back } }));
        Outcomes(backResult)[IdOf(back)].Should().Be("Quarantined");
        (await DivergencesAsync(IdOf(back))).Should().ContainSingle(d => d.Kind == SyncDivergenceKind.DeviceClock);
    }

    [Fact]
    public async Task Aparelho_substituido_sobe_em_quarentena_e_a_epoca_nova_nao_colide()
    {
        var (owner, till) = await TillAsync();
        using var oldDevice = till.Device;
        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        var oldOpen = Seal(till, PosEventIngestion.CashKind, OpenCash(till, session, now), now);

        // Outro aparelho entra no caixa (época 2); o antigo ainda tinha um evento gravado.
        using var fresh = new Device();
        await OkAsync(await RegisterDeviceAsync(owner, fresh, till.Store, till.Register, replace: true), HttpStatusCode.Created);
        var oldPrint = Seal(till, PosEventIngestion.PrintKind, new { type = "ReceiptPrinted", id = Guid.CreateVersion7(), registerId = till.Register, kind = "Test", copy = 1, operatorId = till.OperatorId, printedAt = now }, now);
        var result = await OkAsync(await PostEventsAsync(oldDevice, new { events = new[] { oldOpen, oldPrint }, oldestPendingAt = now.AddHours(-9) }));
        Outcomes(result).Values.Should().OnlyContain(o => o == "Quarantined", "CA 12: o endpoint de subida continua aberto para o revogado");
        // M3: uma pendência por aparelho revogado, não uma por evento; e o aparelho revogado não dispara o aviso de caixa.
        (await WithDbAsync(db => db.SyncDivergences.CountAsync(d => d.DeviceId == till.DeviceId && d.Kind == SyncDivergenceKind.ReplacedDevice))).Should().Be(1);
        (await NotificationsAsync("Caixa 01 sem enviar vendas há 9")).Should().BeEmpty();
        (await WithDbAsync(db => db.CashSessions.CountAsync(s => s.Id == session))).Should().Be(0, "quarentena não projeta");

        // A época nova começa no seq 1 e não colide com a anterior.
        var handshake = await OkAsync(await HandshakeAsync(fresh));
        var epoch2 = new Till(fresh, till.Store, till.Register, handshake.GetProperty("salePrefix").GetString()!, handshake.GetProperty("epoch").GetInt32(),
            handshake.GetProperty("deviceId").GetGuid(), till.OperatorId, till.ProductId, till.PriceCents, till.PriceVersion, till.CashId);
        epoch2.Epoch.Should().Be(till.Epoch + 1);
        var group = await CashierGroupAsync();
        var operatorId = await CreateOperationUserAsync(owner, group, till.Store, "Rita Nova");
        await OkAsync(await PinLoginAsync(fresh, operatorId, InitialPin, OwnPin));
        var newOpen = Seal(epoch2, PosEventIngestion.CashKind, new
        {
            type = "CashSessionOpened", sessionId = Guid.CreateVersion7(), registerId = till.Register, deviceEpoch = epoch2.Epoch, operatorId, openingCents = 0L,
            openingMovementId = Guid.CreateVersion7(), at = DateTimeOffset.UtcNow,
        }, DateTimeOffset.UtcNow);
        Outcomes(await OkAsync(await PostEventsAsync(fresh, new { events = new[] { newOpen } })))[IdOf(newOpen)].Should().Be("Accepted");
    }

    [Fact]
    public async Task Requisitos_de_seguranca_operador_autorizacao_cartao_e_documento_fiscal_seguram_a_venda()
    {
        var (_, till) = await TillAsync();
        using var device = till.Device;
        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        await OkAsync(await PostEventsAsync(device, new { events = new[] { Seal(till, PosEventIngestion.CashKind, OpenCash(till, session, now.AddMinutes(-1)), now.AddMinutes(-1)) } }));

        // Operador sem sessão por PIN neste caixa (o aparelho diz que foi ele): quarentena, e o relatório de exceções conta.
        var (outsider, _) = await OwnerAsync(name: "Fora Docaixa");
        var (_, forged) = SaleOf(till, session, now, operatorId: outsider);
        var e1 = Seal(till, PosEventIngestion.SaleKind, forged, now);

        // Remoção de item que não era o último lançamento, sem quem autorizou.
        var (_, removal) = SaleOf(till, session, now, quantity: 1, extra: (sale, firstLine) =>
        [
            Ev(sale, "LineAdded", now, new { line = new { lineId = Guid.CreateVersion7(), itemId = till.ProductId, name = "Arroz", unit = "UN", fractional = false, quantity = "1", unitCents = till.PriceCents, priceTableVersion = till.PriceVersion, code = (string?)null, note = (string?)null } }),
            Ev(sale, "LineRemoved", now, new { lineId = firstLine, reason = "authorized", operatorId = till.OperatorId }),
        ]);
        var e2 = Seal(till, PosEventIngestion.SaleKind, removal, now);

        // NSU com número de cartão: sai do conteúdo antes de gravar e o evento vai para a quarentena.
        var (_, card) = SaleOf(till, session, now, nsu: "4111 1111 1111 1111");
        var e3 = Seal(till, PosEventIngestion.SaleKind, card, now);
        var result = await OkAsync(await PostEventsAsync(device, new { events = new[] { e1, e2, e3 } }));
        Outcomes(result).Values.Should().OnlyContain(o => o == "Quarantined");
        (await DivergencesAsync(IdOf(e1))).Single().Summary.Should().Contain("não tinha sessão por PIN");
        (await DivergencesAsync(IdOf(e2))).Single().Summary.Should().Contain("Sem quem autorizou");
        (await WithDbAsync(db => db.DeviceEvents.AsNoTracking().SingleAsync(e => e.EventId == IdOf(e3)))).Payload.Should().NotContain("4111");
        (await WithDbAsync(db => db.OperatorIncidents.CountAsync(i => i.RegisterId == till.Register && i.Kind == OperatorIncidentKind.EventRejected))).Should().Be(3);

        // Loja sem o modo não fiscal aceito: a venda espera o Proprietário ("Documento fiscal").
        var (_, other) = await TillAsync(nonFiscal: false);
        using var otherDevice = other.Device;
        var otherSession = Guid.CreateVersion7();
        var o1 = Seal(other, PosEventIngestion.CashKind, OpenCash(other, otherSession, now), now);
        var (_, plain) = SaleOf(other, otherSession, now);
        var o2 = Seal(other, PosEventIngestion.SaleKind, plain, now);
        var otherResult = await OkAsync(await PostEventsAsync(otherDevice, new { events = new[] { o1, o2 } }));
        Outcomes(otherResult)[IdOf(o2)].Should().Be("Quarantined");
        (await DivergencesAsync(IdOf(o2))).Should().ContainSingle(d => d.Kind == SyncDivergenceKind.FiscalDocument);
    }

    [Fact]
    public async Task Pendencias_do_PDV_listam_e_liberar_da_quarentena_projeta_a_venda_auditado()
    {
        var (owner, till) = await TillAsync();
        using var device = till.Device;
        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        var (outsider, outsiderClient) = await OwnerAsync(name: "Gerente Fora");
        var open = Seal(till, PosEventIngestion.CashKind, OpenCash(till, session, now.AddMinutes(-1)), now.AddMinutes(-1));
        var (saleId, sale) = SaleOf(till, session, now, operatorId: outsider);
        var held = Seal(till, PosEventIngestion.SaleKind, sale, now);
        await OkAsync(await PostEventsAsync(device, new { events = new[] { open, held }, oldestPendingAt = now.AddHours(-5) }));

        // CA 14: sales.view vê a pendência com a loja, o caixa, a venda e o que divergiu.
        var (_, viewer) = await MemberAsync("sales.view");
        var list = await OkAsync(await viewer.GetAsync($"/api/v1/pos/divergences?storeId={till.Store}"));
        var item = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("saleId").ValueKind == JsonValueKind.String && i.GetProperty("saleId").GetGuid() == saleId);
        item.GetProperty("quarantined").GetBoolean().Should().BeTrue();
        item.GetProperty("kindLabel").GetString().Should().Be("Recusado na conferência");
        item.GetProperty("registerNumber").GetInt32().Should().Be(1);
        var id = item.GetProperty("id").GetGuid();
        var version = item.GetProperty("version").GetUInt32();
        (await viewer.PostAsJsonAsync($"/api/v1/pos/divergences/{id}/resolve", new { resolution = "ReleaseQuarantine", reason = "Conferido", version }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "resolver exige sales.approve");

        // CA 15: liberar da quarentena projeta a venda; a pendência fica Resolved e auditada.
        // M4: o operador do evento não libera o próprio evento.
        (await outsiderClient.PostAsJsonAsync($"/api/v1/pos/divergences/{id}/resolve", new { resolution = "ReleaseQuarantine", reason = "Eu mesmo", version }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (_, approver) = await MemberAsync("sales.view", "sales.approve");
        (await approver.PostAsJsonAsync($"/api/v1/pos/divergences/{id}/resolve", new { resolution = "AcceptAsIs", reason = "x", version }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "evento em quarentena só se libera ou mantém fora");
        var resolved = await OkAsync(await approver.PostAsJsonAsync($"/api/v1/pos/divergences/{id}/resolve", new { resolution = "ReleaseQuarantine", reason = "Gerente conferiu com o operador", version }));
        resolved.GetProperty("status").GetString().Should().Be("Resolved");
        (await WithDbAsync(db => db.Sales.CountAsync(s => s.Id == saleId))).Should().Be(1);
        // M4: a conferência rodou de novo e o achado ficou visível.
        (await DivergencesAsync(IdOf(held))).Should().Contain(d => !d.Quarantined && d.Summary.StartsWith("Liberado da quarentena apesar de", StringComparison.Ordinal));
        (await WithDbAsync(db => db.AuditEntries.AsNoTracking().CountAsync(a => a.Entity == nameof(SyncDivergence) && a.EntityId == id.ToString() && a.Action == "Updated"))).Should().Be(1);

        // A exportação e o relatório de exceções por operador.
        var csv = await viewer.GetAsync($"/api/v1/pos/divergences/export?status=Resolved&storeId={till.Store}");
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        (await csv.Content.ReadAsStringAsync()).Should().Contain("Gerente conferiu com o operador");
        var today = DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(now).DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var report = await OkAsync(await viewer.GetAsync($"/api/v1/pos/divergences/operator-exceptions?from={today}&to={today}&storeId={till.Store}"));
        report.GetProperty("rows").EnumerateArray().Should().Contain(r => r.GetProperty("eventsRejected").GetInt32() == 1);

        // CA 16: a fila do aparelho tinha evento de 5 h atrás: o gerente (sales.approve) recebe o aviso.
        (await NotificationsAsync("Caixa 01 sem enviar vendas há")).Should().Contain("Caixa 01 sem enviar vendas há 5 horas.");
        _ = owner;
    }

    [Fact]
    public async Task Sangria_acima_do_calculado_e_marcada_no_servidor_e_sessao_desconhecida_vai_para_quarentena()
    {
        var (_, till) = await TillAsync();
        using var device = till.Device;
        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        var open = Seal(till, PosEventIngestion.CashKind, OpenCash(till, session, now.AddMinutes(-2), openingCents: 5000), now.AddMinutes(-2));
        // O aparelho diz que não passou do esperado; o servidor recalcula (R$ 50 de fundo, nenhuma venda): R$ 80 é acima.
        var drop = Seal(till, PosEventIngestion.CashKind, new
        {
            type = "CashOutRecorded", sessionId = session, movementId = Guid.CreateVersion7(), amountCents = 8000L, reason = "Cofre", operatorId = till.OperatorId, aboveExpected = false, at = now.AddMinutes(-1),
        }, now.AddMinutes(-1));
        var orphan = Seal(till, PosEventIngestion.CashKind, new
        {
            type = "CashInRecorded", sessionId = Guid.CreateVersion7(), movementId = Guid.CreateVersion7(), amountCents = 1000L, reason = "Troco", operatorId = till.OperatorId, aboveExpected = false, at = now,
        }, now);
        var result = await OkAsync(await PostEventsAsync(device, new { events = new[] { open, drop, orphan } }));
        Outcomes(result)[IdOf(drop)].Should().Be("AcceptedWithDivergence");
        (await WithDbAsync(db => db.CashMovements.AsNoTracking().SingleAsync(m => m.SessionId == session && m.Kind == CashMovementKind.CashOut))).AboveExpected.Should().BeTrue();
        Outcomes(result)[IdOf(orphan)].Should().Be("Quarantined");
        (await DivergencesAsync(IdOf(orphan))).Should().ContainSingle(d => d.Kind == SyncDivergenceKind.CashSession);
    }
}
