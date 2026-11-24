using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// OFF-03 com PostgreSQL real: o verificador sem rede desce só para quem é da loja e confere com o PIN sem o PIN descer (CA 1); a entrada sem rede
/// com a prova vale como sessão do caixa (CA 2), a abertura, a aprovação do supervisor com o código sem internet e o fechamento sem rede chegam
/// marcados e o servidor calcula a quebra (CA 6 a 8, 13 e 15); PIN trocado depois da descida marca a entrada e manda o que a pessoa fez para a
/// quarentena (CA 4); o bloqueio sem rede vai para a auditoria (CA 3); prova errada e aprovação acima do limite vão para a quarentena.
/// </summary>
public partial class PosTests
{
    private const string OfflineCode = "295173";

    private static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>A chave derivada que o aparelho calcula com o segredo digitado e os parâmetros do verificador.</summary>
    private static byte[] Derive(JsonElement verifier, string secret) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), Convert.FromBase64String(verifier.GetProperty("salt").GetString()!), verifier.GetProperty("iterations").GetInt32(),
            verifier.GetProperty("algorithm").GetString() == "PBKDF2-SHA512" ? HashAlgorithmName.SHA512 : HashAlgorithmName.SHA256, verifier.GetProperty("keyLength").GetInt32());

    /// <summary>A prova: HMAC-SHA256 com a chave de prova HKDF-SHA256(subkey, info "bq-offline-proof"), como no aparelho.</summary>
    private static string Mac(byte[] key, string message) =>
        Convert.ToBase64String(HMACSHA256.HashData(HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, [], Encoding.UTF8.GetBytes("bq-offline-proof")), Encoding.UTF8.GetBytes(message)));

    /// <summary>Operador e supervisor da loja sem sessão com rede: o PIN inicial vale (sem troca obrigatória) e o supervisor tem o código sem internet.</summary>
    private async Task<(Guid Operator, Guid Supervisor)> OfflinePeopleAsync(HttpClient owner, Till till)
    {
        var op = await CreateOperationUserAsync(owner, await CashierGroupAsync("pos.cash_out"), till.Store, "Olga Semrede");
        var supervisor = await CreateOperationUserAsync(owner, await CashierGroupAsync("pos.cancel_item", "pos.discount"), till.Store, "Sara Semrede");
        var code = new PasswordHasher<User>().HashPassword(null!, OfflineCode);
        await WithDbAsync(db => db.PosPins.Where(p => p.UserId == op || p.UserId == supervisor).ExecuteUpdateAsync(s => s.SetProperty(p => p.MustChange, false)));
        await WithDbAsync(db => db.PosPins.Where(p => p.UserId == supervisor).ExecuteUpdateAsync(s => s.SetProperty(p => p.OfflineCodeHash, code)));
        return (op, supervisor);
    }

    private static JsonElement VerifierOf(JsonElement snapshot, Guid user, string kind) =>
        snapshot.GetProperty("verifiers").EnumerateArray().Single(v => v.GetProperty("userId").GetGuid() == user && v.GetProperty("kind").GetString() == kind);

    private static object OfflineLogin(Till till, JsonElement verifier, Guid user, string pin, DateTimeOffset at)
    {
        var loginId = Guid.CreateVersion7();
        var proof = Mac(Derive(verifier, pin), PosOfflineProof.LoginMessage(till.DeviceId, user, loginId, Iso(at)));
        return Seal(till, PosEventIngestion.AuthKind, new { type = PosEventIngestion.OfflineLoginType, userId = user, loginId, at = Iso(at), proof, offline = true }, at);
    }

    [Fact]
    public async Task Turno_inteiro_sem_rede_com_verificador_entrada_aprovacao_do_supervisor_e_fechamento_conferidos_no_servidor()
    {
        var (owner, till) = await TillAsync();
        using var device = till.Device;
        var (op, supervisor) = await OfflinePeopleAsync(owner, till);

        // CA 1: o verificador desce só para quem é da loja, sem o PIN; confere com o PIN e o aparelho; a descida fica registrada.
        var snapshot = await SnapshotAsync(device);
        var opVerifier = VerifierOf(snapshot, op, "operator");
        var supVerifier = VerifierOf(snapshot, supervisor, "supervisor");
        snapshot.GetRawText().Should().NotContain(InitialPin).And.NotContain(OfflineCode);
        opVerifier.GetProperty("verifier").GetString().Should().Be(Mac(Derive(opVerifier, InitialPin),
            PosOfflineProof.VerifierMessage("operator", till.DeviceId, op, opVerifier.GetProperty("nonce").GetString()!)));
        supVerifier.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).Should().BeEquivalentTo(["cancel_item", "discount"]);
        snapshot.GetProperty("verifiers").EnumerateArray().Should().NotContain(v => v.GetProperty("userId").GetGuid() == op && v.GetProperty("kind").GetString() == "supervisor",
            "quem não aprova nada não recebe verificador de supervisor");
        (await WithDbAsync(db => db.DevicePinVerifiers.AsNoTracking().Where(v => v.DeviceId == till.DeviceId && (v.UserId == op || v.UserId == supervisor)).CountAsync()))
            .Should().Be(3, "o operador (PIN) e o supervisor (PIN, pois também vende, e o código sem internet)");

        // CA 2 e 6: entrada sem rede com a prova do PIN, abertura sem rede e a aprovação do supervisor sem rede (CA 7 e 15).
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        var session = Guid.CreateVersion7();
        var (saleId, firstLine) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var lineCents = 2 * till.PriceCents;
        var login = OfflineLogin(till, opVerifier, op, InitialPin, now);
        var open = Seal(till, PosEventIngestion.CashKind, new
        {
            type = "CashSessionOpened", sessionId = session, registerId = till.Register, deviceEpoch = till.Epoch, operatorId = op, openingCents = 10000L,
            openingMovementId = Guid.CreateVersion7(), at = now, offline = true,
        }, now);
        object ApprovalEvent(Guid approvalId, long before, string? proof = null, Guid? sale = null, Guid? line = null)
        {
            var at = Iso(now.AddSeconds(2));
            var message = PosOfflineProof.ApprovalMessage(till.DeviceId, approvalId, "CancelItem", op, supervisor, sale ?? saleId, line ?? firstLine, before, 0, session, at, "Cliente desistiu");
            return Seal(till, PosEventIngestion.ApprovalKind, new
            {
                approvalId, action = "CancelItem", operatorId = op, supervisorId = supervisor, reason = "Cliente desistiu", saleId = sale ?? saleId, lineId = line ?? firstLine,
                beforeCents = before, afterCents = 0L, at, cashSessionId = session, proof = proof ?? Mac(Derive(supVerifier, OfflineCode), message), offline = true,
            }, now.AddSeconds(2));
        }

        var approvalId = Guid.CreateVersion7();
        var approval = ApprovalEvent(approvalId, lineCents);
        var forged = ApprovalEvent(Guid.CreateVersion7(), lineCents, proof: Mac(Derive(supVerifier, "000000"), "x"), sale: Guid.CreateVersion7());
        var tooMuch = ApprovalEvent(Guid.CreateVersion7(), 6000, sale: Guid.CreateVersion7());
        var saleAt = now.AddSeconds(3);
        var (_, sale) = SaleOf(till, session, saleAt, operatorId: op, fixedSaleId: saleId, fixedLineId: firstLine, extra: (s, l) =>
        [
            Ev(s, "LineAdded", saleAt, new { line = new { lineId = Guid.CreateVersion7(), itemId = till.ProductId, name = "Arroz", unit = "UN", fractional = false, quantity = "2", unitCents = till.PriceCents, priceTableVersion = till.PriceVersion, code = (string?)null, note = (string?)null } }),
            Ev(s, "LineRemoved", saleAt, new { lineId = l, reason = "authorized", operatorId = op, authorizedBy = supervisor, approvalId }),
        ]);
        var saleWithFlag = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(sale, Web), Web)!;
        saleWithFlag["offline"] = true;
        var sold = Seal(till, PosEventIngestion.SaleKind, saleWithFlag, saleAt);
        var locked = Seal(till, PosEventIngestion.AuthKind, new { type = PosEventIngestion.OfflineLockedType, userId = supervisor, kind = "supervisor", at = Iso(saleAt) }, saleAt);
        var result = await OkAsync(await PostEventsAsync(device, new { events = new[] { login, open, approval, forged, tooMuch, sold, locked } }));
        var outcomes = Outcomes(result);
        outcomes[IdOf(login)].Should().Be("Accepted");
        outcomes[IdOf(open)].Should().Be("Accepted");
        outcomes[IdOf(approval)].Should().Be("Accepted");
        outcomes[IdOf(forged)].Should().Be("Quarantined");
        (await DivergencesAsync(IdOf(forged))).Single().Summary.Should().Contain("prova do código do supervisor não confere");
        outcomes[IdOf(tooMuch)].Should().Be("Quarantined");
        (await DivergencesAsync(IdOf(tooMuch))).Single().Summary.Should().Contain("acima do limite da loja");
        outcomes[IdOf(sold)].Should().Be("Accepted");
        outcomes[IdOf(locked)].Should().Be("Accepted");

        var row = await WithDbAsync(db => db.SupervisorApprovals.AsNoTracking().SingleAsync(a => a.Id == approvalId));
        (row.Online, row.CashSessionId, row.SupervisorId, row.OperatorId, row.UsedByEventId).Should().Be((false, (Guid?)session, supervisor, op, (Guid?)IdOf(sold)));
        (await WithDbAsync(db => db.CashSessions.AsNoTracking().SingleAsync(s => s.Id == session))).OpenedOffline.Should().BeTrue();
        (await WithDbAsync(db => db.SessionSales.AsNoTracking().SingleAsync(s => s.SaleId == saleId))).Offline.Should().BeTrue();
        (await WithDbAsync(db => db.AuditEntries.AnyAsync(a => a.EntityId == op.ToString() && a.Action == "PosPinOfflineLogin"))).Should().BeTrue();
        (await WithDbAsync(db => db.AuditEntries.AnyAsync(a => a.EntityId == supervisor.ToString() && a.Action == "PosPinOfflineLocked"))).Should().BeTrue("CA 3");

        // CA 8 e 13: o fechamento sem rede; com as aprovações sem rede acima do alerta da loja, a sessão vai para a conferência.
        await WithDbAsync(db => db.Stores.Where(s => s.Id == till.Store).ExecuteUpdateAsync(s => s.SetProperty(x => x.OfflineApprovalReviewAmount, 1m)));
        var closeAt = now.AddSeconds(5);
        var started = Seal(till, PosEventIngestion.CashKind, new { type = "CashCountStarted", sessionId = session, operatorId = op, at = closeAt, offline = true }, closeAt);
        var counted = Seal(till, PosEventIngestion.CashKind, new
        {
            type = "CashCounted", sessionId = session, countId = Guid.CreateVersion7(), operatorId = op, at = closeAt, keptCents = 10000L, depositMovementId = Guid.CreateVersion7(),
            salesCount = 1, lines = new[] { new { methodId = (Guid?)till.CashId, countedCents = 10000L + lineCents, denominations = (object?)null } }, offline = true,
        }, closeAt);
        var closed = Outcomes(await OkAsync(await PostEventsAsync(device, new { events = new[] { started, counted } })));
        closed.Values.Should().AllBe("Accepted");
        var after = await WithDbAsync(db => db.CashSessions.AsNoTracking().SingleAsync(s => s.Id == session));
        (after.Status, after.ClosedOffline, after.ReviewReasons).Should().Be((CashSessionStatus.PendingReview, true, CashReviewReasons.OfflineApprovals));
        var detail = await OkAsync(await owner.GetAsync($"/api/v1/pos/cash-sessions/{session}"));
        detail.GetProperty("offline").GetProperty("sales").EnumerateArray().Select(s => s.GetString()).Should().ContainSingle();
        detail.GetProperty("offline").GetProperty("approvals").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Venda_que_usa_aprovacao_sem_rede_de_um_real_para_desconto_maior_vai_para_a_quarentena()
    {
        var (owner, till) = await TillAsync();
        using var device = till.Device;
        var (op, supervisor) = await OfflinePeopleAsync(owner, till);
        await WithDbAsync(db => db.Stores.Where(s => s.Id == till.Store).ExecuteUpdateAsync(s => s.SetProperty(x => x.DiscountLimitPercent, 0m)));
        var snapshot = await SnapshotAsync(device);
        var opVerifier = VerifierOf(snapshot, op, "operator");
        var supVerifier = VerifierOf(snapshot, supervisor, "supervisor");

        // Revisão de segurança (achado 2): a aprovação sem rede vale R$ 1,00 de desconto na linha; a venda aplica R$ 2,00 citando a mesma aprovação.
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        var session = Guid.CreateVersion7();
        var (saleId, lineId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var lineCents = 2 * till.PriceCents;
        var login = OfflineLogin(till, opVerifier, op, InitialPin, now);
        var open = Seal(till, PosEventIngestion.CashKind, new
        {
            type = "CashSessionOpened", sessionId = session, registerId = till.Register, deviceEpoch = till.Epoch, operatorId = op, openingCents = 10000L,
            openingMovementId = Guid.CreateVersion7(), at = now, offline = true,
        }, now);
        var approvalId = Guid.CreateVersion7();
        var at = Iso(now.AddSeconds(2));
        var proof = Mac(Derive(supVerifier, OfflineCode),
            PosOfflineProof.ApprovalMessage(till.DeviceId, approvalId, "Discount", op, supervisor, saleId, lineId, lineCents, lineCents - 100, session, at, "Cliente fiel"));
        var approval = Seal(till, PosEventIngestion.ApprovalKind, new
        {
            approvalId, action = "Discount", operatorId = op, supervisorId = supervisor, reason = "Cliente fiel", saleId, lineId, beforeCents = lineCents,
            afterCents = lineCents - 100, at, cashSessionId = session, proof, offline = true,
        }, now.AddSeconds(2));
        var saleAt = now.AddSeconds(3);
        var (_, sale) = SaleOf(till, session, saleAt, operatorId: op, fixedSaleId: saleId, fixedLineId: lineId, extra: (s, _) =>
        [
            Ev(s, "DiscountApplied", saleAt, new { lineId, discountCents = 200L, reason = "Cliente fiel", operatorId = op, authorizedBy = supervisor, approvalId }),
        ]);
        var sold = Seal(till, PosEventIngestion.SaleKind, sale, saleAt);
        var outcomes = Outcomes(await OkAsync(await PostEventsAsync(device, new { events = new[] { login, open, approval, sold } })));
        outcomes[IdOf(approval)].Should().Be("Accepted");
        outcomes[IdOf(sold)].Should().Be("Quarantined");
        (await DivergencesAsync(IdOf(sold))).Should().Contain(d => d.Summary.Contains("valores diferentes dos aprovados"));
        (await WithDbAsync(db => db.SupervisorApprovals.AsNoTracking().SingleAsync(a => a.Id == approvalId))).UsedByEventId.Should().BeNull();
    }

    [Fact]
    public async Task PIN_trocado_depois_da_descida_marca_a_entrada_sem_rede_e_o_que_a_pessoa_fez_vai_para_a_quarentena()
    {
        var (owner, till) = await TillAsync();
        using var device = till.Device;
        var (op, _) = await OfflinePeopleAsync(owner, till);
        var verifier = VerifierOf(await SnapshotAsync(device), op, "operator");

        // CA 4: o PIN muda no servidor depois da descida; o aparelho ainda tinha o verificador antigo.
        var hash = new PasswordHasher<User>().HashPassword(null!, "614273");
        await WithDbAsync(db => db.PosPins.Where(p => p.UserId == op).ExecuteUpdateAsync(s => s.SetProperty(p => p.PinHash, hash)));
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        var login = OfflineLogin(till, verifier, op, InitialPin, now);
        var (_, sale) = SaleOf(till, Guid.CreateVersion7(), now.AddSeconds(1), operatorId: op);
        var sold = Seal(till, PosEventIngestion.SaleKind, sale, now.AddSeconds(1));
        var outcomes = Outcomes(await OkAsync(await PostEventsAsync(device, new { events = new[] { login, sold } })));
        outcomes[IdOf(login)].Should().Be("AcceptedWithDivergence");
        (await DivergencesAsync(IdOf(login))).Should().ContainSingle(d => d.Kind == SyncDivergenceKind.OfflineAccess && d.Summary.Contains("PIN que já tinha sido trocado"));
        outcomes[IdOf(sold)].Should().Be("Quarantined");

        // Na próxima descida, o verificador é o do PIN novo (o antigo é apagado no aparelho ao receber a lista).
        var fresh = VerifierOf(await SnapshotAsync(device), op, "operator");
        fresh.GetProperty("verifier").GetString().Should().Be(Mac(Derive(fresh, "614273"),
            PosOfflineProof.VerifierMessage("operator", till.DeviceId, op, fresh.GetProperty("nonce").GetString()!)));
    }
}
