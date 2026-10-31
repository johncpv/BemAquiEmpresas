using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Finance.Application;
using BemAqui.Finance.Application.Reconciliation;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// BNK-03b, integração: lançar sem OP a partir da linha com <c>finance.pay</c>, 2FA de novo na saída, aviso e teto do OFX (CA 2 e 6);
/// saída comum nunca vira pagamento (CA 8); baixar título com os limites da FIN-01 (CA 3 e 7); desfazer sem estornar (CA 10); fechar o
/// período e o período fechado, inclusive pela FIN-04, pela FIN-01 e pelo ignorar linha, com um mês simulado fechando com diferença zero (CA 11).
/// </summary>
public partial class ReconciliationTests
{
    private static uint VersionOf(JsonElement view, Guid line) => AllLines(view).Single(l => l.GetProperty("id").GetGuid() == line).GetProperty("version").GetUInt32();

    private static JsonElement Unpaired(JsonElement view, Guid line) => view.GetProperty("bankOnly").EnumerateArray().Single(u => u.GetProperty("line").GetProperty("id").GetGuid() == line);

    private static Task<HttpResponseMessage> EntryFromLineAsync(HttpClient client, Guid account, JsonElement view, Guid line, string kind, string reason = "Tarifa do mês") =>
        client.PostAsJsonAsync(Account(account) + $"/reconciliation/lines/{line}/bank-entry", new { kind, reason, lineVersion = VersionOf(view, line) });

    private Task<int> NotificationsAsync(Guid userId, string type) => DbAsync(db => db.Notifications.AsNoTracking().CountAsync(n => n.UserId == userId && n.Type == type));

