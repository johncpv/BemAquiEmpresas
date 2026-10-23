using System.Text.Json;
using BemAqui.Host.Persistence;
using BemAqui.Inventory.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>EST-01a CA 13 a 19 e 25 a 27: motor, trava, concorrência, custo médio, contratos e eventos.</summary>
public partial class StockTests
{
    [Fact]
    public async Task PostAsync_sem_transacao_lanca_e_nada_grava()
    {
        var (item, location) = (await ItemAsync(), await LocationAsync());
        await using var scope = await ScopeAsync();
        var ledger = scope.ServiceProvider.GetRequiredService<IStockMovements>();
        var act = () => ledger.PostAsync(new StockPosting(StockMovementType.Receipt, item, location, null, 1m, 10m, StockSources.GoodsReceipt, Guid.NewGuid()));
        await act.Should().ThrowAsync<InvalidOperationException>("o chamador é dono da transação (CA 17)");
        (await WithDbAsync(db => db.StockMovements.CountAsync(m => m.ItemId == item))).Should().Be(0);
    }

    [Fact]
    public async Task Saida_acima_do_fisico_da_409_e_nada_grava()
    {
        var (item, location) = (await ItemAsync(), await LocationAsync());
        (await ReceiveAsync(item, location, 5m, 10m)).IsSuccess.Should().BeTrue();
        var refused = await IssueAsync(item, location, 6m);
        refused.Error!.Code.Should().Be("insufficient_stock");
        refused.Error.Message.Should().StartWith("Saldo insuficiente: há 5,0000 UN de ");
        (await StateAsync(item, location)).OnHand.Should().Be(5m);
        (await WithDbAsync(db => db.StockMovements.CountAsync(m => m.ItemId == item))).Should().Be(1);
    }

