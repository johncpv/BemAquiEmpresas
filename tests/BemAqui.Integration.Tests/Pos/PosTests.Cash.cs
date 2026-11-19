using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Host.Persistence;
using BemAqui.Pos.Application;
using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// CX-01: sessão de caixa e movimentos de dinheiro gravados no servidor (o que a subida da OFF-02 chama) e as transferências do Financeiro.
/// CA 2: uma sessão aberta por caixa. CA 9: abertura, sangria e suprimento viram transferências Cofre ↔ Caixa do PDV, idempotentes.
/// </summary>
public partial class PosTests
{
    [Fact]
    public async Task Uma_sessao_aberta_por_caixa_com_quem_abriu_e_desde_quando()
    {
        await CommerceAsync(true);
        var (ownerId, owner) = await OwnerAsync(name: "Paula Lima");
        var (_, register, _) = await StoreAsync(owner);
        var (otherId, _) = await OwnerAsync(name: "Rui Souza");
        var first = Guid.CreateVersion7();
        var openedAt = new DateTimeOffset(2026, 11, 18, 11, 2, 0, TimeSpan.Zero); // 08:02 em Brasília

        var opened = await CashAsync(s => s.OpenAsync(new CashSessionOpenInput(first, register, 1, ownerId, 150m, openedAt, Guid.CreateVersion7())));
        opened.IsSuccess.Should().BeTrue(opened.Error?.Message);
        opened.Value.Status.Should().Be(CashSessionStatus.Open);
        opened.Value.OperatorId.Should().Be(ownerId);

        // CA 2: outro operador no mesmo caixa recebe quem abriu e desde quando.
        var second = Guid.CreateVersion7();
        var refused = await CashAsync(s => s.OpenAsync(new CashSessionOpenInput(second, register, 1, otherId, 50m, openedAt.AddHours(1), Guid.CreateVersion7())));
        refused.IsFailure.Should().BeTrue();
        refused.Error!.Code.Should().Be("cash_session_open");
        refused.Error.Message.Should().Be("Este caixa já está aberto por Paula Lima desde 08:02.");

        // Reenvio da mesma abertura: a mesma sessão, sem duplicar.
        var replay = await CashAsync(s => s.OpenAsync(new CashSessionOpenInput(first, register, 1, ownerId, 150m, openedAt, Guid.CreateVersion7())));
        replay.IsSuccess.Should().BeTrue();
        replay.Value.Replayed.Should().BeTrue();
        (await WithDbAsync(db => db.CashSessions.CountAsync(s => s.RegisterId == register))).Should().Be(1);

        // Fechada a primeira, o caixa abre de novo; o fechamento também é idempotente.
        (await CashAsync(s => s.CloseAsync(first, ownerId, openedAt.AddHours(8)))).IsSuccess.Should().BeTrue();
        (await CashAsync(s => s.CloseAsync(first, ownerId, openedAt.AddHours(8)))).IsSuccess.Should().BeTrue();
        var reopened = await CashAsync(s => s.OpenAsync(new CashSessionOpenInput(second, register, 1, otherId, 50m, openedAt.AddHours(9), Guid.CreateVersion7())));
        reopened.IsSuccess.Should().BeTrue(reopened.Error?.Message);

        // Auditoria da abertura.
        (await WithDbAsync(db => db.AuditEntries.AsNoTracking().CountAsync(a => a.Entity == nameof(CashSession) && a.EntityId == first.ToString()))).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Abertura_sangria_e_suprimento_viram_transferencias_do_Cofre_ligadas_ao_movimento_sem_duplicar()
    {
        await CommerceAsync(true);
        var (ownerId, owner) = await OwnerAsync();
        var (_, register, _) = await StoreAsync(owner);
        var drawer = await WithDbAsync(db => db.Registers.Where(r => r.Id == register).Select(r => r.CashAccountId).SingleAsync());
        var session = Guid.CreateVersion7();
        var openingMovement = Guid.CreateVersion7();
        var cashOut = Guid.CreateVersion7();
        var cashIn = Guid.CreateVersion7();
        var at = DateTimeOffset.UtcNow.AddMinutes(-30);

        (await CashAsync(s => s.OpenAsync(new CashSessionOpenInput(session, register, 1, ownerId, 150m, at, openingMovement)))).IsSuccess.Should().BeTrue();

        // Sangria e suprimento exigem operador, motivo e valor positivo; sangria acima do calculado é aceita e fica marcada (CA 4).
        (await CashAsync(s => s.RecordMovementAsync(new CashMovementInput(Guid.CreateVersion7(), session, CashMovementKind.CashOut, 10m, "  ", ownerId, at, false))))
            .Error!.Code.Should().Be("reason_required");
        (await CashAsync(s => s.RecordMovementAsync(new CashMovementInput(Guid.CreateVersion7(), session, CashMovementKind.CashOut, 10m, "Cofre", Guid.Empty, at, false))))
            .Error!.Code.Should().Be("operator_required");
        (await CashAsync(s => s.RecordMovementAsync(new CashMovementInput(Guid.CreateVersion7(), session, CashMovementKind.CashIn, 0m, "Troco", ownerId, at, false))))
            .Error!.Code.Should().Be("invalid_amount");
        var drop = await CashAsync(s => s.RecordMovementAsync(new CashMovementInput(cashOut, session, CashMovementKind.CashOut, 400m, "Depósito no banco", ownerId, at.AddMinutes(5), true)));
        drop.IsSuccess.Should().BeTrue(drop.Error?.Message);
        drop.Value.AboveExpected.Should().BeTrue();
        (await CashAsync(s => s.RecordMovementAsync(new CashMovementInput(cashIn, session, CashMovementKind.CashIn, 50m, "Troco", ownerId, at.AddMinutes(10), false)))).IsSuccess.Should().BeTrue();

        // Reenvio: nada novo.
        var again = await CashAsync(s => s.RecordMovementAsync(new CashMovementInput(cashOut, session, CashMovementKind.CashOut, 400m, "Depósito no banco", ownerId, at.AddMinutes(5), true)));
        again.Value.Replayed.Should().BeTrue();
        await DrainAsync();

        var transfers = await WithDbAsync(db => db.BankTransfers.AsNoTracking()
            .Where(t => t.PosCashMovementId == openingMovement || t.PosCashMovementId == cashOut || t.PosCashMovementId == cashIn).ToListAsync());
        transfers.Should().HaveCount(3);
        var opening = transfers.Single(t => t.PosCashMovementId == openingMovement);
        var safe = opening.FromAccountId;
        opening.ToAccountId.Should().Be(drawer);
        opening.Amount.Should().Be(150m);
        (await WithDbAsync(db => db.BankAccounts.Where(a => a.Id == safe).Select(a => a.Name).SingleAsync())).Should().Be(PosCashMovementConsumer.SafeName);
        var dropTransfer = transfers.Single(t => t.PosCashMovementId == cashOut);
        (dropTransfer.FromAccountId, dropTransfer.ToAccountId, dropTransfer.Amount).Should().Be((drawer, safe, 400m));
        dropTransfer.Description.Should().Be("Sangria do caixa 01: Depósito no banco");
        var supply = transfers.Single(t => t.PosCashMovementId == cashIn);
        (supply.FromAccountId, supply.ToAccountId, supply.Amount).Should().Be((safe, drawer, 50m));
        var ids = transfers.Select(t => (Guid?)t.Id).ToList();
        (await WithDbAsync(db => db.FinancialTransactions.CountAsync(t => ids.Contains(t.TransferId)))).Should().Be(6, "duas pernas por transferência");

        // Reprocessar o evento (outra entrega) não duplica a transferência.
        await using (var scope = await ScopeAsync())
        {
            var evt = new CashMovementRecorded(cashOut, session, Guid.Empty, register, 1, drawer, CashMovementRecorded.CashOut, 400m, "Depósito no banco",
                DateOnly.FromDateTime(at.UtcDateTime), at);
            await scope.ServiceProvider.GetRequiredService<PosCashMovementConsumer>().HandleAsync(evt, default);
        }

        (await WithDbAsync(db => db.BankTransfers.CountAsync(t => t.PosCashMovementId == cashOut))).Should().Be(1);
    }

    [Fact]
    public async Task Fundo_de_troco_zero_abre_sem_transferencia()
    {
        await CommerceAsync(true);
        var (ownerId, owner) = await OwnerAsync();
        var (_, register, _) = await StoreAsync(owner);
        var movement = Guid.CreateVersion7();
        (await CashAsync(s => s.OpenAsync(new CashSessionOpenInput(Guid.CreateVersion7(), register, 1, ownerId, 0m, DateTimeOffset.UtcNow, movement)))).IsSuccess.Should().BeTrue();
        await DrainAsync();
        (await WithDbAsync(db => db.CashMovements.CountAsync(m => m.Id == movement))).Should().Be(1);
        (await WithDbAsync(db => db.BankTransfers.CountAsync(t => t.PosCashMovementId == movement))).Should().Be(0);
    }

    private async Task<Result<T>> CashAsync<T>(Func<CashSessionIngestion, Task<Result<T>>> action)
    {
        await using var scope = await ScopeAsync();
        return await action(scope.ServiceProvider.GetRequiredService<CashSessionIngestion>());
    }

    [Fact]
    public async Task Aviso_e_teto_da_loja_gravam_no_PUT_e_voltam_na_lista()
    {
        await CommerceAsync(true);
        var (_, owner) = await OwnerAsync();
        var (store, _, code) = await StoreAsync(owner);
        var view = (await OkAsync(await owner.GetAsync("/api/v1/pos/stores"))).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == store);
        var body = new
        {
            code, name = "Loja " + code, stockLocationId = view.GetProperty("stockLocationId").GetGuid(), defaultPriceListId = view.GetProperty("defaultPriceListId").GetGuid(),
            cashDropAlertAmount = 800.50m, cashCeilingAmount = 2500m,
        };
        var saved = await OkAsync(await owner.PutAsJsonAsync($"/api/v1/pos/stores/{store}", body));
        saved.GetProperty("cashDropAlertAmount").GetDecimal().Should().Be(800.50m);
        saved.GetProperty("cashCeilingAmount").GetDecimal().Should().Be(2500m);
        (await WithDbAsync(db => db.Stores.Where(s => s.Id == store).Select(s => new { s.CashDropAlertAmount, s.CashCeilingAmount }).SingleAsync()))
            .Should().Be(new { CashDropAlertAmount = 800.50m, CashCeilingAmount = (decimal?)2500m });
    }

