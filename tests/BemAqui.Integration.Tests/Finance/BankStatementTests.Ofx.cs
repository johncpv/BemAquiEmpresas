using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.Finance.Application.Statements;
using BemAqui.Finance.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using static BemAqui.Integration.Tests.Finance.BankIntegrationFixture;
using static BemAqui.Integration.Tests.Finance.OfxSamples;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// BNK-03a, OFX e consulta: válido com antivírus, repetido pelo hash e da conta errada (CA 4, 5), bloqueado em conta Inter e a
/// contingência (CA 6), aviso ao Proprietário (CA 7), máscara por permissão e desempenho (CA 8), imutabilidade, ignorar e desfazer
/// (CA 9), três camadas e IDOR (CA 10) e auditoria (CA 12).
/// </summary>
public partial class BankStatementTests
{
    private const string AccountDigits = "1234567-8";

    private static MultipartFormDataContent File(byte[] content, string name = "extrato.ofx") =>
        new() { { new ByteArrayContent(content), "file", name } };

    private static Tx[] Sample(string prefix) =>
    [
        new(prefix + "-1", Today.AddDays(-3), -12.90m, "TARIFA PACOTE SERVICOS", "DEBIT"),
        new(prefix + "-2", Today.AddDays(-2), 1500m, "PIX RECEBIDO", "CREDIT", "JOAO DA SILVA"),
        new(prefix + "-3", Today.AddDays(-1), -300.55m, "PAGTO BOLETO FORNECEDOR", "DEBIT"),
    ];