    [Fact]
    public async Task Vinte_saidas_simultaneas_da_ultima_unidade_so_uma_grava()
    {
        var (item, location) = (await ItemAsync(), await LocationAsync());
        (await ReceiveAsync(item, location, 1m, 10m)).IsSuccess.Should().BeTrue();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => IssueAsync(item, location, 1m)));
        results.Count(r => r.IsSuccess).Should().Be(1, "CA 14");
        results.Where(r => r.IsFailure).Should().OnlyContain(r => r.Error!.Code == "insufficient_stock");
        (await StateAsync(item, location)).OnHand.Should().Be(0m);
    }

    [Fact]
    public async Task Dois_documentos_com_os_mesmos_itens_em_ordem_inversa_nao_travam()
    {
        var (a, b, location) = (await ItemAsync(), await ItemAsync(), await LocationAsync());
        (await ReceiveAsync(a, location, 100m, 1m)).IsSuccess.Should().BeTrue();
        (await ReceiveAsync(b, location, 100m, 1m)).IsSuccess.Should().BeTrue();

        async Task<bool> DocumentAsync(Guid first, Guid second)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var source = Guid.NewGuid();
            var result = await scope.ServiceProvider.GetRequiredService<IStockMovements>().PostManyAsync([
                new StockPosting(StockMovementType.Issue, first, location, null, -1m, null, StockSources.Requisition, source, 1),
                new StockPosting(StockMovementType.Issue, second, location, null, -1m, null, StockSources.Requisition, source, 2),
            ]);
            await transaction.CommitAsync();
            return result.IsSuccess;
        }

        var runs = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => i % 2 == 0 ? DocumentAsync(a, b) : DocumentAsync(b, a)));
        runs.Should().OnlyContain(ok => ok, "a trava segue item_id crescente: sem deadlock (CA 14)");
        (await StateAsync(a, location)).OnHand.Should().Be(90m);
        (await StateAsync(b, location)).OnHand.Should().Be(90m);
    }

    [Fact]
    public async Task Dez_primeiras_entradas_paralelas_criam_uma_posicao_e_um_lote_inclusive_sem_lote()
    {
        var (plain, lotted, location) = (await ItemAsync(), await ItemAsync(tracksLot: true), await LocationAsync());
        Guid lot;
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            lot = await scope.ServiceProvider.GetRequiredService<IStockMovements>().EnsureLotAsync(lotted, "L-001");
            await transaction.CommitAsync();
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ReceiveAsync(plain, location, 1m, 10m))
            .Concat(Enumerable.Range(0, 10).Select(_ => ReceiveAsync(lotted, location, 1m, 10m, lot))));
        results.Should().OnlyContain(r => r.IsSuccess);
        (await WithDbAsync(db => db.StockPositions.CountAsync(p => p.ItemId == plain && p.LocationId == location))).Should().Be(1, "UNIQUE (item_id, location_id)");
        (await WithDbAsync(db => db.StockBalances.CountAsync(b => b.ItemId == plain && b.LocationId == location && b.LotId == null))).Should().Be(1, "NULLS NOT DISTINCT com lot_id nulo (CA 15)");
        (await WithDbAsync(db => db.StockBalances.CountAsync(b => b.ItemId == lotted && b.LotId == lot))).Should().Be(1);
        (await StateAsync(plain, location)).OnHand.Should().Be(10m);
        (await StateAsync(lotted, location)).OnHand.Should().Be(10m);
    }

    [Fact]
    public async Task Dez_entradas_paralelas_com_custos_diferentes_dao_o_mesmo_medio_do_calculo_em_serie()
    {
        var (item, a, b) = (await ItemAsync(), await LocationAsync(), await LocationAsync());
        var costs = Enumerable.Range(1, 10).Select(i => 10m + i * 1.37m).ToList();
        var results = await Task.WhenAll(costs.Select((c, i) => ReceiveAsync(item, i % 2 == 0 ? a : b, 3m, c)));
        results.Should().OnlyContain(r => r.IsSuccess);
        // Só entradas a partir do zero: a ordem não muda a média ponderada (Σ q × c ÷ Σ q), salvo o arredondamento de cada gravação.
        var expected = costs.Sum(c => 3m * c) / 30m;
        var average = (await StateAsync(item, a)).Average;
        average.Should().BeApproximately(expected, 0.00005m, "nenhuma atualização perdida (CA 16)");
        var sumOfLots = await WithDbAsync(db => db.StockBalances.Where(x => x.ItemId == item).SumAsync(x => x.QuantityOnHand));
        var sumOfPositions = await WithDbAsync(db => db.StockPositions.Where(x => x.ItemId == item).SumAsync(x => x.QuantityOnHand));
        sumOfLots.Should().Be(sumOfPositions).And.Be(30m, "Σ lotes = posição (CA 16)");
    }

    [Fact]
    public async Task Dataset_de_30_movimentos_no_banco_bate_com_a_planilha_e_o_kardex_segue_a_ordem_de_lancamento()
    {
        var (item, a, b) = (await ItemAsync(), await LocationAsync(), await LocationAsync());
        var ids = new List<Guid>();
        foreach (var step in StockDataset.Steps)
        {
            var source = Guid.NewGuid();
            var result = step.Kind switch
            {
                StepKind.Receipt => await ReceiveAsync(item, a, step.Quantity, step.Cost, source: source),
                StepKind.Issue => await IssueAsync(item, a, step.Quantity, source: source),
                StepKind.Transfer => await TransferAsync(item, a, b, step.Quantity, source),
                _ => await ReverseAsync(ids[step.Of]),
            };
            result.IsSuccess.Should().BeTrue($"passo {step.Number}: {result.Error?.Message}");
            ids.Add(result.Value.MovementId);
        }

        var average = (await StateAsync(item, a)).Average;
        average.Should().Be(StockDataset.ExpectedFinalAverage, "CA 18: o custo médio do banco bate com a planilha");
        var total = await WithDbAsync(db => db.StockPositions.Where(p => p.ItemId == item).SumAsync(p => p.QuantityOnHand));
        total.Should().Be(StockDataset.ExpectedFinalOnHand);

        var (_, owner) = await ActorAsync("owner");
        var kardex = await OkAsync(await owner.GetAsync($"{Base}/items/{item}/movements?pageSize=100"));
        var rows = kardex.GetProperty("items").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("postedSeq").GetInt64()).Should().BeInAscendingOrder("kardex pela ordem de lançamento (CA 24)");
        rows.Last().GetProperty("averageCostAfter").GetDecimal().Should().Be(StockDataset.ExpectedFinalAverage);
        rows.Where(r => r.GetProperty("locationId").GetGuid() == a).Last().GetProperty("balanceAfter").GetDecimal()
            .Should().Be(await WithDbAsync(db => db.StockPositions.Where(p => p.ItemId == item && p.LocationId == a).Select(p => p.QuantityOnHand).SingleAsync()));

        async Task<BemAqui.SharedKernel.Result<StockMovementResult>> TransferAsync(Guid i, Guid from, Guid to, decimal q, Guid source)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var legs = await scope.ServiceProvider.GetRequiredService<IStockMovements>().PostManyAsync([
                new StockPosting(StockMovementType.Transfer, i, from, null, -q, null, StockSources.Adjustment, source, 1),
                new StockPosting(StockMovementType.Transfer, i, to, null, q, null, StockSources.Adjustment, source, 2),
            ]);
            await transaction.CommitAsync();
            return legs.IsSuccess ? legs.Value[0] : legs.Error!;
        }

        async Task<BemAqui.SharedKernel.Result<StockMovementResult>> ReverseAsync(Guid movement)
        {
            var original = await WithDbAsync(db => db.StockMovements.AsNoTracking().SingleAsync(m => m.Id == movement));
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var reversed = await scope.ServiceProvider.GetRequiredService<IStockMovements>().ReverseAsync(new StockReversal(movement, original.SourceType, original.SourceId, "Estorno do dataset"));
            await transaction.CommitAsync();
            return reversed;
        }
    }

    [Fact]
    public async Task Mesma_perna_do_mesmo_documento_nao_lanca_duas_vezes()
    {
        var (item, location) = (await ItemAsync(), await LocationAsync());
        var source = Guid.NewGuid();
        var first = await ReceiveAsync(item, location, 2m, 10m, source: source);
        var again = await ReceiveAsync(item, location, 2m, 10m, source: source);
        again.Value.MovementId.Should().Be(first.Value.MovementId, "CA 25: idempotência por (origem, perna, tipo)");
        again.Value.Replayed.Should().BeTrue();
        var otherLeg = await PostAsync(new StockPosting(StockMovementType.Receipt, item, location, null, 2m, 10m, StockSources.GoodsReceipt, source, Leg: 2));
        otherLeg.Value.MovementId.Should().NotBe(first.Value.MovementId, "outra perna do mesmo documento lança");
        (await StateAsync(item, location)).OnHand.Should().Be(4m);
    }

    [Fact]
    public async Task Saida_com_projeto_publica_StockIssued_com_valor_anterior_e_novo_e_o_estorno_volta_a_zero()
    {
        var (item, location) = (await ItemAsync(), await LocationAsync());
        var (project, order) = (Guid.NewGuid(), Guid.NewGuid());
        (await ReceiveAsync(item, location, 10m, 12.5m)).IsSuccess.Should().BeTrue();
        var source = Guid.NewGuid();
        var issued = await IssueAsync(item, location, 4m, project: project, order: order, source: source);
        issued.IsSuccess.Should().BeTrue();
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await scope.ServiceProvider.GetRequiredService<IStockMovements>().ReverseAsync(new StockReversal(issued.Value.MovementId, StockSources.Requisition, source))).IsSuccess.Should().BeTrue();
            await transaction.CommitAsync();
        }

        // Busca pelo tipo e filtra o conteúdo na memória (LIKE em jsonb quebra no Postgres).
        var payloads = (await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.EndsWith("StockIssued")).Select(m => m.Payload).ToListAsync()))
            .Select(p => JsonDocument.Parse(p).RootElement).Where(p => p.GetProperty("MovementId").GetGuid() == issued.Value.MovementId).OrderBy(p => p.GetProperty("Seq").GetInt32()).ToList();
        payloads.Should().HaveCount(2);
        payloads[0].GetProperty("PreviousAmount").GetDecimal().Should().Be(0m);
        payloads[0].GetProperty("NewAmount").GetDecimal().Should().Be(50m, "4 × 12,50");
        payloads[0].GetProperty("ProjectId").GetGuid().Should().Be(project);
        payloads[0].GetProperty("ProductionOrderId").GetGuid().Should().Be(order);
        payloads[1].GetProperty("PreviousAmount").GetDecimal().Should().Be(50m);
        payloads[1].GetProperty("NewAmount").GetDecimal().Should().Be(0m);
        payloads[0].EnumerateObject().Select(p => p.Name).Should().NotContain(n => n.Contains("User", StringComparison.Ordinal), "sem dado pessoal");
    }

    [Fact]
    public async Task CostByOrder_soma_saidas_menos_devolucoes_e_estornos_da_OS()
    {
        var (a, b, location) = (await ItemAsync(), await ItemAsync(), await LocationAsync());
        var order = Guid.NewGuid();
        (await ReceiveAsync(a, location, 10m, 10m)).IsSuccess.Should().BeTrue();
        (await ReceiveAsync(b, location, 10m, 3m)).IsSuccess.Should().BeTrue();
        (await IssueAsync(a, location, 4m, order: order)).IsSuccess.Should().BeTrue();
        (await IssueAsync(b, location, 5m, order: order)).IsSuccess.Should().BeTrue();
        (await PostAsync(new StockPosting(StockMovementType.Return, a, location, null, 1m, 10m, StockSources.Requisition, Guid.NewGuid(), ProductionOrderId: order))).IsSuccess.Should().BeTrue();
        await using var scope = await ScopeAsync();
        var cost = await scope.ServiceProvider.GetRequiredService<IStockMovements>().CostByOrderAsync(order);
        cost.Lines.Single(l => l.ItemId == a).Should().BeEquivalentTo(new { Quantity = 3m, Amount = 30m });
        cost.Lines.Single(l => l.ItemId == b).Should().BeEquivalentTo(new { Quantity = 5m, Amount = 15m });
        cost.Total.Should().Be(45m, "CA 27");
    }

    [Fact]
    public async Task IStockBalances_le_fisico_reservado_e_disponivel_da_posicao_em_lote()
    {
        var (a, b, l1, l2) = (await ItemAsync(), await ItemAsync(), await LocationAsync(), await LocationAsync());
        (await ReceiveAsync(a, l1, 7m, 1m)).IsSuccess.Should().BeTrue();
        (await ReceiveAsync(a, l2, 3m, 1m)).IsSuccess.Should().BeTrue();
        (await ReceiveAsync(b, l1, 2m, 1m)).IsSuccess.Should().BeTrue();
        await using var scope = await ScopeAsync();
        var balances = scope.ServiceProvider.GetRequiredService<IStockBalances>();
        var all = await balances.GetAsync([a, b]);
        all.Should().HaveCount(3).And.OnlyContain(x => x.Reserved == 0m && x.Available == x.OnHand);
        (await balances.GetAsync([a], l2)).Should().ContainSingle().Which.OnHand.Should().Be(3m);
        typeof(StockAvailability).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("Cost", StringComparison.Ordinal), "sem custo (CA 26)");
    }

    [Fact]
    public async Task Os_CHECKs_do_banco_recusam_saldo_negativo_e_reserva_acima_do_fisico()
    {
        var (item, location) = (await ItemAsync(), await LocationAsync());
        (await ReceiveAsync(item, location, 1m, 1m)).IsSuccess.Should().BeTrue();
        var negative = () => WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE inventory.stock_positions SET quantity_on_hand = -1 WHERE item_id = {item}"));
        await negative.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.SqlState == Npgsql.PostgresErrorCodes.CheckViolation);
        var reserved = () => WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE inventory.stock_positions SET quantity_reserved = 2 WHERE item_id = {item}"));
        await reserved.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.SqlState == Npgsql.PostgresErrorCodes.CheckViolation);
        var lot = () => WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE inventory.stock_balances SET quantity_on_hand = -1 WHERE item_id = {item}"));
        await lot.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.SqlState == Npgsql.PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task O_trigger_recusa_qualquer_UPDATE_e_DELETE_no_movimento()
    {
        var (item, location) = (await ItemAsync(), await LocationAsync());
        var movement = (await ReceiveAsync(item, location, 1m, 1m)).Value.MovementId;
        var update = () => WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE inventory.stock_movements SET reason = 'x' WHERE id = {movement}"));
        await update.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.MessageText.Contains("não muda nem é apagado"));
        var delete = () => WithDbAsync(db => db.Database.ExecuteSqlAsync($"DELETE FROM inventory.stock_movements WHERE id = {movement}"));
        await delete.Should().ThrowAsync<Npgsql.PostgresException>();
        (await WithDbAsync(db => db.StockMovements.CountAsync(m => m.Id == movement))).Should().Be(1);
    }
}