    [Fact]
    public async Task Cofre_nao_pode_ser_a_conta_do_caixa()
    {
        await CommerceAsync(true);
        var (_, owner) = await OwnerAsync();
        var (store, _, _) = await StoreAsync(owner);
        var safe = new BemAqui.Finance.Domain.BankAccount { Name = "Cofre", Kind = BemAqui.Finance.Contracts.BankAccountKind.Cash, CreatedAt = DateTimeOffset.UtcNow };
        await WithDbAsync(async db => { db.BankAccounts.Add(safe); return await db.SaveChangesAsync(); });
        var response = await owner.PostAsJsonAsync($"/api/v1/pos/stores/{store}/registers", new { number = 2, cashAccountId = safe.Id, uiMode = "Keyboard" });
        (await OkAsync(response, System.Net.HttpStatusCode.UnprocessableEntity)).ToString().Should().Contain("O Cofre recebe as sangrias");
    }

    [Fact]
    public async Task Horario_do_aparelho_fora_da_janela_e_recusado()
    {
        await CommerceAsync(true);
        var (ownerId, owner) = await OwnerAsync();
        var (_, register, _) = await StoreAsync(owner);
        var now = DateTimeOffset.UtcNow;
        (await CashAsync(s => s.OpenAsync(new CashSessionOpenInput(Guid.CreateVersion7(), register, 1, ownerId, 0m, now.AddMinutes(10), Guid.CreateVersion7()))))
            .Error!.Code.Should().Be("invalid_time", "à frente do servidor além da folga");
        var session = Guid.CreateVersion7();
        (await CashAsync(s => s.OpenAsync(new CashSessionOpenInput(session, register, 1, ownerId, 0m, now.AddHours(-1), Guid.CreateVersion7())))).IsSuccess.Should().BeTrue();
        (await CashAsync(s => s.RecordMovementAsync(new CashMovementInput(Guid.CreateVersion7(), session, CashMovementKind.CashIn, 10m, "Troco", ownerId, now.AddHours(-2), false))))
            .Error!.Code.Should().Be("invalid_time", "antes da abertura da sessão");
    }