    private static async Task<(Guid Id, string Cnpj)> CustomerAsync(HttpClient owner)
    {
        var cnpj = BillingSlipFixture.NewCnpj();
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new
        {
            kind = "PJ", document = cnpj, legalName = "Cliente Conciliação " + Guid.NewGuid().ToString("N")[..6], tradeName = (string?)null, roles = new[] { "Customer" },
            billingAddress = new { kind = "Billing", zipCode = "01310100", street = "Avenida Paulista", number = "1000", complement = (string?)null, district = "Bela Vista", city = "São Paulo", ibgeCode = "3550308", state = "SP", isDefault = true },
            mainContact = (object?)null,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return ((await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), cnpj);
    }

    private async Task<JsonElement> ReceivableAsync(HttpClient client, Guid party, decimal amount)
    {
        var revenue = await DbAsync(db => db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync());
        var body = new { partyId = party, description = "Venda", accountId = revenue, costCenterId = (Guid?)null, projectId = (Guid?)null, amount, issueDate = Today.AddDays(-30), dueDate = Today.AddDays(10) };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/finance/receivables") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var created = await client.SendAsync(request);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
    }

    [Fact]
    public async Task Lancar_sem_OP_pela_regra_com_2FA_na_saida_aviso_ao_Proprietario_e_saida_comum_nunca_vira_pagamento()
    {
        var (ownerId, _, account) = await AccountAsync();
        var (_, editor, _) = await UserAsync(false, "finance.view", "finance.edit");
        var (_, payer, _) = await UserAsync(false, "finance.view", "finance.edit", "finance.pay");
        var (_, payer2fa, _) = await UserAsync(true, "finance.view", "finance.edit", "finance.pay");

        // CA 2: regra criada pela tela (seed vazio) com finance.edit.
        var rule = await OkAsync(await editor.PostAsJsonAsync("/api/v1/finance/bank-statement-rules", new { keyword = "PACOTE", entryKind = "BankFee" }));
        var lines = await LinesAsync(account, StatementSource.Api, null,
            new L(-12.90m, -1, "TAR PACOTE SERVICOS", StatementLineKind.Other), new L(-300m, -1, "PAGTO BOLETO FORNECEDOR", StatementLineKind.Boleto), new L(4.10m, -1, "RENDIMENTO CDB", StatementLineKind.Yield));
        var view = await ViewAsync(payer, account);
        var fee = Unpaired(view, lines[0]);
        fee.GetProperty("action").GetProperty("kind").GetString().Should().Be("BankEntry");
        fee.GetProperty("action").GetProperty("text").GetString().Should().Be("Lançar como Tarifa bancária sem OP");
        fee.GetProperty("action").GetProperty("ruleId").GetGuid().Should().Be(rule.GetProperty("id").GetGuid());
        fee.GetProperty("action").GetProperty("accountId").ValueKind.Should().Be(JsonValueKind.String, "a conta do plano vem preenchida");
        var payment = Unpaired(view, lines[1]);
        payment.GetProperty("action").GetProperty("kind").GetString().Should().Be("PaymentOrder");
        payment.GetProperty("canBankEntry").GetBoolean().Should().BeFalse();
        Unpaired(view, lines[2]).GetProperty("action").GetProperty("entryKind").GetString().Should().Be("Yield");

        // CA 6: saída pede o 2FA de novo.
        (await ProblemAsync(await EntryFromLineAsync(payer, account, view, lines[0], "BankFee"), HttpStatusCode.Forbidden)).GetProperty("code").GetString().Should().Be("two_factor_required");
        (await ProblemAsync(await EntryFromLineAsync(editor, account, view, lines[0], "BankFee"), HttpStatusCode.Forbidden)).GetProperty("code").GetString().Should().Be("forbidden", "lançar sem OP é finance.pay");
        var before = await NotificationsAsync(ownerId, BankMovementService.NotificationType);
        var done = await OkAsync(await EntryFromLineAsync(payer2fa, account, view, lines[0], "BankFee"));
        done.GetProperty("kind").GetString().Should().Be("BankEntry");

        var stored = await DbAsync(async db => new
        {
            Entry = await db.BankEntries.AsNoTracking().SingleAsync(e => e.BankAccountId == account),
            Tx = await db.FinancialTransactions.AsNoTracking().SingleAsync(t => t.BankAccountId == account),
            Line = await db.BankStatementLines.AsNoTracking().SingleAsync(l => l.Id == lines[0]),
        });
        stored.Entry.Should().Match<BankEntry>(e => e.Kind == BankEntryKind.BankFee && e.Amount == -12.90m && e.Date == Today.AddDays(-1) && e.Reason == "Tarifa do mês");
        stored.Tx.Should().Match<FinancialTransaction>(t => t.Type == TransactionType.BankEntry && t.Reconciled && t.StatementLineId == lines[0] && t.BankEntryId == stored.Entry.Id);
        stored.Line.Status.Should().Be(StatementLineStatus.Reconciled);
        (await NotificationsAsync(ownerId, BankMovementService.NotificationType)).Should().Be(before + 1, "todo lançamento sem OP avisa o Proprietário");

        // CA 8: saída que não é tarifa, IOF nem juros nunca vira lançamento (nem "Outra saída"); a linha fica pendente.
        var refused = await ProblemAsync(await EntryFromLineAsync(payer2fa, account, view, lines[1], "BankFee"), HttpStatusCode.UnprocessableEntity);
        refused.GetProperty("code").GetString().Should().Be(ReconciliationService.EntryNotAllowedCode);
        refused.GetProperty("detail").GetString().Should().Be(ReconciliationService.PaymentNeedsOrder);
        (await ProblemAsync(await EntryFromLineAsync(payer2fa, account, view, lines[1], "OtherOut"), HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString()
            .Should().Be(ReconciliationService.EntryNotAllowedCode);
        (await DbAsync(db => db.FinancialTransactions.CountAsync(t => t.BankAccountId == account))).Should().Be(1);
        (await DbAsync(db => db.BankStatementLines.AsNoTracking().SingleAsync(l => l.Id == lines[1]))).Status.Should().Be(StatementLineStatus.Pending);
        // "Vincular a uma OP paga": só pagamentos de OP como candidatos (aqui, nenhum).
        (await OkAsync(await payer.GetAsync(Account(account) + $"/reconciliation/candidates?date={Today.AddDays(-1):yyyy-MM-dd}&outflow=true&paymentsOnly=true"))).GetArrayLength().Should().Be(0);

        // Entrada (rendimento) não pede o 2FA.
        (await OkAsync(await EntryFromLineAsync(payer, account, view, lines[2], "Yield", "Rendimento do mês"))).GetProperty("kind").GetString().Should().Be("BankEntry");
    }

    [Fact]
    public async Task Linha_de_OFX_acima_do_teto_so_com_quem_aprova()
    {
        var (_, owner, account) = await AccountAsync();
        var (importerId, _, _) = await UserAsync(false, "finance.view", "finance.edit");
        var (_, payer2fa, _) = await UserAsync(true, "finance.view", "finance.edit", "finance.pay");
        var lines = await LinesAsync(account, StatementSource.Ofx, importerId, false, new L(-800m, -1, "TARIFA EMISSAO", StatementLineKind.Fee), new L(-30m, -1, "TARIFA TED", StatementLineKind.Fee));
        var view = await ViewAsync(payer2fa, account);
        view.GetProperty("ofxBankEntryCap").GetDecimal().Should().Be(500m);

        (await ProblemAsync(await EntryFromLineAsync(payer2fa, account, view, lines[0], "BankFee"), HttpStatusCode.Forbidden)).GetProperty("code").GetString().Should().Be(ReconciliationService.OfxCapCode);
        await OkAsync(await EntryFromLineAsync(payer2fa, account, view, lines[1], "BankFee"));
        await OkAsync(await EntryFromLineAsync(owner, account, view, lines[0], "BankFee"));
    }

    [Fact]
    public async Task Baixar_o_titulo_a_partir_da_linha_com_os_limites_da_FIN01_e_desfazer_sem_estornar()
    {
        var (_, owner, account) = await AccountAsync();
        var (_, editor, _) = await UserAsync(false, "finance.view", "finance.edit");
        var (party, cnpj) = await CustomerAsync(owner);
        var title = await ReceivableAsync(owner, party, 1500m);
        var discounted = await ReceivableAsync(owner, party, 2000m);
        var lines = await LinesAsync(account, StatementSource.Api, null, new L(1500m, -1, "PIX RECEBIDO", StatementLineKind.Pix, cnpj), new L(1800m, -1, "PIX RECEBIDO", StatementLineKind.Pix, cnpj));

        // CA 3: pagador cliente com título em aberto do mesmo valor.
        var view = await ViewAsync(editor, account);
        var action = Unpaired(view, lines[0]).GetProperty("action");
        action.GetProperty("kind").GetString().Should().Be("Settle");
        action.GetProperty("receivableId").GetGuid().Should().Be(title.GetProperty("id").GetGuid());
        action.GetProperty("text").GetString().Should().StartWith("Baixar o título " + title.GetProperty("number").GetString() + " de Cliente Conciliação");

        object Settle(JsonElement t, Guid line, decimal principal, decimal discount) => new
        {
            receivableId = t.GetProperty("id").GetGuid(), principal, interest = 0m, fine = 0m, discount, receivableVersion = t.GetProperty("version").GetUInt32(), lineVersion = VersionOf(view, line),
        };

        // CA 7: o total precisa ser o valor da linha; desconto acima do limite (10%) sem finance.approve é recusado e nada fica gravado.
        Plain((await ProblemAsync(await editor.PostAsJsonAsync(Account(account) + $"/reconciliation/lines/{lines[0]}/settle", Settle(title, lines[0], 1000m, 0m)), HttpStatusCode.UnprocessableEntity))
            .GetProperty("detail").GetString()).Should().Be("O total da baixa (R$ 1.000,00) precisa ser o valor da linha (R$ 1.500,00).");
        (await ProblemAsync(await editor.PostAsJsonAsync(Account(account) + $"/reconciliation/lines/{lines[1]}/settle", Settle(discounted, lines[1], 2000m, 200m)), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().Should().Be("discount_requires_approval");
        (await DbAsync(db => db.FinancialTransactions.CountAsync(t => t.BankAccountId == account))).Should().Be(0);

        var settled = await OkAsync(await editor.PostAsJsonAsync(Account(account) + $"/reconciliation/lines/{lines[0]}/settle", Settle(title, lines[0], 1500m, 0m)));
        settled.GetProperty("kind").GetString().Should().Be("Settlement");
        var titleId = title.GetProperty("id").GetGuid();
        var stored = await DbAsync(async db => new
        {
            Title = await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == titleId),
            Tx = await db.FinancialTransactions.AsNoTracking().SingleAsync(t => t.BankAccountId == account),
        });
        stored.Title.Status.Should().Be(ReceivableStatus.Paid);
        stored.Tx.Should().Match<FinancialTransaction>(t => t.Amount == 1500m && t.Reconciled && t.StatementLineId == lines[0] && t.ReceivableId == titleId && t.Date == Today.AddDays(-1));

        // CA 10: desfazer pede motivo; volta tudo a pendente sem estornar a baixa.
        var rid = settled.GetProperty("reconciliationId").GetGuid();
        var version = await DbAsync(db => db.BankReconciliations.AsNoTracking().Where(r => r.Id == rid).Select(r => r.Version).SingleAsync());
        await ProblemAsync(await editor.PostAsJsonAsync(Account(account) + $"/reconciliations/{rid}/undo", new { reason = " ", version }), HttpStatusCode.UnprocessableEntity);
        await OkAsync(await editor.PostAsJsonAsync(Account(account) + $"/reconciliations/{rid}/undo", new { reason = "Linha errada", version }));
        var after = await DbAsync(async db => new
        {
            Title = await db.Receivables.AsNoTracking().SingleAsync(r => r.Id == titleId),
            Txs = await db.FinancialTransactions.AsNoTracking().Where(t => t.BankAccountId == account).ToListAsync(),
            Line = await db.BankStatementLines.AsNoTracking().SingleAsync(l => l.Id == lines[0]),
            Rec = await db.BankReconciliations.AsNoTracking().SingleAsync(r => r.Id == rid),
        });
        after.Title.Status.Should().Be(ReceivableStatus.Paid, "desfazer não estorna a baixa (sai só pelo estorno da FIN-01)");
        after.Txs.Should().ContainSingle().Which.Reconciled.Should().BeFalse();
        after.Line.Status.Should().Be(StatementLineStatus.Pending);
        after.Rec.UndoReason.Should().Be("Linha errada");

        // A linha volta a ter par: a própria transação do recebimento.
        PairOf(await ViewAsync(editor, account), lines[0]).GetProperty("transaction").GetProperty("id").GetGuid().Should().Be(after.Txs[0].Id);
    }

    [Fact]
    public async Task Mes_simulado_fecha_com_diferenca_zero_e_o_periodo_fechado_pede_aprovacao_e_motivo_em_todo_lancamento()
    {
        var (ownerId, owner, account) = await AccountAsync();
        var (_, editor, _) = await UserAsync(false, "finance.view", "finance.edit");
        var (_, payer, _) = await UserAsync(false, "finance.view", "finance.edit", "finance.pay");
        var (_, approver, _) = await UserAsync(true, "finance.view", "finance.edit", "finance.pay", "finance.approve");
        var data = new[] { (1000m, -6), (-45.90m, -5), (250m, -4), (-120m, -3) };
        var lines = await LinesAsync(account, StatementSource.Api, null, [.. data.Select(d => new L(d.Item1, d.Item2))]);
        foreach (var (amount, day) in data)
        {
            await TxAsync(account, amount, day);
        }

        await BalanceAsync(account, -3, 1084.10m);
        var until = Today.AddDays(-3);

        // Com linha pendente não fecha.
        (await ProblemAsync(await approver.PostAsJsonAsync(Account(account) + "/reconciliation/close", new { until }), HttpStatusCode.Conflict)).GetProperty("code").GetString()
            .Should().Be(ReconciliationService.NotReadyCode);

        var view = await ViewAsync(editor, account);
        view.GetProperty("reconciledUntil").ValueKind.Should().Be(JsonValueKind.Null);
        var pairs = view.GetProperty("pairs").EnumerateArray().Select(p => new
        {
            lineId = p.GetProperty("line").GetProperty("id").GetGuid(), lineVersion = p.GetProperty("line").GetProperty("version").GetUInt32(),
            transactionId = p.GetProperty("transaction").GetProperty("id").GetGuid(), transactionVersion = p.GetProperty("transaction").GetProperty("version").GetUInt32(),
        }).ToArray();
        (await OkAsync(await editor.PostAsJsonAsync(Account(account) + "/reconciliations/accept-high", new { from = Today.AddDays(-20), to = Today, pairs }))).GetProperty("reconciled").GetInt32().Should().Be(4);

        view = await ViewAsync(editor, account);
        view.GetProperty("reconciledUntil").GetString().Should().Be(Iso(until));
        var preview = await OkAsync(await editor.GetAsync(Account(account) + $"/reconciliation/closing-preview?until={until:yyyy-MM-dd}"));
        preview.GetProperty("allowed").GetBoolean().Should().BeTrue();
        preview.GetProperty("lines").GetInt32().Should().Be(4);
        preview.GetProperty("credits").GetDecimal().Should().Be(1250m);
        preview.GetProperty("debits").GetDecimal().Should().Be(-165.90m);
        preview.GetProperty("difference").GetDecimal().Should().Be(0m);

        // Fechar é finance.approve.
        (await ProblemAsync(await editor.PostAsJsonAsync(Account(account) + "/reconciliation/close", new { until }), HttpStatusCode.Forbidden)).GetProperty("code").GetString().Should().Be("forbidden");
        var closed = await OkAsync(await approver.PostAsJsonAsync(Account(account) + "/reconciliation/close", new { until }));
        closed.GetProperty("until").GetString().Should().Be(Iso(until));
        (await ViewAsync(editor, account)).GetProperty("closedUntil").GetString().Should().Be(Iso(until));

        // Desfazer no período fechado: só com finance.approve e motivo, e o Proprietário é avisado.
        var done = (await ViewAsync(editor, account)).GetProperty("done").EnumerateArray().First();
        done.GetProperty("inClosedPeriod").GetBoolean().Should().BeTrue();
        var undo = new { reason = "Par trocado", version = done.GetProperty("version").GetUInt32() };
        (await ProblemAsync(await editor.PostAsJsonAsync(Account(account) + $"/reconciliations/{done.GetProperty("id").GetGuid()}/undo", undo), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().Should().Be(ReconciliationPeriodGuard.ClosedCode);
        var notices = await NotificationsAsync(ownerId, ReconciliationPeriodGuard.NotificationType);
        await OkAsync(await approver.PostAsJsonAsync(Account(account) + $"/reconciliations/{done.GetProperty("id").GetGuid()}/undo", undo));
        (await NotificationsAsync(ownerId, ReconciliationPeriodGuard.NotificationType)).Should().Be(notices + 1);

        // FIN-04: lançamento com data no período fechado.
        async Task<HttpResponseMessage> EntryAsync(HttpClient client, string? reason)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/finance/bank-entries")
            {
                Content = JsonContent.Create(new { bankAccountId = account, kind = "BankFee", date = Today.AddDays(-4), amount = 5m, accountId = (Guid?)null, costCenterId = (Guid?)null, reason = "Tarifa esquecida", backdateReason = reason }),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            return await client.SendAsync(request);
        }

        (await ProblemAsync(await EntryAsync(payer, "Esquecida"), HttpStatusCode.Forbidden)).GetProperty("code").GetString().Should().Be(ReconciliationPeriodGuard.ClosedCode);
        await ProblemAsync(await EntryAsync(approver, null), HttpStatusCode.UnprocessableEntity);
        (await EntryAsync(approver, "Tarifa esquecida no fechamento")).StatusCode.Should().Be(HttpStatusCode.Created);

        // BNK-03a: ignorar linha com data no período fechado.
        var late = await LinesAsync(account, StatementSource.Api, null, new L(7m, -4, "PIX ATRASADO"));
        var lateVersion = await DbAsync(db => db.BankStatementLines.AsNoTracking().Where(l => l.Id == late[0]).Select(l => l.Version).SingleAsync());
        (await ProblemAsync(await editor.PostAsJsonAsync($"{Account(account)}/statement-lines/{late[0]}/ignore", new { reason = "Duplicada", version = lateVersion }), HttpStatusCode.Forbidden))
            .GetProperty("code").GetString().Should().Be(ReconciliationPeriodGuard.ClosedCode);

        // FIN-01: baixa com data no período fechado.
        var (party, _) = await CustomerAsync(owner);
        var title = await ReceivableAsync(owner, party, 300m);
        var settle = await editor.PostAsJsonAsync($"/api/v1/finance/receivables/{title.GetProperty("id").GetGuid()}/settlements", new
        {
            bankAccountId = account, date = Today.AddDays(-4), principal = 300m, interest = 0m, fine = 0m, discount = 0m, backdateReason = "Recebido no período", version = title.GetProperty("version").GetUInt32(),
        });
        (await ProblemAsync(settle, HttpStatusCode.Forbidden)).GetProperty("code").GetString().Should().Be(ReconciliationPeriodGuard.ClosedCode);

        // Depois da data fechada segue normal.
        var normal = await editor.PostAsJsonAsync($"/api/v1/finance/receivables/{title.GetProperty("id").GetGuid()}/settlements", new
        {
            bankAccountId = account, date = Today, principal = 300m, interest = 0m, fine = 0m, discount = 0m, backdateReason = (string?)null, version = title.GetProperty("version").GetUInt32(),
        });
        normal.StatusCode.Should().Be(HttpStatusCode.OK, await normal.Content.ReadAsStringAsync());
    }
}
