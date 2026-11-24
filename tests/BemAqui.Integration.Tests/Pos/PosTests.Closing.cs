using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// CX-02: fechamento com conferência cega, com PostgreSQL real. Pela subida do aparelho: dentro e acima da tolerância, o recolhimento ao Cofre, nada
/// depois do "Fechar caixa" e uma contagem só. Na retaguarda: o esperado só para <c>sales.approve</c>, o operador não aprova nem reabre o próprio
/// caixa, e só a última sessão reabre.
/// </summary>
public partial class PosTests
{
    private static object CountStarted(Till till, Guid session, DateTimeOffset at) =>
        new { type = "CashCountStarted", sessionId = session, operatorId = till.OperatorId, at };

    private static object Counted(Till till, Guid session, DateTimeOffset at, long cashCents, long keptCents, int salesCount) =>
        new
        {
            type = "CashCounted", sessionId = session, countId = Guid.CreateVersion7(), operatorId = till.OperatorId, at, keptCents, depositMovementId = Guid.CreateVersion7(),
            salesCount, lines = new[] { new { methodId = (Guid?)till.CashId, countedCents = cashCents, denominations = (object?)null } },
        };

    [Fact]
    public async Task Fechamento_pela_subida_fecha_dentro_da_tolerancia_vai_para_conferencia_acima_e_recolhe_ao_Cofre()
    {
        var (_, till) = await TillAsync();
        using var device = till.Device;
        var now = DateTimeOffset.UtcNow;
        var first = Guid.CreateVersion7();
        var (_, sale) = SaleOf(till, first, now.AddSeconds(2));
        var total = till.PriceCents * 2;

        // Contado R$ 3,00 acima do esperado (fundo R$ 100 + venda em dinheiro, já sem o troco): dentro da tolerância de R$ 5,00 por forma.
        var events = new List<object>
        {
            Seal(till, PosEventIngestion.CashKind, OpenCash(till, first, now.AddSeconds(1)), now.AddSeconds(1)),
            Seal(till, PosEventIngestion.SaleKind, sale, now.AddSeconds(2)),
            Seal(till, PosEventIngestion.CashKind, CountStarted(till, first, now.AddSeconds(3)), now.AddSeconds(3)),
            Seal(till, PosEventIngestion.CashKind, Counted(till, first, now.AddSeconds(4), 10000 + total + 300, 10000, 1), now.AddSeconds(4)),
        };
        var (_, late) = SaleOf(till, first, now.AddSeconds(5));
        var lateSale = Seal(till, PosEventIngestion.SaleKind, late, now.AddSeconds(5));
        events.Add(lateSale);
        var result = await OkAsync(await PostEventsAsync(device, new { events }));

        var closed = await WithDbAsync(db => db.CashSessions.AsNoTracking().SingleAsync(s => s.Id == first));
        // Diagnóstico para o CI: o resultado de cada evento e as pendências do caixa, se a sessão não fechar.
        var held = await WithDbAsync(db => db.SyncDivergences.AsNoTracking().Where(d => d.RegisterId == till.Register).Select(d => d.Seq + ":" + d.Summary).ToListAsync());
        closed.Status.Should().Be(CashSessionStatus.Closed, "CA 5: diferença de R$ 3,00 dentro da tolerância ({0} :: {1})",
            string.Join(", ", Outcomes(result).Values), string.Join(" | ", held));
        closed.DepositAmount.Should().Be((total + 300) / 100m, "CA 12: recolhimento = contado − fundo que fica");
        var row = await WithDbAsync(db => db.CashCounts.AsNoTracking().SingleAsync(c => c.SessionId == first));
        (row.Expected, row.Difference).Should().Be(((10000 + total) / 100m, 3m), "o esperado é do servidor, nunca do aparelho");
        (await OutboxAsync("CashSessionClosed")).Count(p => p.Contains(first.ToString(), StringComparison.Ordinal))
            .Should().Be(1, "o CashSessionClosed sai quando a sessão chega a Closed");

        // Nada entra depois do "Fechar caixa": a venda atrasada vai para a quarentena.
        Outcomes(result)[IdOf(lateSale)].Should().Be("Quarantined");
        (await DivergencesAsync(IdOf(lateSale))).Should().ContainSingle(d => d.Kind == SyncDivergenceKind.CashSession);

        // O recolhimento vira a transferência Caixa do PDV → Cofre (pelo consumidor da CX-01).
        await DrainAsync();
        var deposit = await WithDbAsync(db => db.CashMovements.AsNoTracking().SingleAsync(m => m.SessionId == first && m.Kind == CashMovementKind.Deposit));
        var transfer = await WithDbAsync(db => db.BankTransfers.AsNoTracking().SingleAsync(t => t.PosCashMovementId == deposit.Id));
        transfer.Amount.Should().Be(deposit.Amount);
        transfer.Description.Should().Be("Recolhimento do caixa 01");
        var drawer = await WithDbAsync(db => db.Registers.Where(r => r.Id == till.Register).Select(r => r.CashAccountId).SingleAsync());
        transfer.FromAccountId.Should().Be(drawer);

        // CA 6: falta de R$ 10,00 no dinheiro, acima da tolerância: em conferência, e quem aprova vendas é avisado. Uma contagem só por fechamento.
        // Selados na ordem da fila do aparelho (o seq vem do Seal): abertura, "Fechar caixa", contagem e a recontagem.
        var second = Guid.CreateVersion7();
        var secondOpen = Seal(till, PosEventIngestion.CashKind, OpenCash(till, second, now.AddSeconds(6), openingCents: 5000), now.AddSeconds(6));
        var secondStart = Seal(till, PosEventIngestion.CashKind, CountStarted(till, second, now.AddSeconds(7)), now.AddSeconds(7));
        var secondCount = Seal(till, PosEventIngestion.CashKind, Counted(till, second, now.AddSeconds(8), 4000, 4000, 0), now.AddSeconds(8));
        var recount = Seal(till, PosEventIngestion.CashKind, Counted(till, second, now.AddSeconds(9), 5000, 5000, 0), now.AddSeconds(9));
        var again = await OkAsync(await PostEventsAsync(device, new { events = new[] { secondOpen, secondStart, secondCount, recount } }));
        (await WithDbAsync(db => db.CashSessions.AsNoTracking().SingleAsync(s => s.Id == second))).Status.Should().Be(CashSessionStatus.PendingReview);
        (await NotificationsAsync("Fechamento do caixa 01")).Should().NotBeEmpty();
        Outcomes(again)[IdOf(recount)].Should().Be("Quarantined", "recontar exige o supervisor");

        // A abertura seguinte transfere só a diferença para o fundo que ficou na gaveta (R$ 50 − R$ 100: R$ 50 voltam ao Cofre).
        await DrainAsync();
        var opening = await WithDbAsync(db => db.CashMovements.AsNoTracking().SingleAsync(m => m.SessionId == second && m.Kind == CashMovementKind.Opening));
        var back = await WithDbAsync(db => db.BankTransfers.AsNoTracking().SingleAsync(t => t.PosCashMovementId == opening.Id));
        (back.FromAccountId, back.Amount).Should().Be((drawer, 50m));
    }