    [Fact]
    public async Task Movimento_da_abertura_repetido_vira_conflito()
    {
        await CommerceAsync(true);
        var (ownerId, owner) = await OwnerAsync();
        var (_, first, _) = await StoreAsync(owner);
        var (_, second, _) = await StoreAsync(owner);
        var movement = Guid.CreateVersion7();
        (await CashAsync(s => s.OpenAsync(new CashSessionOpenInput(Guid.CreateVersion7(), first, 1, ownerId, 0m, DateTimeOffset.UtcNow, movement)))).IsSuccess.Should().BeTrue();
        (await CashAsync(s => s.OpenAsync(new CashSessionOpenInput(Guid.CreateVersion7(), second, 1, ownerId, 0m, DateTimeOffset.UtcNow, movement))))
            .Error!.Code.Should().Be("cash_movement_conflict");
    }

    [Fact]
    public async Task Transferencia_em_periodo_conciliado_entra_hoje_com_observacao_e_conta_inativa_nao_grava()
    {
        await CommerceAsync(true);
        var (ownerId, owner) = await OwnerAsync();
        var (_, register, _) = await StoreAsync(owner);
        var drawer = await WithDbAsync(db => db.Registers.Where(r => r.Id == register).Select(r => r.CashAccountId).SingleAsync());
        var at = DateTimeOffset.UtcNow.AddHours(-1);
        var businessDate = DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(at).DateTime);
        await WithDbAsync(async db =>
        {
            db.BankReconciliationClosings.Add(new BemAqui.Finance.Domain.BankReconciliationClosing { BankAccountId = drawer, UntilDate = businessDate, ClosedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        });
        var session = Guid.CreateVersion7();
        var opening = Guid.CreateVersion7();
        (await CashAsync(s => s.OpenAsync(new CashSessionOpenInput(session, register, 1, ownerId, 100m, at, opening)))).IsSuccess.Should().BeTrue();
        await DrainAsync();
        var transfer = await WithDbAsync(db => db.BankTransfers.AsNoTracking().SingleAsync(t => t.PosCashMovementId == opening));
        transfer.Date.Should().Be(DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(DateTimeOffset.UtcNow).DateTime));
        transfer.BackdateReason.Should().Contain("período já conciliado");

        // Conta do caixa inativa: nada é gravado (log de erro), sem exceção no consumidor.
        await WithDbAsync(async db => { (await db.BankAccounts.SingleAsync(a => a.Id == drawer)).IsActive = false; return await db.SaveChangesAsync(); });
        var cashIn = Guid.CreateVersion7();
        (await CashAsync(s => s.RecordMovementAsync(new CashMovementInput(cashIn, session, CashMovementKind.CashIn, 10m, "Troco", ownerId, at.AddMinutes(1), false)))).IsSuccess.Should().BeTrue();
        await DrainAsync();
        (await WithDbAsync(db => db.BankTransfers.CountAsync(t => t.PosCashMovementId == cashIn))).Should().Be(0);
        (await WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.Type.EndsWith("CashMovementRecorded") && m.Error != null))).Should().Be(0, "sem exceção no consumidor");
    }

    private async Task DrainAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        for (var round = 0; round < 60; round++)
        {
            await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
            {
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(info);
            }

            var pending = await WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= DateTimeOffset.UtcNow.AddSeconds(1)));
            if (pending == 0)
            {
                return;
            }
        }
    }
}
