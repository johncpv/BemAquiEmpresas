using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Pos.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Sales;

/// <summary>
/// CX-03, com PostgreSQL real (movimenta o financeiro): o <see cref="CashSessionClosed"/> vira os títulos da sessão (dinheiro baixado na gaveta,
/// cartão contra a adquirente por bandeira e vencimento, parcelado por parcela, Pix a conciliar sem o "Não localizado"), a quebra e o saldo da
/// conta do caixa no fundo que fica (CA 1 a 5); a segunda entrega não duplica (CA 6); a reabertura estorna e o recebimento conciliado a trava
/// (CA 7); um depósito baixa vários títulos com a diferença apontada (CA 8); o fluxo de caixa mostra o líquido na data prevista (CA 9).
/// </summary>
public partial class SalesTests
{
    private async Task InTransactionAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await action(scope.ServiceProvider);
        await transaction.CommitAsync();
    }

    private Task<decimal> BalanceAsync(Guid account) => WithDbAsync(db => db.FinancialTransactions.Where(t => t.BankAccountId == account).SumAsync(t => t.Amount));

    private static SalePaymentInput Card(Guid method, decimal amount, string brand, int installments = 1) =>
        new(Guid.CreateVersion7(), method, amount, SalePaymentStatus.Captured, PaymentIntegration.Manual, 0, Installments: installments, CardBrand: brand);

    [Fact]
    public async Task Fechamento_lanca_resumos_quebra_e_recebiveis_de_cartao_idempotente_estorna_na_reabertura_e_concilia_um_deposito_com_varios_titulos()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 50, 2m);
        var day = Today.AddDays(-3);
        var session = Guid.CreateVersion7();
        var register = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var drawer = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new { name = "Caixa PDV " + suffix, kind = "Cash" }));
        var bank = await WithDbAsync(db => db.PaymentMethods.Where(m => m.Id == methods.Credit).Select(m => m.DestinationAccountId).SingleAsync());
        var parcelled = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/payment-methods",
            new { name = "Parcelado " + suffix, type = "CreditInstallments", destinationAccountId = bank, acquirerPartyId = methods.Acquirer, nfePaymentCode = (string?)null }));
        (await owner.PostAsJsonAsync($"/api/v1/finance/payment-methods/{parcelled}/fees",
            new { brand = "Any", installments = 2, feePercent = 4m, settlementDays = 30, validFrom = day.AddDays(-30).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        SaleIngestionRequest Sale(decimal price, params SalePaymentInput[] payments) =>
            new(Guid.CreateVersion7(), SaleChannel.Pos, Unique("01-1"), "PosDevice", null, NoonOf(day), [Line(product, 1, price)], payments, RegisterId: register, CashSessionId: session,
                StockLocationId: location, FromDevice: true);

        foreach (var sale in new[]
                 {
                     Sale(50m, Paid(methods.Cash, 60m, change: 10m)), Sale(100m, Card(methods.Credit, 100m, "Visa")), Sale(90m, Card(parcelled, 90m, "Visa", 3)),
                     Sale(30m, Paid(methods.Pix, 30m)), Sale(20m, Paid(methods.Pix, 20m)),
                 })
        {
            (await IngestAsync(sale)).IsSuccess.Should().BeTrue();
        }

        await DrainAsync();
        (await WithDbAsync(db => db.Receivables.CountAsync(r => r.CashSessionId == session))).Should().Be(0, "a venda com sessão espera o fechamento");

        // CX-01 e CX-02: fundo de troco de 100 (Cofre → Caixa) e, no fechamento, o recolhimento de 45 (contado 145 − fundo que fica 100).
        CashMovementRecorded Movement(string kind, decimal amount) => new(Guid.CreateVersion7(), session, Guid.NewGuid(), register, 1, drawer, kind, amount, day, NoonOf(day));
        await InTransactionAsync(sp => sp.GetRequiredService<PosCashMovementConsumer>().HandleAsync(Movement(CashMovementRecorded.Opening, 100m), default));
        await InTransactionAsync(sp => sp.GetRequiredService<PosCashMovementConsumer>().HandleAsync(Movement(CashMovementRecorded.Deposit, 45m), default));
        var closed = new CashSessionClosed(session, Guid.NewGuid(), register, drawer, Guid.NewGuid(), NoonOf(day).AddHours(-4), NoonOf(day).AddHours(6), day, 100m, 45m, 0,
            [new CashSessionBreak(null, 150m, 145m, -5m), new CashSessionBreak(methods.Pix, 50m, 30m, -20m, 20m)]);
        await InTransactionAsync(sp => sp.GetRequiredService<CashSessionFinanceConsumer>().HandleAsync(closed, default));

        var titles = await WithDbAsync(db => db.Receivables.AsNoTracking().Where(r => r.CashSessionId == session).ToListAsync());
        var visa = titles.Single(r => r.Origin == DocumentOrigin.CardSettlement && r.GrossAmount == 100m);
        (visa.ExpectedFee, visa.NetAmount, visa.Amount, visa.DueDate, visa.PartyId, visa.Status).Should().Be((3m, 97m, 97m, (DateOnly?)day.AddDays(30), methods.Acquirer, ReceivableStatus.Open));
        var parcels = titles.Where(r => r.Origin == DocumentOrigin.CardSettlement && r.GrossAmount != 100m).OrderBy(r => r.DueDate).ToList();
        parcels.Select(p => p.DueDate).Should().Equal(day.AddDays(30), day.AddDays(60), day.AddDays(90));
        parcels.Sum(p => p.GrossAmount).Should().Be(90m);
        parcels.Sum(p => p.ExpectedFee).Should().Be(3.60m);
        var pix = titles.Single(r => r.Origin == DocumentOrigin.Sale && r.Description.StartsWith("Vendas", StringComparison.Ordinal) && r.GrossAmount == 30m);
        pix.Status.Should().Be(ReceivableStatus.Open, "o Pix conferido entra a conciliar na conta de destino");
        var lost = titles.Single(r => r.Description.StartsWith("Pix não localizado", StringComparison.Ordinal));
        (lost.Amount, lost.Status).Should().Be((20m, ReceivableStatus.Paid));
        var money = titles.Single(r => r.GrossAmount == 50m);
        money.Status.Should().Be(ReceivableStatus.Paid, "o dinheiro já está na gaveta");
        (await BalanceAsync(drawer)).Should().Be(100m, "abertura + vendas em dinheiro − quebra − recolhimento = o fundo que fica");
        var breakAccount = await WithDbAsync(db => db.BankAccounts.Where(a => a.Name == CashSessionFinanceConsumer.BreakAccountName && a.IsActive).Select(a => a.Id).FirstAsync());
        var quebraTransfer = await WithDbAsync(db => db.BankTransfers.AsNoTracking().SingleAsync(t => t.PosCashSessionId == session && t.PosCashMovementId == CashSessionFinanceConsumer.BreakMovementId(session, 0)));
        (quebraTransfer.FromAccountId, quebraTransfer.ToAccountId, quebraTransfer.Amount).Should().Be((drawer, breakAccount, 5m));

        // CA 6: a segunda entrega não duplica nada.
        var transactions = await WithDbAsync(db => db.FinancialTransactions.CountAsync());
        await InTransactionAsync(sp => sp.GetRequiredService<CashSessionFinanceConsumer>().HandleAsync(closed, default));
        (await WithDbAsync(db => db.Receivables.CountAsync(r => r.CashSessionId == session))).Should().Be(titles.Count);
        (await WithDbAsync(db => db.FinancialTransactions.CountAsync())).Should().Be(transactions);

        // Seção "No financeiro" da sessão e o filtro "Origem: Cartões"; o título de adquirente sai da visão padrão de clientes.
        var view = await JsonAsync(await owner.GetAsync($"/api/v1/finance/cash-sessions/{session}"));
        view.GetProperty("receivables").GetArrayLength().Should().Be(titles.Count);
        view.GetProperty("transfers").EnumerateArray().Should().Contain(t => t.GetProperty("amount").GetDecimal() == 5m && t.GetProperty("fromAccountName").GetString()!.StartsWith("Caixa PDV", StringComparison.Ordinal));
        var cards = await JsonAsync(await owner.GetAsync("/api/v1/finance/receivables?origin=cartoes&pageSize=200"));
        cards.GetProperty("page").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("origin").GetString()).Should().OnlyContain(o => o == "CardSettlement");
        var plain = await JsonAsync(await owner.GetAsync("/api/v1/finance/receivables?pageSize=200&q=" + visa.Number));
        plain.GetProperty("page").GetProperty("items").GetArrayLength().Should().Be(0);
        var detail = await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{visa.Id}"));
        (detail.GetProperty("cashSessionId").GetGuid(), detail.GetProperty("netAmount").GetDecimal()).Should().Be((session, 97m));

        // CA 9: o fluxo de caixa vê o recebível de cartão em aberto pelo líquido, na data prevista.
        (await WithDbAsync(db => db.Receivables.Where(r => r.Id == visa.Id).Select(r => r.Amount - r.PaidAmount).SingleAsync())).Should().Be(97m);

        // CA 7: reabrir estorna as baixas, a quebra e zera os títulos; o saldo do caixa volta (o recolhimento volta pela CX-02).
        await InTransactionAsync(sp => sp.GetRequiredService<CashSessionFinanceConsumer>().HandleAsync(new CashSessionReopened(session, Guid.NewGuid(), register, Guid.NewGuid(), DateTimeOffset.UtcNow, 1), default));
        (await WithDbAsync(db => db.Receivables.CountAsync(r => r.CashSessionId == session && r.Status != ReceivableStatus.Cancelled))).Should().Be(0);
        (await BalanceAsync(drawer)).Should().Be(55m, "abertura 100 − recolhimento 45; as vendas e a quebra saíram");
        (await WithDbAsync(db => db.BankTransfers.AsNoTracking().SingleAsync(t => t.Id == quebraTransfer.Id))).ReversedAt.Should().NotBeNull();

        // Fechar de novo (reabertura 1) refaz os títulos e a quebra.
        await InTransactionAsync(sp => sp.GetRequiredService<CashSessionFinanceConsumer>().HandleAsync(closed with { ReopenCount = 1 }, default));
        (await WithDbAsync(db => db.Receivables.CountAsync(r => r.CashSessionId == session && r.Status != ReceivableStatus.Cancelled))).Should().Be(titles.Count);
        (await BalanceAsync(drawer)).Should().Be(100m);

        // CA 8: um depósito da adquirente baixa o crédito à vista e a 1ª parcela, com R$ 1,00 a menos: a diferença fica apontada no último.
        var expected = 97m + parcels[0].NetAmount!.Value;
        var line = await WithDbAsync(async db =>
        {
            var batch = new BankStatementBatch { BankAccountId = bank, Source = StatementSource.Api, Trigger = StatementTrigger.Manual, NewCount = 1, ImportedAt = DateTimeOffset.UtcNow };
            db.BankStatementBatches.Add(batch);
            var row = new BankStatementLine
            {
                BankAccountId = bank, BatchId = batch.Id, Date = Today, Amount = expected - 1m, Description = "Depósito da adquirente", Kind = StatementLineKind.Transfer,
                ExternalId = "cx03-" + Guid.NewGuid().ToString("N"), Source = StatementSource.Api, Sequence = 0, ImportedAt = DateTimeOffset.UtcNow,
            };
            db.BankStatementLines.Add(row);
            await db.SaveChangesAsync();
            return row;
        });
        var refs = await WithDbAsync(db => db.Receivables.AsNoTracking().Where(r => r.Id == visa.Id || r.Id == parcels[0].Id).Select(r => new { id = r.Id, version = r.Version }).ToListAsync());
        var above = await owner.PostAsJsonAsync($"/api/v1/finance/bank-accounts/{bank}/reconciliation/lines/{line.Id}/settle-cards", new { receivables = refs.Where(r => r.id == parcels[0].Id), lineVersion = line.Version });
        above.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o depósito passa do previsto de um título só");
        var settled = await owner.PostAsJsonAsync($"/api/v1/finance/bank-accounts/{bank}/reconciliation/lines/{line.Id}/settle-cards", new { receivables = refs, lineVersion = line.Version });
        settled.StatusCode.Should().Be(HttpStatusCode.OK, await settled.Content.ReadAsStringAsync());
        var result = await JsonAsync(settled);
        (result.GetProperty("expected").GetDecimal(), result.GetProperty("difference").GetDecimal()).Should().Be((expected, -1m));
        var after = await WithDbAsync(db => db.Receivables.AsNoTracking().Where(r => r.Id == visa.Id || r.Id == parcels[0].Id).ToListAsync());
        after.Count(r => r.Status == ReceivableStatus.Paid).Should().Be(1);
        after.Sum(r => r.Amount - r.PaidAmount).Should().Be(1m, "a diferença fica em aberto no último título");

        // Recebimento conciliado trava a reabertura (o Pos pergunta pelo contrato).
        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<ICashSessionFinance>().HasReconciledReceiptAsync(session)).Should().BeTrue();
            (await scope.ServiceProvider.GetRequiredService<ICashSessionFinance>().HasReconciledReceiptAsync(Guid.NewGuid())).Should().BeFalse();
        }
    }
}