    [Fact]
    public async Task Esperado_so_para_quem_aprova_operador_nao_aprova_nem_reabre_e_so_a_ultima_sessao_reabre()
    {
        await CommerceAsync(true);
        var (_, owner) = await OwnerAsync();
        var (_, register, _) = await StoreAsync(owner);
        var (operatorId, operatorClient) = await StaffAsync("sales.view", "sales.approve");
        var (_, manager) = await StaffAsync("sales.view", "sales.approve");
        var (_, viewer) = await StaffAsync("sales.view");
        var now = DateTimeOffset.UtcNow;
        var first = await CountedSessionAsync(register, operatorId, now.AddMinutes(-30), openingCents: 10000, countedCents: 8000);

        // CA 3: sem sales.approve, a API não devolve o esperado nem a diferença (nem na lista, nem na via do gerente).
        var hidden = await OkAsync(await viewer.GetAsync($"/api/v1/pos/cash-sessions/{first}"));
        // M3 da revisão: sem sales.approve, em conferência e fechado são o mesmo "Enviado" (nem o filtro nem o total revelam a quebra).
        (hidden.GetProperty("status").GetString(), hidden.GetProperty("statusLabel").GetString()).Should().Be(("Closed", "Enviado"));
        var viewerList = await OkAsync(await viewer.GetAsync($"/api/v1/pos/cash-sessions?registerId={register}&status=PendingReview"));
        viewerList.GetProperty("pendingReview").GetInt32().Should().Be(0);
        viewerList.GetProperty("items").EnumerateArray().Should().ContainSingle().Which.GetProperty("statusLabel").GetString().Should().Be("Enviado");

        // B3: a exportação da lista fica na auditoria, como o PDF.
        (await viewer.GetAsync($"/api/v1/pos/cash-sessions/export?registerId={register}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await WithDbAsync(db => db.AuditEntries.AsNoTracking().CountAsync(a => a.Entity == nameof(CashSession) && a.EntityId == "lista")))
            .Should().BeGreaterThan(0);

        // M2: quem operou o caixa não confere o Pix dele.
        (await operatorClient.PostAsJsonAsync($"/api/v1/pos/cash-sessions/{first}/pix-check", new { paymentId = Guid.CreateVersion7(), status = "Found" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        hidden.GetProperty("expectedVisible").GetBoolean().Should().BeFalse();
        hidden.GetProperty("lines")[0].GetProperty("expected").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        hidden.GetProperty("lines")[0].GetProperty("difference").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        (await viewer.GetAsync($"/api/v1/pos/cash-sessions/{first}/report.pdf?copy=manager")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var shown = await OkAsync(await manager.GetAsync($"/api/v1/pos/cash-sessions/{first}"));
        shown.GetProperty("lines")[0].GetProperty("difference").GetDecimal().Should().Be(-20m);

        // CA 8: quem operou o caixa não aprova o próprio fechamento; outro gerente aprova com motivo.
        var own = await operatorClient.PostAsJsonAsync($"/api/v1/pos/cash-sessions/{first}/approve", new { reason = "Conferi", version = shown.GetProperty("version").GetUInt32() });
        own.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(own)).Should().Be("own_cash_session");
        var approved = await OkAsync(await manager.PostAsJsonAsync($"/api/v1/pos/cash-sessions/{first}/approve", new { reason = "Falta de troco conferida", version = shown.GetProperty("version").GetUInt32() }));
        approved.GetProperty("status").GetString().Should().Be("Closed");

        // M2: no caixa fechado a quebra já saiu; conferir o Pix exige reabrir.
        var closedPix = await manager.PostAsJsonAsync($"/api/v1/pos/cash-sessions/{first}/pix-check", new { paymentId = Guid.CreateVersion7(), status = "Found" });
        closedPix.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(closedPix)).Should().Be("cash_session_closed");

        // CA 10: só a última sessão do caixa reabre; quem operou não reabre; a reabertura avisa o Proprietário e devolve o recolhimento à gaveta.
        var second = await CountedSessionAsync(register, operatorId, now.AddMinutes(-10), openingCents: 5000, countedCents: 15000);
        var secondView = await OkAsync(await manager.GetAsync($"/api/v1/pos/cash-sessions/{second}"));
        secondView.GetProperty("status").GetString().Should().Be("PendingReview", "R$ 100,00 de sobra");
        var closedFirst = await OkAsync(await manager.GetAsync($"/api/v1/pos/cash-sessions/{first}"));
        var notLast = await manager.PostAsJsonAsync($"/api/v1/pos/cash-sessions/{first}/reopen", new { reason = "Recontar", version = closedFirst.GetProperty("version").GetUInt32() });
        notLast.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var approvedSecond = await OkAsync(await manager.PostAsJsonAsync($"/api/v1/pos/cash-sessions/{second}/approve", new { reason = "Sobra conferida", version = secondView.GetProperty("version").GetUInt32() }));
        (await operatorClient.PostAsJsonAsync($"/api/v1/pos/cash-sessions/{second}/reopen", new { reason = "Recontar", version = approvedSecond.GetProperty("version").GetUInt32() }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var reopened = await OkAsync(await manager.PostAsJsonAsync($"/api/v1/pos/cash-sessions/{second}/reopen", new { reason = "Contagem errada", version = approvedSecond.GetProperty("version").GetUInt32() }));
        reopened.GetProperty("status").GetString().Should().Be("Open");
        reopened.GetProperty("reopenCount").GetInt32().Should().Be(1);
        (await WithDbAsync(db => db.CashMovements.AsNoTracking().CountAsync(m => m.SessionId == second && m.Kind == CashMovementKind.DepositReturn && m.Amount == 100m))).Should().Be(1);
        (await NotificationsAsync("Caixa 01 da loja")).Should().Contain(t => t.Contains("reaberto"));
        (await OutboxAsync("CashSessionReopened")).Count(p => p.Contains(second.ToString(), StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task Fechamento_sem_contagem_da_CX_01_vai_para_a_quarentena_e_a_sessao_continua_aberta()
    {
        var (_, till) = await TillAsync();
        using var device = till.Device;
        var now = DateTimeOffset.UtcNow;
        var session = Guid.CreateVersion7();
        var open = Seal(till, PosEventIngestion.CashKind, OpenCash(till, session, now.AddSeconds(-2)), now.AddSeconds(-2));
        var legacy = Seal(till, PosEventIngestion.CashKind, new { type = "CashSessionClosed", sessionId = session, operatorId = till.OperatorId, at = now.AddSeconds(-1) }, now.AddSeconds(-1));
        var result = await OkAsync(await PostEventsAsync(device, new { events = new[] { open, legacy } }));

        Outcomes(result)[IdOf(legacy)].Should().Be("Quarantined");
        (await DivergencesAsync(IdOf(legacy))).Should().ContainSingle(d => d.Kind == SyncDivergenceKind.CashSession && d.Summary == PosEventIngestion.LegacyClosedMessage);
        (await WithDbAsync(db => db.CashSessions.AsNoTracking().SingleAsync(s => s.Id == session))).Status.Should().Be(CashSessionStatus.Open);
    }

    [Fact]
    public async Task Depois_da_reabertura_so_as_vendas_novas_contam_contra_o_numero_do_aparelho()
    {
        await CommerceAsync(true);
        var (_, owner) = await OwnerAsync();
        var (_, register, _) = await StoreAsync(owner);
        var (operatorId, _) = await StaffAsync("sales.view");
        var (_, manager) = await StaffAsync("sales.view", "sales.approve");
        var now = DateTimeOffset.UtcNow;
        var session = await CountedSessionAsync(register, operatorId, now.AddMinutes(-30), openingCents: 10000, countedCents: 10000);
        await WithDbAsync(async db =>
        {
            db.SessionSales.Add(new SessionSale { SaleId = Guid.CreateVersion7(), SessionId = session, CashAmount = 0m, CreatedAt = now.AddMinutes(-20) });
            return await db.SaveChangesAsync();
        });
        var view = await OkAsync(await manager.GetAsync($"/api/v1/pos/cash-sessions/{session}"));
        await OkAsync(await manager.PostAsJsonAsync($"/api/v1/pos/cash-sessions/{session}/reopen", new { reason = "Contagem errada", version = view.GetProperty("version").GetUInt32() }));

        // O aparelho zerou a contagem ao retomar: 1 venda nova. A venda de antes da reabertura não conta: a sessão espera a nova.
        await using (var scope = await ScopeAsync())
        {
            var closing = scope.ServiceProvider.GetRequiredService<CashClosingService>();
            var at = DateTimeOffset.UtcNow;
            (await closing.StartCountAsync(session, at)).IsSuccess.Should().BeTrue();
            (await closing.CountAsync(new CashCountInput(session, Guid.CreateVersion7(), operatorId, at, 10000, Guid.CreateVersion7(), 1,
                [new CashCountLineInput(null, 10000, null)]))).IsSuccess.Should().BeTrue();
        }

        var waiting = await OkAsync(await manager.GetAsync($"/api/v1/pos/cash-sessions/{session}"));
        (waiting.GetProperty("status").GetString(), waiting.GetProperty("awaitingSales").GetBoolean()).Should().Be(("Counted", true));
        await WithDbAsync(async db =>
        {
            db.SessionSales.Add(new SessionSale { SaleId = Guid.CreateVersion7(), SessionId = session, CashAmount = 0m, CreatedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        });
        await using (var scope = await ScopeAsync())
        {
            await scope.ServiceProvider.GetRequiredService<CashClosingService>().OnSaleArrivedAsync(session);
        }

        (await WithDbAsync(db => db.CashSessions.AsNoTracking().SingleAsync(s => s.Id == session))).Status.Should().Be(CashSessionStatus.Closed);
    }

    private Task<List<string>> OutboxAsync(string type) =>
        WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.EndsWith(type)).Select(m => m.Payload).ToListAsync());

    /// <summary>Sessão aberta e contada direto pelos serviços (sem aparelho): dinheiro sem forma, fundo que fica = o de abertura.</summary>
    private async Task<Guid> CountedSessionAsync(Guid register, Guid operatorId, DateTimeOffset at, long openingCents, long countedCents)
    {
        var session = Guid.CreateVersion7();
        (await CashAsync(s => s.OpenAsync(new CashSessionOpenInput(session, register, 1, operatorId, openingCents / 100m, at, Guid.CreateVersion7())))).IsSuccess.Should().BeTrue();
        await using var scope = await ScopeAsync();
        var closing = scope.ServiceProvider.GetRequiredService<CashClosingService>();
        (await closing.StartCountAsync(session, at.AddMinutes(1))).IsSuccess.Should().BeTrue();
        var counted = await closing.CountAsync(new CashCountInput(session, Guid.CreateVersion7(), operatorId, at.AddMinutes(2), Math.Min(openingCents, countedCents), Guid.CreateVersion7(), 0,
            [new CashCountLineInput(null, countedCents, null)]));
        counted.IsSuccess.Should().BeTrue(counted.Error?.Message);
        return session;
    }

    /// <summary>Usuário completo (não Proprietário) num grupo próprio com as ações pedidas, com o id.</summary>
    private async Task<(Guid Id, HttpClient Client)> StaffAsync(params string[] grants)
    {
        var (userId, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "caixa" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = Unique("Loja ", 6), Description = "teste", CreatedAt = DateTimeOffset.UtcNow };
            db.Groups.Add(group);
            var split = grants.Select(p => p.Split('.')).ToList();
            foreach (var module in split.Select(g => g[0]).Distinct())
            {
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
            }

            db.GroupPermissions.AddRange(split.Select(g => new GroupGrant { GroupId = group.Id, ModuleCode = g[0], Action = g[1] }));
            db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = group.Id });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([userId]);
        }

        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: userId, stamp: stamp));
        return (userId, client);
    }
}
