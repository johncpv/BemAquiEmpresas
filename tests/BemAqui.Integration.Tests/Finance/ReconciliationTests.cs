using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BemAqui.Integration.Tests.Finance.BankIntegrationFixture;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// BNK-03b, integração (Testcontainers): sugestão e conciliação 1×N com a soma errada e o lote de confiança alta (CA 1 e 4), segregação de
/// quem importou o OFX (CA 5), boleto aguardando conferência (CA 9), seções (CA 12), IDOR (CA 13), concorrência (CA 14) e histórico (CA 15).
/// O lançamento sem OP, a baixa, o desfazer e o fechamento ficam no <c>ReconciliationTests.Lines</c>.
/// </summary>
public partial class ReconciliationTests(BankIntegrationFixture fixture) : IClassFixture<BankIntegrationFixture>
{
    private static readonly DateOnly Today = BusinessTime.Today(TimeProvider.System);

    private sealed record L(decimal Amount, int Day, string Description = "PIX RECEBIDO", StatementLineKind Kind = StatementLineKind.Pix, string? Document = null);

    private static string Account(Guid id) => $"/api/v1/finance/bank-accounts/{id}";

    private static string? Plain(string? text) => text?.Replace(" ", " ", StringComparison.Ordinal).Replace(" ", " ", StringComparison.Ordinal);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string Range => $"?from={Today.AddDays(-20):yyyy-MM-dd}&to={Today:yyyy-MM-dd}";

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private Task<(Guid Id, HttpClient Client, string Stamp)> UserAsync(bool twoFactor = false, params string[] grants) =>
        fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: false, recentTwoFactor: twoFactor, grants);

    /// <summary>Proprietário (com a prova do 2FA) e uma conta nova do Itaú, sem API.</summary>
    private async Task<(Guid OwnerId, HttpClient Owner, Guid Account)> AccountAsync()
    {
        var (ownerId, owner, _) = await fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: true, recentTwoFactor: true);
        return (ownerId, owner, await BankAccountAsync(owner, "341"));
    }

    private async Task<T> DbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Um lote do extrato com as linhas (origem, quem importou e se o banco verificou).</summary>
    private Task<List<Guid>> LinesAsync(Guid account, StatementSource source, Guid? importedBy, params L[] lines) => LinesAsync(account, source, importedBy, true, lines);

    private Task<List<Guid>> LinesAsync(Guid account, StatementSource source, Guid? importedBy, bool verified, params L[] lines) => DbAsync(async db =>
    {
        var batch = new BankStatementBatch
        {
            BankAccountId = account, Source = source, Trigger = source == StatementSource.Ofx ? StatementTrigger.Upload : StatementTrigger.Manual, BankVerified = verified,
            NewCount = lines.Length, ImportedAt = DateTimeOffset.UtcNow, ImportedBy = importedBy,
        };
        db.BankStatementBatches.Add(batch);
        var rows = lines.Select((l, i) => new BankStatementLine
        {
            BankAccountId = account, BatchId = batch.Id, Date = Today.AddDays(l.Day), Amount = l.Amount, Description = l.Description, Kind = l.Kind, CounterpartyDocument = l.Document,
            ExternalId = "x-" + Guid.NewGuid().ToString("N"), Source = source, BankVerified = verified, Sequence = i, ImportedAt = DateTimeOffset.UtcNow,
        }).ToList();
        db.BankStatementLines.AddRange(rows);
        await db.SaveChangesAsync();
        return rows.Select(r => r.Id).ToList();
    });

    /// <summary>Transação do sistema já registrada (recebimento ou pagamento), sem passar pelos casos de uso.</summary>
    private Task<Guid> TxAsync(Guid account, decimal amount, int day, string description = "Movimento do sistema", bool pendingReview = false) => DbAsync(async db =>
    {
        var tx = new FinancialTransaction
        {
            BankAccountId = account, Date = Today.AddDays(day), Amount = amount, Type = amount > 0 ? TransactionType.Receipt : TransactionType.Payment, Description = description,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (pendingReview)
        {
            tx.AwaitingReview();
        }

        db.FinancialTransactions.Add(tx);
        await db.SaveChangesAsync();
        return tx.Id;
    });

    private Task<int> BalanceAsync(Guid account, int day, decimal balance) => DbAsync(async db =>
    {
        db.BankStatementBalances.Add(new BankStatementBalance { BankAccountId = account, Date = Today.AddDays(day), Balance = balance, Source = StatementSource.Api, RecordedAt = DateTimeOffset.UtcNow });
        return await db.SaveChangesAsync();
    });

    private static async Task<JsonElement> ViewAsync(HttpClient client, Guid account) => await OkAsync(await client.GetAsync(Account(account) + "/reconciliation" + Range));

    private static IEnumerable<JsonElement> AllLines(JsonElement view) =>
        view.GetProperty("pairs").EnumerateArray().Select(p => p.GetProperty("line")).Concat(view.GetProperty("bankOnly").EnumerateArray().Select(u => u.GetProperty("line")));

    private static IEnumerable<JsonElement> AllTransactions(JsonElement view) =>
        view.GetProperty("pairs").EnumerateArray().Select(p => p.GetProperty("transaction")).Concat(view.GetProperty("systemOnly").EnumerateArray().Select(u => u.GetProperty("transaction")));

    private static object Ref(IEnumerable<JsonElement> items, Guid id)
    {
        var item = items.Single(i => i.GetProperty("id").GetGuid() == id);
        return new { id, version = item.GetProperty("version").GetUInt32() };
    }

    private static Task<HttpResponseMessage> ReconcileAsync(HttpClient client, Guid account, JsonElement view, Guid[] lines, Guid[] transactions, bool accepted = false) =>
        client.PostAsJsonAsync(Account(account) + "/reconciliations", new
        {
            lines = lines.Select(l => Ref(AllLines(view), l)).ToArray(), transactions = transactions.Select(t => Ref(AllTransactions(view), t)).ToArray(), accepted,
        });

    private static JsonElement PairOf(JsonElement view, Guid line) => view.GetProperty("pairs").EnumerateArray().Single(p => p.GetProperty("line").GetProperty("id").GetGuid() == line);

    [Fact]
    public async Task Sugere_os_pares_concilia_1xN_recusa_soma_errada_e_aceita_o_lote_de_confianca_alta()
    {
        var (_, owner, account) = await AccountAsync();
        var (_, editor, _) = await UserAsync(false, "finance.view", "finance.edit");
        var lines = await LinesAsync(account, StatementSource.Api, null, new L(1000m, -5), new L(-45.90m, -4, "TARIFA", StatementLineKind.Fee), new L(-300m, -3, "PAGTO FORNECEDOR", StatementLineKind.Boleto));
        var receipt = await TxAsync(account, 1000m, -5, "Recebimento CR-1");
        var fee = await TxAsync(account, -45.90m, -4, "Tarifa");
        var part1 = await TxAsync(account, -100m, -3, "Pagamento OP parte 1");
        var part2 = await TxAsync(account, -200m, -3, "Pagamento OP parte 2");
        var other = await TxAsync(account, -250m, -3, "Outro pagamento");

        // CA 1: uma linha por par com motivo e confiança; a linha de R$ 300 não tem par (nenhuma transação de R$ 300).
        var view = await ViewAsync(editor, account);
        PairOf(view, lines[0]).GetProperty("transaction").GetProperty("id").GetGuid().Should().Be(receipt);
        PairOf(view, lines[0]).GetProperty("confidence").GetString().Should().Be("High");
        PairOf(view, lines[0]).GetProperty("reason").GetString().Should().Be("Mesmo valor e mesma data");
        PairOf(view, lines[1]).GetProperty("transaction").GetProperty("id").GetGuid().Should().Be(fee);
        view.GetProperty("bankOnly").EnumerateArray().Select(u => u.GetProperty("line").GetProperty("id").GetGuid()).Should().Equal(lines[2]);
        view.GetProperty("systemOnly").EnumerateArray().Select(u => u.GetProperty("transaction").GetProperty("id").GetGuid()).Should().BeEquivalentTo([part1, part2, other]);
        view.GetProperty("totals").GetProperty("pending").GetInt32().Should().Be(3);

        // CA 4: soma diferente recusada com a mensagem; a certa concilia 1×N.
        var wrong = await ProblemAsync(await ReconcileAsync(editor, account, view, [lines[2]], [part1, other]), HttpStatusCode.UnprocessableEntity);
        wrong.GetProperty("code").GetString().Should().Be("reconciliation_sum_mismatch");
        Plain(wrong.GetProperty("detail").GetString()).Should().Be("A soma das transações (-R$ 350,00) não bate com a linha (-R$ 300,00).");
        var manual = await OkAsync(await ReconcileAsync(editor, account, view, [lines[2]], [part1, part2]));
        manual.GetProperty("kind").GetString().Should().Be("Manual");
        manual.GetProperty("transactions").GetInt32().Should().Be(2);

        // "Aceitar todas as de confiança alta": os dois pares, cada um gravado como sugestão aceita.
        view = await ViewAsync(editor, account);
        var pairs = view.GetProperty("pairs").EnumerateArray().Where(p => p.GetProperty("confidence").GetString() == "High").Select(p => new
        {
            lineId = p.GetProperty("line").GetProperty("id").GetGuid(), lineVersion = p.GetProperty("line").GetProperty("version").GetUInt32(),
            transactionId = p.GetProperty("transaction").GetProperty("id").GetGuid(), transactionVersion = p.GetProperty("transaction").GetProperty("version").GetUInt32(),
        }).ToArray();
        pairs.Should().HaveCount(2);
        var batch = await OkAsync(await editor.PostAsJsonAsync(Account(account) + "/reconciliations/accept-high", new { from = Today.AddDays(-20), to = Today, pairs }));
        batch.GetProperty("reconciled").GetInt32().Should().Be(2);
        batch.GetProperty("refused").GetArrayLength().Should().Be(0);

        var stored = await DbAsync(async db => new
        {
            Lines = await db.BankStatementLines.AsNoTracking().Where(l => lines.Contains(l.Id)).ToListAsync(),
            Txs = await db.FinancialTransactions.AsNoTracking().Where(t => t.BankAccountId == account).ToListAsync(),
            Recs = await db.BankReconciliations.AsNoTracking().Where(r => r.BankAccountId == account).ToListAsync(),
        });
        stored.Lines.Should().OnlyContain(l => l.Status == StatementLineStatus.Reconciled && l.MatchedTransactionId != null);
        stored.Txs.Where(t => t.Id != other).Should().OnlyContain(t => t.Reconciled && t.StatementLineId != null);
        stored.Txs.Single(t => t.Id == part2).StatementLineId.Should().Be(lines[2]);
        stored.Txs.Single(t => t.Id == other).Reconciled.Should().BeFalse();
        stored.Recs.Where(r => r.Kind == ReconciliationKind.Suggested).Should().HaveCount(2).And.OnlyContain(r => r.Confidence == ReconciliationConfidence.High && r.Reason == "Mesmo valor e mesma data");
        (await ViewAsync(owner, account)).GetProperty("totals").GetProperty("reconciled").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Quem_importou_o_OFX_nao_confirma_e_outro_do_financeiro_confirma_linha_da_API_sem_restricao()
    {
        var (_, owner, account) = await AccountAsync();
        var (importerId, importer, _) = await UserAsync(true, "finance.view", "finance.edit", "finance.pay");
        var ofx = await LinesAsync(account, StatementSource.Ofx, importerId, new L(700m, -2), new L(-9.90m, -2, "TARIFA", StatementLineKind.Fee));
        var api = await LinesAsync(account, StatementSource.Api, importerId, new L(80m, -2));
        var tx = await TxAsync(account, 700m, -2);
        var apiTx = await TxAsync(account, 80m, -2);

        var view = await ViewAsync(importer, account);
        var line = PairOf(view, ofx[0]).GetProperty("line");
        line.GetProperty("blocked").GetBoolean().Should().BeTrue();
        line.GetProperty("blockedReason").GetString().Should().Be("Quem importou o extrato não confirma a conciliação dele. Peça a outra pessoa do Financeiro.");

        // CA 5: conciliar, aceitar a sugestão, lançar sem OP e baixar a partir da linha: 403 segregation_required.
        var refused = await ProblemAsync(await ReconcileAsync(importer, account, view, [ofx[0]], [tx], accepted: true), HttpStatusCode.Forbidden);
        refused.GetProperty("code").GetString().Should().Be("segregation_required");
        var feeVersion = AllLines(view).Single(l => l.GetProperty("id").GetGuid() == ofx[1]).GetProperty("version").GetUInt32();
        (await ProblemAsync(await importer.PostAsJsonAsync(Account(account) + $"/reconciliation/lines/{ofx[1]}/bank-entry", new { kind = "BankFee", reason = "Tarifa", lineVersion = feeVersion }),
            HttpStatusCode.Forbidden)).GetProperty("code").GetString().Should().Be("segregation_required");
        (await ProblemAsync(await importer.PostAsJsonAsync(Account(account) + $"/reconciliation/lines/{ofx[0]}/settle", new
        {
            receivableId = Guid.NewGuid(), principal = 700m, interest = 0m, fine = 0m, discount = 0m, receivableVersion = 0u, lineVersion = line.GetProperty("version").GetUInt32(),
        }), HttpStatusCode.Forbidden)).GetProperty("code").GetString().Should().Be("segregation_required");

        // A linha da API importada pela mesma pessoa ("Buscar agora") não tem a restrição.
        (await OkAsync(await ReconcileAsync(importer, account, view, [api[0]], [apiTx], accepted: true))).GetProperty("kind").GetString().Should().Be("Suggested");

        // Outro usuário (o Proprietário) confirma a do OFX.
        var ownerView = await ViewAsync(owner, account);
        PairOf(ownerView, ofx[0]).GetProperty("line").GetProperty("blocked").GetBoolean().Should().BeFalse();
        (await OkAsync(await ReconcileAsync(owner, account, ownerView, [ofx[0]], [tx], accepted: true))).GetProperty("kind").GetString().Should().Be("Suggested");
    }

    [Fact]
    public async Task Boleto_aguardando_conferencia_e_casado_mas_a_conferencia_continua_pendente()
    {
        var (_, owner, account) = await AccountAsync();
        var lines = await LinesAsync(account, StatementSource.Api, null, new L(1023.33m, -1, "BOLETO RECEBIDO", StatementLineKind.Boleto));
        var tx = await TxAsync(account, 1023.33m, -1, "Recebimento a conferir", pendingReview: true);

        var view = await ViewAsync(owner, account);
        PairOf(view, lines[0]).GetProperty("transaction").GetProperty("pendingReview").GetBoolean().Should().BeTrue();
        await OkAsync(await ReconcileAsync(owner, account, view, [lines[0]], [tx], accepted: true));

        var stored = await DbAsync(db => db.FinancialTransactions.AsNoTracking().SingleAsync(t => t.Id == tx));
        stored.Reconciled.Should().BeTrue();
        stored.PendingReview.Should().BeTrue("a conferência do título continua em \"Pagamentos a conferir\" (BNK-02 CA 9)");
    }

    [Fact]
    public async Task Secoes_sem_par_no_banco_e_no_sistema_nao_no_banco_com_alerta_depois_de_5_dias_e_IDOR()
    {
        var (_, owner, account) = await AccountAsync();
        var otherAccount = await BankAccountAsync(owner, "341");
        var lines = await LinesAsync(account, StatementSource.Api, null, new L(15m, -1, "PIX RECEBIDO DESCONHECIDO"));
        var old = await TxAsync(account, -70m, -8, "Pagamento que não saiu");
        var recent = await TxAsync(account, -20m, -2, "Pagamento de ontem");
        var foreign = await TxAsync(otherAccount, 15m, -1, "De outra conta");

        // CA 12.
        var view = await ViewAsync(owner, account);
        view.GetProperty("bankOnly").EnumerateArray().Single().GetProperty("line").GetProperty("id").GetGuid().Should().Be(lines[0]);
        var systemOnly = view.GetProperty("systemOnly").EnumerateArray().ToDictionary(u => u.GetProperty("transaction").GetProperty("id").GetGuid());
        systemOnly[old].GetProperty("stale").GetBoolean().Should().BeTrue();
        systemOnly[old].GetProperty("daysWithoutBank").GetInt32().Should().Be(8);
        systemOnly[recent].GetProperty("stale").GetBoolean().Should().BeFalse();
        systemOnly.Should().NotContainKey(foreign);

        // CA 13: transação de outra conta, 422; conciliação e linha de outra conta pela rota errada, 404.
        var lineVersion = view.GetProperty("bankOnly")[0].GetProperty("line").GetProperty("version").GetUInt32();
        var idor = await owner.PostAsJsonAsync(Account(account) + "/reconciliations", new { lines = new[] { new { id = lines[0], version = lineVersion } }, transactions = new[] { new { id = foreign, version = 0u } } });
        (await ProblemAsync(idor, HttpStatusCode.UnprocessableEntity)).GetProperty("detail").GetString().Should().Be("Escolha transações desta conta.");
        var wrongRoute = await owner.PostAsJsonAsync(Account(otherAccount) + "/reconciliations", new { lines = new[] { new { id = lines[0], version = lineVersion } }, transactions = new[] { new { id = foreign, version = 0u } } });
        await ProblemAsync(wrongRoute, HttpStatusCode.NotFound);

        var tx = await TxAsync(account, 15m, -1);
        view = await ViewAsync(owner, account);
        var done = await OkAsync(await ReconcileAsync(owner, account, view, [lines[0]], [tx]));
        var rid = done.GetProperty("reconciliationId").GetGuid();
        await ProblemAsync(await owner.PostAsJsonAsync(Account(otherAccount) + $"/reconciliations/{rid}/undo", new { reason = "teste", version = 0u }), HttpStatusCode.NotFound);
        await ProblemAsync(await owner.GetAsync(Account(otherAccount) + $"/reconciliation/lines/{lines[0]}/history"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dois_usuarios_conciliando_a_mesma_linha_ou_a_mesma_transacao_ao_mesmo_tempo_so_um_passa()
    {
        var (_, owner, account) = await AccountAsync();
        var (_, editor, _) = await UserAsync(false, "finance.view", "finance.edit");
        var lines = await LinesAsync(account, StatementSource.Api, null, new L(50m, -1), new L(60m, -1), new L(60m, -1));
        var a = await TxAsync(account, 50m, -1);
        var b = await TxAsync(account, 50m, -1);
        var t = await TxAsync(account, 60m, -1);

        // Mesma linha com transações diferentes.
        var view = await ViewAsync(owner, account);
        var sameLine = await Task.WhenAll(ReconcileAsync(owner, account, view, [lines[0]], [a]), ReconcileAsync(editor, account, view, [lines[0]], [b]));
        sameLine.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        sameLine.Single(r => r.StatusCode != HttpStatusCode.OK).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Mesma transação para duas linhas.
        var sameTx = await Task.WhenAll(ReconcileAsync(owner, account, view, [lines[1]], [t]), ReconcileAsync(editor, account, view, [lines[2]], [t]));
        sameTx.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var loser = sameTx.Single(r => r.StatusCode != HttpStatusCode.OK);
        loser.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await loser.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().BeOneOf("concurrency_conflict", "transaction_reconciled", "bank_statement_line_not_pending");

        var links = await DbAsync(db => db.BankReconciliationLinks.AsNoTracking().Where(k => k.TransactionId == t || k.StatementLineId == lines[0]).ToListAsync());
        links.Should().HaveCount(2, "uma conciliação para a linha 1 e uma para a transação t");
        (await DbAsync(db => db.BankStatementLines.AsNoTracking().CountAsync(l => (l.Id == lines[1] || l.Id == lines[2]) && l.Status == StatementLineStatus.Reconciled))).Should().Be(1);
    }

    [Fact]
    public async Task Historico_da_linha_e_da_transacao_com_quem_a_sugestao_aceita_quem_importou_e_o_motivo()
    {
        var (_, owner, account) = await AccountAsync();
        var (importerId, _, _) = await UserAsync(false, "finance.view", "finance.edit");
        var lines = await LinesAsync(account, StatementSource.Ofx, importerId, new L(310m, -2));
        var tx = await TxAsync(account, 310m, -2);
        var view = await ViewAsync(owner, account);
        var done = await OkAsync(await ReconcileAsync(owner, account, view, [lines[0]], [tx], accepted: true));
        var rid = done.GetProperty("reconciliationId").GetGuid();
        var version = await DbAsync(db => db.BankReconciliations.AsNoTracking().Where(r => r.Id == rid).Select(r => r.Version).SingleAsync());
        await OkAsync(await owner.PostAsJsonAsync(Account(account) + $"/reconciliations/{rid}/undo", new { reason = "Par errado", version }));

        var history = await OkAsync(await owner.GetAsync(Account(account) + $"/reconciliation/lines/{lines[0]}/history"));
        history.GetProperty("importedByName").GetString().Should().NotBeNullOrEmpty();
        history.GetProperty("source").GetString().Should().Be("Ofx");
        var entry = history.GetProperty("entries").EnumerateArray().Single();
        entry.GetProperty("kind").GetString().Should().Be("Suggested");
        entry.GetProperty("reason").GetString().Should().Be("Mesmo valor e mesma data");
        entry.GetProperty("createdByName").GetString().Should().NotBeNullOrEmpty();
        entry.GetProperty("undoReason").GetString().Should().Be("Par errado");
        entry.GetProperty("undoneByName").GetString().Should().NotBeNullOrEmpty();

        var txHistory = await OkAsync(await owner.GetAsync(Account(account) + $"/reconciliation/transactions/{tx}/history"));
        txHistory.GetProperty("entries").GetArrayLength().Should().Be(1);

        // A auditoria guarda a conciliação (camelCase) e a mudança da linha.
        var audit = await DbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.EntityId == rid.ToString()).ToListAsync());
        audit.Should().NotBeEmpty();
    }
}
