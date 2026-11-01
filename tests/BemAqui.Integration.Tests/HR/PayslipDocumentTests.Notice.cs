using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Email;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.HR;

public partial class PayslipDocumentTests
{
    private const string Canary = "86420.13";
    private const string CanaryBr = "86.420,13";

    /// <summary>
    /// CA 7, 8, 9 e 12: só com a folha fechada; o diálogo conta quem recebe, os sem usuário (com o e-mail sugerido) e os desligados; um
    /// e-mail por funcionário para o usuário ligado, sem anexo nem valor, modelo mínimo; Idempotency-Key; avisar de novo a folha não
    /// duplica; "Avisar de novo" por holerite; situação pelo email_logs; canário fora do log, do email_logs e do outbox.
    /// </summary>
    [Fact]
    public async Task Avisar_funcionarios_so_com_folha_fechada_sem_anexo_nem_valor()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner.Client);
        var ana = await EmployeeAsync(owner.Client, position);
        var bia = await EmployeeAsync(owner.Client, position);
        var caio = await EmployeeAsync(owner.Client, position);
        var run = await RunAsync(owner.Client, "2033-01", [ana.Id, bia.Id, caio.Id]);
        await FillAsync(owner.Client, run, decimal.Parse(Canary, System.Globalization.CultureInfo.InvariantCulture));
        var anaUser = await LinkedAsync(ana.Id);
        await DbAsync(async db =>
        {
            (await db.Employees.SingleAsync(e => e.Id == bia.Id)).Email = "bia.pessoal@exemplo.com";
            var c = await db.Employees.SingleAsync(e => e.Id == caio.Id);
            c.Status = EmployeeStatus.Terminated;
            c.TerminationDate = new DateOnly(2033, 1, 20);
            return await db.SaveChangesAsync();
        });

        (await CodeAsync(await owner.Client.SendAsync(Post($"{Runs}/{run}/notify", new { onlyChanged = false })))).Should().Be(PayslipDocumentRules.NotClosedCode, "só depois de fechar (CA 7)");
        await SetStatusAsync(run, PayrollRunStatus.Closed);

        var notices = await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}/notices/sensitive"));
        notices.GetProperty("pending").GetInt32().Should().Be(1);
        notices.GetProperty("withoutUser").EnumerateArray().Single().GetProperty("suggestedEmail").GetString().Should().Be("bia.pessoal@exemplo.com");
        notices.GetProperty("terminated").EnumerateArray().Single().GetProperty("employeeId").GetGuid().Should().Be(caio.Id);
        StatusOf(notices, bia.Id).Should().Be("NoUser");
        StatusOf(notices, caio.Id).Should().Be("Terminated");

        // Camadas: avisar exige hr.edit (rota) e hr.view_sensitive (serviço); sem a chave de idempotência, recusa.
        var editor = await ActorAsync("hr:view,edit");
        (await editor.Client.SendAsync(Post($"{Runs}/{run}/notify", new { onlyChanged = false }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var sensitiveOnly = await ActorAsync("hr:view,view_sensitive");
        (await sensitiveOnly.Client.SendAsync(Post($"{Runs}/{run}/notify", new { onlyChanged = false }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.Client.PostAsJsonAsync($"{Runs}/{run}/notify", new { onlyChanged = false })).IsSuccessStatusCode.Should().BeFalse("exige Idempotency-Key");

        var key = Guid.NewGuid();
        var sent = await owner.Client.SendAsync(Post($"{Runs}/{run}/notify", new { onlyChanged = false }, key));
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        (await JsonAsync(sent)).GetProperty("message").GetString().Should().Be("1 aviso na fila de envio");
        (await owner.Client.SendAsync(Post($"{Runs}/{run}/notify", new { onlyChanged = false }, key))).StatusCode.Should().Be(HttpStatusCode.OK, "mesma chave devolve o original");
        (await CodeAsync(await owner.Client.SendAsync(Post($"{Runs}/{run}/notify", new { onlyChanged = false })))).Should().Be("payslip_notice_none", "avisar de novo a folha não duplica");

        var slips = (await SlipsAsync(owner.Client, run)).ToDictionary(s => s.GetProperty("employeeId").GetGuid(), s => s.GetProperty("id").GetGuid());
        var logs = await DbAsync(db => db.EmailLogs.AsNoTracking().Where(l => l.Template == PayslipDocumentRules.NoticeTemplate && l.RelatedId == slips[ana.Id]).ToListAsync());
        var log = logs.Should().ContainSingle().Subject;
        log.To.Should().Be(anaUser.Email, "só o e-mail do usuário ligado (CA 8)");
        (log.Cc, log.AttachmentIds.Length).Should().Be((null, 0), "sem cópia nem anexo");
        JsonDocument.Parse(log.Model).RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["employee_name", "month", "tenant_name", "link"]);
        log.Model.Should().Contain("01/2033").And.Contain("/app/meus-holerites").And.NotContain(Canary).And.NotContain(CanaryBr).And.NotContain(ana.Cpf);
        var rendered = new EmailTemplateRenderer().Render(log.Template, JsonSerializer.Deserialize<Dictionary<string, object?>>(log.Model)!.ToDictionary(k => k.Key, k => (object?)k.Value?.ToString()));
        rendered.Subject.Should().StartWith("Seu holerite de 01/2033 está disponível — ");
        rendered.Text.Should().Contain("Entre no sistema e abra Meus holerites").And.NotContain(CanaryBr);

        notices = await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}/notices/sensitive"));
        StatusOf(notices, ana.Id).Should().Be("Queued");
        await DbAsync(async db =>
        {
            var l = await db.EmailLogs.SingleAsync(x => x.Id == log.Id);
            l.Status = EmailStatus.Failed;
            l.Error = "Caixa cheia";
            return await db.SaveChangesAsync();
        });
        notices = await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}/notices/sensitive"));
        StatusOf(notices, ana.Id).Should().Be("Failed");
        notices.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("employeeId").GetGuid() == ana.Id).GetProperty("error").GetString().Should().Be("Caixa cheia");

        // "Avisar de novo" por holerite: novo e-mail; sem usuário e desligado recusam; holerite de outra folha, 404.
        (await owner.Client.SendAsync(Post($"{Runs}/{run}/payslips/{slips[ana.Id]}/notify", null))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DbAsync(db => db.EmailLogs.CountAsync(l => l.Template == PayslipDocumentRules.NoticeTemplate && l.RelatedId == slips[ana.Id]))).Should().Be(2);
        (await CodeAsync(await owner.Client.SendAsync(Post($"{Runs}/{run}/payslips/{slips[bia.Id]}/notify", null)))).Should().Be("payslip_notice_no_user");
        (await CodeAsync(await owner.Client.SendAsync(Post($"{Runs}/{run}/payslips/{slips[caio.Id]}/notify", null)))).Should().Be("payslip_notice_terminated");
        (await owner.Client.SendAsync(Post($"{Runs}/{Guid.CreateVersion7()}/payslips/{slips[ana.Id]}/notify", null))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Canário (CA 12): o líquido não aparece no log, no outbox nem na auditoria do aviso.
        await owner.Client.GetAsync($"{Runs}/{run}/payslips/{slips[ana.Id]}/pdf");
        var outbox = string.Join("\n", await DbAsync(db => db.OutboxMessages.AsNoTracking().Select(m => m.Payload).ToListAsync()));
        outbox.Should().NotContain(Canary).And.NotContain(CanaryBr);
        var audit = string.Join("\n", await DbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.EntityId == slips[ana.Id].ToString()).Select(a => a.Changes).ToListAsync()));
        audit.Should().Contain("notifiedAt", "o aviso fica na auditoria do holerite").And.NotContain(Canary).And.NotContain("86420,13").And.NotContain(anaUser.Email);
        LogText().Should().NotContain(Canary).And.NotContain(CanaryBr).And.NotContain("86420,13");
    }

    private static string StatusOf(JsonElement notices, Guid employeeId) =>
        notices.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("employeeId").GetGuid() == employeeId).GetProperty("status").GetString()!;

    /// <summary>
    /// CA 10: folha fechada e avisada, reaberta e fechada de novo com mudança no holerite: o PDF guardado deixa de valer e é refeito, o
    /// holerite ganha "Alterado depois do aviso" e o fechamento conta os alterados; "Avisar os alterados" só avisa esses.
    /// </summary>
    [Fact]
    public async Task Fechada_de_novo_com_mudanca_marca_alterado_depois_do_aviso_e_refaz_o_PDF()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var position = await PositionAsync(owner.Client);
        var ana = await EmployeeAsync(owner.Client, position);
        var bia = await EmployeeAsync(owner.Client, position);
        foreach (var p in new[] { ana, bia })
        {
            await SetAccountAsync(p.Id);
            await LinkedAsync(p.Id);
        }

        var run = await RunAsync(owner.Client, "2034-01", [ana.Id, bia.Id]);
        await FillAsync(owner.Client, run, 1_000m);
        (await CloseAsync(owner, run)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.Client.SendAsync(Post($"{Runs}/{run}/notify", new { onlyChanged = false }))).StatusCode.Should().Be(HttpStatusCode.OK);
        var slips = (await SlipsAsync(owner.Client, run)).ToDictionary(s => s.GetProperty("employeeId").GetGuid());
        var anaSlip = slips[ana.Id].GetProperty("id").GetGuid();
        (await owner.Client.GetAsync($"{Runs}/{run}/payslips/{anaSlip}/pdf")).EnsureSuccessStatusCode();
        var firstPdf = await DbAsync(db => db.Payslips.Where(p => p.Id == anaSlip).Select(p => p.PdfAttachmentId).SingleAsync());
        firstPdf.Should().NotBeNull();

        var version = (await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}"))).GetProperty("version").GetUInt32();
        (await owner.Client.PostAsJsonAsync($"{Runs}/{run}/reopen", new { reason = "Correção da contadora", version })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DbAsync(db => db.Payslips.Where(p => p.PayrollRunId == run).Select(p => p.PdfAttachmentId).ToListAsync())).Should().OnlyContain(p => p == null, "reaberta: o PDF guardado deixa de valer");

        // Muda só o holerite da Ana e fecha de novo.
        var items = (await JsonAsync(await owner.Client.GetAsync("/api/v1/hr/payroll-items"))).EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString()!, i => i.GetProperty("id").GetGuid());
        var current = (await SlipsAsync(owner.Client, run)).Single(s => s.GetProperty("id").GetGuid() == anaSlip);
        (await owner.Client.PutAsJsonAsync($"{Runs}/{run}/payslips/{anaSlip}",
            new { lines = new[] { new { payrollItemId = items["SAL"], reference = "30 dias", amount = 1_100m } }, inssBase = 1_100m, fgtsBase = 1_100m, irrfBase = 1_100m, fgtsAmount = 0m, version = current.GetProperty("version").GetUInt32() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = await CloseAsync(owner, run);
        closed.StatusCode.Should().Be(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync());
        (await JsonAsync(closed)).GetProperty("changedAfterNotice").GetInt32().Should().Be(1);

        var notices = await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}/notices/sensitive"));
        notices.GetProperty("changed").GetInt32().Should().Be(1);
        notices.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("payslipId").GetGuid() == anaSlip).GetProperty("changedAfterNotice").GetBoolean().Should().BeTrue();
        var pdf = await owner.Client.GetAsync($"{Runs}/{run}/payslips/{anaSlip}/pdf");
        Text(await pdf.Content.ReadAsByteArrayAsync()).Should().Contain("1.100,00", "o PDF é refeito");
        (await DbAsync(db => db.Payslips.Where(p => p.Id == anaSlip).Select(p => p.PdfAttachmentId).SingleAsync())).Should().NotBe(firstPdf!.Value).And.NotBeNull();

        var again = await owner.Client.SendAsync(Post($"{Runs}/{run}/notify", new { onlyChanged = true }));
        (await JsonAsync(again)).GetProperty("queued").GetInt32().Should().Be(1, "só os alterados");
        (await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}/notices/sensitive"))).GetProperty("changed").GetInt32().Should().Be(0);
    }

    private static async Task<HttpResponseMessage> CloseAsync(Actor actor, Guid run)
    {
        var version = (await JsonAsync(await actor.Client.GetAsync($"{Runs}/{run}"))).GetProperty("version").GetUInt32();
        return await actor.Client.SendAsync(Post($"{Runs}/{run}/close", new { paymentDate = "2034-02-05", twoFactorCode = actor.Codes!.Next(), version, acknowledgedChanges = true }));
    }

    private Task<int> SetAccountAsync(Guid employeeId) =>
        DbAsync(async db =>
        {
            var e = await db.Employees.SingleAsync(x => x.Id == employeeId);
            e.BankCode = "077";
            e.BankAgency = "0001";
            e.BankAccount = "12345-6";
            e.BankAccountType = EmployeeBankAccountType.Checking;
            e.BankHolderName = e.Name;
            e.BankHolderCpf = e.Cpf;
            e.BankAccountVersion = 1;
            e.BankChangedAt = DateTimeOffset.UtcNow;
            return await db.SaveChangesAsync();
        });
}