    /// <summary>Conta do Itaú (341) sem API, cadastrada com agência 0001 e conta 1234567-8.</summary>
    private async Task<(HttpClient Owner, Guid OwnerId, Guid Account)> OfxAccountAsync()
    {
        var (ownerId, owner, _) = await fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: true, recentTwoFactor: false);
        return (owner, ownerId, await BankAccountAsync(owner, "341"));
    }

    [Fact]
    public async Task OFX_valido_passa_no_antivirus_vira_linhas_anexo_e_aviso_ao_Proprietario()
    {
        var (owner, ownerId, account) = await OfxAccountAsync();
        var content = Sgml("0341", AccountDigits, Sample("ok"), ledger: 2000m);

        // Antivírus antes da leitura: infectado ou fora do ar recusa e nada é gravado.
        fixture.Scanner.Verdict = ScanVerdict.Infected;
        try
        {
            var infected = await owner.PostAsync(Lines(account) + "/import-ofx", File(content));
            infected.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await CodeAsync(infected)).Should().Be("attachment.infected");
            fixture.Scanner.Verdict = ScanVerdict.Unavailable;
            var down = await owner.PostAsync(Lines(account) + "/import-ofx", File(content));
            down.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeAsync(down)).Should().Be("attachment.scan_unavailable");
        }
        finally
        {
            fixture.Scanner.Verdict = ScanVerdict.Clean;
        }

        (await LinesAsync(fixture.Tenancy.TenantA, account)).Should().BeEmpty();

        // Extensão e magic bytes do dono: .ofx com conteúdo de PDF e .txt com OFX recusados.
        (await owner.PostAsync(Lines(account) + "/import-ofx", File("%PDF-1.7 x"u8.ToArray()))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.PostAsync(Lines(account) + "/import-ofx", File(content, "extrato.txt"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var imported = await OkAsync(await owner.PostAsync(Lines(account) + "/import-ofx", File(content)));
        imported.GetProperty("newCount").GetInt32().Should().Be(3);
        imported.GetProperty("duplicateCount").GetInt32().Should().Be(0);
        imported.GetProperty("bankVerified").GetBoolean().Should().BeTrue();
        var batchId = imported.GetProperty("batchId").GetGuid();

        var lines = await LinesAsync(fixture.Tenancy.TenantA, account);
        lines.Select(l => l.ExternalId).Should().Equal("ok-1", "ok-2", "ok-3");
        lines.Should().OnlyContain(l => l.Source == StatementSource.Ofx && l.BatchId == batchId && l.Status == StatementLineStatus.Pending);
        lines[1].CounterpartyName.Should().Be("JOAO DA SILVA");
        var batch = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.BankStatementBatches.AsNoTracking().SingleAsync(b => b.Id == batchId));
        batch.FileSha256.Should().HaveLength(64);
        batch.AttachmentId.Should().NotBeNull();
        var attachment = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.Attachments.AsNoTracking().SingleAsync(a => a.Id == batch.AttachmentId));
        attachment.OwnerType.Should().Be(BankStatementImportService.AttachmentOwner);
        attachment.ContentType.Should().Be("application/x-ofx");
        attachment.ScanStatus.Should().Be(ScanStatus.Clean);
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.BankStatementBalances.AsNoTracking().SingleAsync(b => b.BankAccountId == account))).Balance.Should().Be(2000m);

        // A rota genérica de anexos não aceita OFX no lote (o arquivo entra só pela importação).
        (await owner.PostAsync($"/api/v1/attachments/{BankStatementImportService.AttachmentOwner}/{batchId}", File(content))).IsSuccessStatusCode.Should().BeFalse();

        // CA 7: aviso ao Proprietário com o período e o link do lote.
        var notice = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.Notifications.AsNoTracking().Where(n => n.UserId == ownerId && n.Type == BankStatementImportService.ImportNotification).ToListAsync());
        notice.Should().ContainSingle(n => n.Link == BankStatementImportService.BatchLink(account, batchId)
            && n.Title.Contains("importou o extrato da conta", StringComparison.Ordinal) && n.Title.Contains("(3 linhas, período", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OFX_repetido_pelo_hash_sobreposto_FITID_repetido_sem_FITID_e_conta_errada()
    {
        var (owner, _, account) = await OfxAccountAsync();
        var content = Sgml("341", AccountDigits, Sample("rep"));
        await OkAsync(await owner.PostAsync(Lines(account) + "/import-ofx", File(content)));

        // CA 5: o mesmo arquivo é recusado inteiro, com quando e quem.
        var same = await owner.PostAsync(Lines(account) + "/import-ofx", File(content));
        same.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(same);
        problem.GetProperty("code").GetString().Should().Be("ofx_already_imported");
        problem.GetProperty("detail").GetString().Should().MatchRegex("^Este arquivo já foi importado em \\d{2}/\\d{2} por .+\\.$");

        // Sobreposto: as do mesmo FITID são ignoradas, só a nova entra.
        var overlap = await OkAsync(await owner.PostAsync(Lines(account) + "/import-ofx", File(Sgml("341", AccountDigits, [.. Sample("rep").Skip(1), new("rep-4", Today, 9m, "PIX RECEBIDO")]))));
        overlap.GetProperty("newCount").GetInt32().Should().Be(1);
        overlap.GetProperty("duplicateCount").GetInt32().Should().Be(2);

        // FITID repetido dentro do arquivo recusa o arquivo.
        var repeated = await owner.PostAsync(Lines(account) + "/import-ofx", File(Sgml("341", AccountDigits, [new("X", Today, 1m, "A"), new("X", Today, 2m, "B")])));
        repeated.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(repeated)).Should().Be("ofx_duplicate_fitid");

        // Sem FITID: entra com o hash e o aviso; o mesmo conteúdo noutro arquivo (hash diferente) não duplica.
        var noFit = await OkAsync(await owner.PostAsync(Lines(account) + "/import-ofx", File(Sgml("341", AccountDigits, [new(null, Today, -7m, "TARIFA AVULSA")]))));
        noFit.GetProperty("newCount").GetInt32().Should().Be(1);
        noFit.GetProperty("notes").EnumerateArray().Select(n => n.GetString()).Should().Contain(n => n!.Contains("sem FITID", StringComparison.Ordinal));
        var noFitAgain = await OkAsync(await owner.PostAsync(Lines(account) + "/import-ofx", File(Sgml("341", AccountDigits, [new(null, Today, -7m, "TARIFA AVULSA")], ledger: 1m))));
        noFitAgain.GetProperty("duplicateCount").GetInt32().Should().Be(1);

        // Conta errada (CA 4): 422 com o final da conta do arquivo; DTD recusada.
        var wrong = await owner.PostAsync(Lines(account) + "/import-ofx", File(Sgml("341", "99887766-5", Sample("w"))));
        wrong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(wrong)).GetProperty("detail").GetString().Should().Be("Este extrato é da conta final 7665, não da conta escolhida.");
        var dtd = await owner.PostAsync(Lines(account) + "/import-ofx", File(Xml("341", AccountDigits, Sample("d"), "<!DOCTYPE OFX [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>\n")));
        (await CodeAsync(dtd)).Should().Be("ofx_dtd");
        (await LinesAsync(fixture.Tenancy.TenantA, account)).Should().HaveCount(5);
    }

    [Fact]
    public async Task OFX_bloqueado_em_conta_Inter_contingencia_com_approve_e_linhas_nao_verificadas()
    {
        var (owner, account, bank) = await InterAccountAsync();
        bank.Statement.Enqueue(new FakeInterTransaction("api-1", Today.AddDays(-1), 100m));
        await OkAsync(await owner.PostAsync(Lines(account) + "/fetch", null));
        var content = Sgml("077", AccountDigits, [new("api-1", Today.AddDays(-1), 100m, "PIX RECEBIDO"), new("ofx-2", Today, -5m, "TARIFA")]);

        var blocked = await owner.PostAsync(Lines(account) + "/import-ofx", File(content));
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(blocked);
        problem.GetProperty("code").GetString().Should().Be(BankStatementImportService.OfxBlockedCode);
        problem.GetProperty("detail").GetString().Should().Be(BankStatementImportService.OfxBlockedMessage);

        // Liberar exige finance.approve (CA 6), prazo até 30 dias e motivo.
        var (_, editor, _) = await fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: false, recentTwoFactor: false, "finance.view", "finance.edit");
        (await editor.PostAsJsonAsync(Lines(account) + "/ofx-contingency", new { reason = "Inter fora do ar", validUntil = Today.AddDays(5) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync(Lines(account) + "/ofx-contingency", new { reason = "Inter fora do ar", validUntil = Today.AddDays(31) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var granted = await OkAsync(await owner.PostAsJsonAsync(Lines(account) + "/ofx-contingency", new { reason = "Inter fora do ar", validUntil = Today.AddDays(5) }));
        granted.GetProperty("validUntil").GetString().Should().Be(Today.AddDays(5).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var imported = await OkAsync(await editor.PostAsync(Lines(account) + "/import-ofx", File(content)));
        imported.GetProperty("bankVerified").GetBoolean().Should().BeFalse();
        imported.GetProperty("newCount").GetInt32().Should().Be(1, "a linha que coincide com a da API pelo id do banco é ignorada");
        imported.GetProperty("duplicateCount").GetInt32().Should().Be(1);
        var ofx = (await LinesAsync(fixture.Tenancy.TenantA, account)).Single(l => l.Source == StatementSource.Ofx);
        ofx.BankVerified.Should().BeFalse();

        var view = await OkAsync(await owner.GetAsync(Lines(account)));
        view.GetProperty("contingency").GetProperty("reason").GetString().Should().Be("Inter fora do ar");
        view.GetProperty("ofxAllowed").GetBoolean().Should().BeTrue();

        // Auditoria da liberação e aviso ao Proprietário.
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.AuditEntries.AsNoTracking().CountAsync(a => a.Entity == nameof(BankOfxContingency)))).Should().BeGreaterThan(0);
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.Notifications.AsNoTracking().AnyAsync(n => n.Type == BankStatementImportService.ContingencyNotification && n.Title.Contains("liberou o OFX")))).Should().BeTrue();
    }

    [Fact]
    public async Task Contraparte_mascarada_so_com_view_IDOR_e_tres_camadas()
    {
        var (owner, account, bank) = await InterAccountAsync();
        bank.Statement.Enqueue(new FakeInterTransaction("m-1", Today, 50m, Name: "Maria da Silva", Document: "52998224725"));
        await OkAsync(await owner.PostAsync(Lines(account) + "/fetch", null));
        var (_, viewer, _) = await fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: false, recentTwoFactor: false, "finance.view");
        var (_, nobody, _) = await fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: false, recentTwoFactor: false);

        var masked = (await OkAsync(await viewer.GetAsync(Lines(account)))).GetProperty("lines")[0];
        masked.GetProperty("counterpartyName").GetString().Should().Be("M***");
        masked.GetProperty("counterpartyDocument").GetString().Should().Be("529.***.***-25");
        masked.GetProperty("counterpartyMasked").GetBoolean().Should().BeTrue();
        var full = (await OkAsync(await owner.GetAsync(Lines(account)))).GetProperty("lines")[0];
        full.GetProperty("counterpartyName").GetString().Should().Be("Maria da Silva");
        full.GetProperty("counterpartyDocument").GetString().Should().Be("529.982.247-25");

        // Camada 3: importar, buscar, ignorar e desfazer sem finance.edit.
        var lineId = full.GetProperty("id").GetGuid();
        var version = full.GetProperty("version").GetUInt32();
        (await viewer.PostAsync(Lines(account) + "/fetch", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsync(Lines(account) + "/import-ofx", File(Sgml("077", AccountDigits, Sample("v"))))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"{Lines(account)}/{lineId}/ignore", new { reason = "x", version })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"{Lines(account)}/batches/{Guid.NewGuid()}/undo", new { reason = "x", version = 0 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Camada 2: sem grupo com o módulo.
        var none = await nobody.GetAsync(Lines(account));
        none.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(none)).Should().Be("forbidden");

        // IDOR: a linha pela conta errada é 404.
        var other = await BankAccountAsync(owner, "341");
        (await owner.PostAsJsonAsync($"{Lines(other)}/{lineId}/ignore", new { reason = "x", version })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync(Lines(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Camada 1: módulo não liberado.
        await fixture.SetModuleAsync(fixture.Tenancy.TenantA, "finance", released: false);
        try
        {
            var locked = await owner.GetAsync(Lines(account));
            locked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CodeAsync(locked)).Should().Be("module_not_released");
        }
        finally
        {
            await fixture.SetModuleAsync(fixture.Tenancy.TenantA, "finance", released: true);
        }
    }

    [Fact]
    public async Task Linha_imutavel_ignorar_credito_com_edit_debito_com_approve_e_desfazer_com_motivo()
    {
        var (owner, _, account) = await OfxAccountAsync();
        var (_, editor, _) = await fixture.ActorAsync(fixture.Tenancy.TenantA, HostA, owner: false, recentTwoFactor: false, "finance.view", "finance.edit");
        var content = Sgml("341", AccountDigits, Sample("imm"));
        var batchId = (await OkAsync(await editor.PostAsync(Lines(account) + "/import-ofx", File(content)))).GetProperty("batchId").GetGuid();
        var lines = (await OkAsync(await editor.GetAsync(Lines(account) + $"?from={Today.AddDays(-10):yyyy-MM-dd}&to={Today:yyyy-MM-dd}"))).GetProperty("lines").EnumerateArray().ToList();
        var debit = lines.First(l => l.GetProperty("amount").GetDecimal() < 0);
        var credit = lines.First(l => l.GetProperty("amount").GetDecimal() > 0);

        // Crédito com finance.edit; débito só com finance.approve (§4.3).
        var ignored = await OkAsync(await editor.PostAsJsonAsync($"{Lines(account)}/{credit.GetProperty("id").GetGuid()}/ignore", new { reason = "Lançamento duplicado do banco", version = credit.GetProperty("version").GetUInt32() }));
        ignored.GetProperty("status").GetString().Should().Be("Ignored");
        var refused = await editor.PostAsJsonAsync($"{Lines(account)}/{debit.GetProperty("id").GetGuid()}/ignore", new { reason = "x", version = debit.GetProperty("version").GetUInt32() });
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(refused)).GetProperty("detail").GetString().Should().Be(BankStatementImportService.DebitNeedsApprove);
        (await owner.PostAsJsonAsync($"{Lines(account)}/{debit.GetProperty("id").GetGuid()}/ignore", new { reason = "", version = debit.GetProperty("version").GetUInt32() })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await OkAsync(await owner.PostAsJsonAsync($"{Lines(account)}/{debit.GetProperty("id").GetGuid()}/ignore", new { reason = "Tarifa estornada", version = debit.GetProperty("version").GetUInt32() }));

        // Filtro por situação.
        var onlyIgnored = await OkAsync(await editor.GetAsync(Lines(account) + $"?from={Today.AddDays(-10):yyyy-MM-dd}&to={Today:yyyy-MM-dd}&status=Ignored"));
        onlyIgnored.GetProperty("total").GetInt32().Should().Be(2);

        // Desfazer com motivo: as linhas saem e o mesmo arquivo pode entrar de novo.
        var batch = (await OkAsync(await editor.GetAsync(Lines(account) + "/batches"))).EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == batchId);
        (await editor.PostAsJsonAsync($"{Lines(account)}/batches/{batchId}/undo", new { reason = " ", version = batch.GetProperty("version").GetUInt32() })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var undone = await OkAsync(await editor.PostAsJsonAsync($"{Lines(account)}/batches/{batchId}/undo", new { reason = "Arquivo de outro periodo", version = batch.GetProperty("version").GetUInt32() }));
        undone.GetProperty("undoReason").GetString().Should().Be("Arquivo de outro periodo");
        (await LinesAsync(fixture.Tenancy.TenantA, account)).Should().BeEmpty();
        (await OkAsync(await editor.PostAsync(Lines(account) + "/import-ofx", File(content)))).GetProperty("newCount").GetInt32().Should().Be(3);

        // Lote com linha conciliada não desfaz (a BNK-03b concilia; aqui direto no banco).
        var second = (await OkAsync(await editor.GetAsync(Lines(account) + "/batches"))).EnumerateArray().First(b => b.GetProperty("undoneAt").ValueKind == JsonValueKind.Null);
        await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.BankStatementLines.Where(l => l.BatchId == second.GetProperty("id").GetGuid()).Take(1)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, StatementLineStatus.Reconciled)));
        var blocked = await editor.PostAsJsonAsync($"{Lines(account)}/batches/{second.GetProperty("id").GetGuid()}/undo", new { reason = "x", version = second.GetProperty("version").GetUInt32() });
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(blocked)).Should().Be("bank_statement_batch_reconciled");

        // CA 12: lote e ignorar na auditoria; a criação das linhas não (fica no lote).
        var audit = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(BankStatementBatch) || a.Entity == nameof(BankStatementLine)).ToListAsync());
        audit.Should().Contain(a => a.Entity == nameof(BankStatementBatch) && a.EntityId == batchId.ToString() && a.Action == "Updated" && a.Changes.Contains("Arquivo de outro periodo"));
        audit.Should().Contain(a => a.Entity == nameof(BankStatementLine) && a.Changes.Contains("Tarifa estornada"));
        audit.Where(a => a.Entity == nameof(BankStatementLine)).Should().OnlyContain(a => a.Action != "Created");
    }

    [Fact]
    public async Task Cinco_mil_linhas_respondem_em_menos_de_1_segundo()
    {
        var (owner, _, account) = await OfxAccountAsync();
        await fixture.QueryAsync(fixture.Tenancy.TenantA, async db =>
        {
            var batch = new BankStatementBatch { BankAccountId = account, Source = StatementSource.Ofx, Trigger = StatementTrigger.Upload, ImportedAt = DateTimeOffset.UtcNow };
            db.BankStatementBatches.Add(batch);
            db.BankStatementLines.AddRange(Enumerable.Range(0, 5000).Select(i => new BankStatementLine
            {
                BankAccountId = account, BatchId = batch.Id, Date = Today.AddDays(-(i % 28)), Amount = i % 2 == 0 ? 10m : -5m, Description = "Linha " + i, Kind = StatementLineKind.Other,
                ExternalId = "load-" + i, Source = StatementSource.Ofx, Sequence = i, ImportedAt = DateTimeOffset.UtcNow, CounterpartyName = "Cliente " + i,
            }));
            return await db.SaveChangesAsync();
        });

        var url = Lines(account) + $"?from={Today.AddDays(-30):yyyy-MM-dd}&to={Today:yyyy-MM-dd}&pageSize=500";
        await owner.GetAsync(url);
        var watch = Stopwatch.StartNew();
        var view = await OkAsync(await owner.GetAsync(url));
        watch.Stop();
        view.GetProperty("total").GetInt32().Should().Be(5000);
        view.GetProperty("lines").GetArrayLength().Should().Be(500);
        watch.ElapsedMilliseconds.Should().BeLessThan(1000);
    }
}
