using System.IO.Compression;
using System.Net;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.HR.Application.Documents;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.HR;

public partial class PayslipDocumentTests
{
    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().Select(p => p.Text));
    }

    /// <summary>CA 1, 3 e 11: rascunho com a marca d'água e sem guardar; fechado guardado (dono Payslip) e reaproveitado; SensitiveRead; IDOR; três camadas.</summary>
    [Fact]
    public async Task Baixar_holerite_rascunho_com_marca_e_fechado_guardado_como_arquivo_do_sistema()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner.Client);
        var a = await EmployeeAsync(owner.Client, position);
        var run = await RunAsync(owner.Client, "2031-01", [a.Id]);
        await FillAsync(owner.Client, run, 7_654.32m);
        var slip = (await SlipsAsync(owner.Client, run))[0].GetProperty("id").GetGuid();
        var url = $"{Runs}/{run}/payslips/{slip}/pdf";

        var draft = await owner.Client.GetAsync(url);
        draft.StatusCode.Should().Be(HttpStatusCode.OK, await draft.Content.ReadAsStringAsync());
        draft.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        draft.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be($"holerite-2031-01-{a.Registration}.pdf", "sem CPF nem nome (CA 2)");
        draft.Headers.CacheControl!.NoStore.Should().BeTrue();
        var draftText = Text(await draft.Content.ReadAsByteArrayAsync());
        draftText.Should().Contain("RASCUNHO").And.Contain("7.654,32").And.Contain(a.Name).And.Contain($"{a.Cpf[..3]}.***.***-{a.Cpf[9..]}").And.NotContain(a.Cpf);
        (await DbAsync(db => db.Payslips.Where(p => p.Id == slip).Select(p => p.PdfAttachmentId).SingleAsync())).Should().BeNull("rascunho não é guardado (CA 3)");
        (await AuditCountAsync("Payslip.Pdf", slip.ToString(), AuditActions.SensitiveRead)).Should().Be(1);

        await SetStatusAsync(run, PayrollRunStatus.Closed);
        var closed = await owner.Client.GetAsync(url);
        closed.StatusCode.Should().Be(HttpStatusCode.OK);
        Text(await closed.Content.ReadAsByteArrayAsync()).Should().NotContain("RASCUNHO").And.Contain("7.654,32");
        var stored = await DbAsync(db => db.Payslips.Where(p => p.Id == slip).Select(p => p.PdfAttachmentId).SingleAsync());
        stored.Should().NotBeNull();
        var attachment = await DbAsync(db => db.Attachments.SingleAsync(x => x.Id == stored));
        (attachment.OwnerType, attachment.OwnerId, attachment.Origin).Should().Be(("Payslip", slip, AttachmentOrigin.System));
        (await owner.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DbAsync(db => db.Payslips.Where(p => p.Id == slip).Select(p => p.PdfAttachmentId).SingleAsync())).Should().Be(stored, "o guardado é reaproveitado");
        (await AuditCountAsync("Payslip", slip.ToString(), AuditActions.SensitiveRead)).Should().Be(2, "cada download fica registrado");
        (await owner.Client.PostAsync($"/api/v1/attachments/Payslip/{slip}", new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.7 x"u8.ToArray()), "file", "x.pdf" } }))
            .StatusCode.Should().NotBe(HttpStatusCode.Created, "o holerite não aceita anexo do usuário");

        // IDOR e camadas: holerite de outra folha, sem hr.view_sensitive, módulo não liberado.
        (await owner.Client.GetAsync($"{Runs}/{Guid.CreateVersion7()}/payslips/{slip}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var viewer = await ActorAsync("hr:view,edit");
        (await viewer.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(await viewer.Client.GetAsync(url))).Should().Be("forbidden");
        await SetHrReleasedAsync(false);
        try
        {
            var notReleased = await owner.Client.GetAsync(url);
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CodeAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetHrReleasedAsync(true);
        }
    }

    /// <summary>CA 4: acima de 300 linhas, 422 antes de gerar.</summary>
    [Fact]
    public async Task Holerite_acima_de_300_linhas_recusa_antes_de_gerar()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var a = await EmployeeAsync(owner.Client, await PositionAsync(owner.Client));
        var run = await RunAsync(owner.Client, "2031-02", [a.Id]);
        var slip = (await SlipsAsync(owner.Client, run))[0].GetProperty("id").GetGuid();
        await DbAsync(async db =>
        {
            var item = await db.PayrollItems.FirstAsync(i => i.Code == "SAL");
            db.PayslipLines.AddRange(Enumerable.Range(0, PayslipDocumentRules.MaxPdfLines + 1)
                .Select(i => new PayslipLine { PayslipId = slip, PayrollItemId = item.Id, Code = item.Code, Description = item.Name, Type = item.Kind, Amount = 1m, Sort = i }));
            return await db.SaveChangesAsync();
        });

        var refused = await owner.Client.GetAsync($"{Runs}/{run}/payslips/{slip}/pdf");
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(refused)).Should().Be(PayslipDocumentRules.TooManyLinesCode);
        (await CodeAsync(await owner.Client.PostAsync($"{Runs}/{run}/pdfs", null))).Should().Be(PayslipDocumentRules.TooManyLinesCode);
    }

    /// <summary>CA 2: até 20, o ZIP na hora (um PDF por funcionário, nome sem CPF nem nome); acima, o job com tenantId e só ids, e o aviso no sino.</summary>
    [Fact]
    public async Task Baixar_todos_gera_ZIP_na_hora_ou_em_segundo_plano_com_aviso_no_sino()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner.Client);
        var people = new List<Person>();
        for (var i = 0; i < PayslipDocumentRules.InlineBatchLimit + 1; i++)
        {
            people.Add(await EmployeeAsync(owner.Client, position, registration: i != 0));
        }

        var small = await RunAsync(owner.Client, "2031-03", people.Take(2).Select(p => p.Id));
        await FillAsync(owner.Client, small, 1_000m);
        var started = await JsonAsync(await owner.Client.PostAsync($"{Runs}/{small}/pdfs", null));
        started.GetProperty("queued").GetBoolean().Should().BeFalse();
        var zipId = started.GetProperty("attachmentId").GetGuid();
        var zip = await owner.Client.GetAsync($"{Runs}/{small}/pdfs/{zipId}");
        zip.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var archive = new ZipArchive(new MemoryStream(await zip.Content.ReadAsByteArrayAsync())))
        {
            archive.Entries.Select(e => e.Name).Should().HaveCount(2).And.OnlyContain(n => n.StartsWith("holerite-2031-03-", StringComparison.Ordinal) && n.EndsWith(".pdf", StringComparison.Ordinal));
            archive.Entries.Select(e => e.Name).Should().NotContain(n => people.Any(p => n.Contains(p.Cpf, StringComparison.Ordinal) || n.Contains(p.Name, StringComparison.Ordinal)));
        }

        (await owner.Client.GetAsync($"{Runs}/{Guid.CreateVersion7()}/pdfs/{zipId}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR");
        (await AuditCountAsync(PayslipDocumentRules.BatchOwnerType, small.ToString(), AuditActions.SensitiveRead)).Should().Be(1);

        var big = await RunAsync(owner.Client, "2031-04", people.Select(p => p.Id));
        var queued = await JsonAsync(await owner.Client.PostAsync($"{Runs}/{big}/pdfs", null));
        queued.GetProperty("queued").GetBoolean().Should().BeTrue();
        queued.GetProperty("message").GetString().Should().Be($"Gerando {people.Count} holerites…");
        var job = _jobs.Created.Last(j => j.Job.Type == typeof(PayslipBatchJob)).Job;
        job.Args[0].Should().Be(tenancy.TenantA.Id, "job com tenantId (§4.3)");
        job.Args[1].Should().BeEquivalentTo(new PayslipBatchArgs(big, owner.Id), "só ids nos argumentos");

        var instance = ActivatorUtilities.CreateInstance<PayslipBatchJob>(Factory.Services);
        await instance.ExecuteAsync(tenancy.TenantA.Id, (PayslipBatchArgs)job.Args[1], CancellationToken.None);
        var notification = await DbAsync(db => db.Notifications.Where(n => n.UserId == owner.Id).OrderByDescending(n => n.CreatedAt).FirstAsync());
        notification.Title.Should().Contain("prontos para baixar");
        var lote = Guid.Parse(notification.Link[(notification.Link.IndexOf("lote=", StringComparison.Ordinal) + 5)..]);
        using var bigZip = new ZipArchive(new MemoryStream(await owner.Client.GetByteArrayAsync($"{Runs}/{big}/pdfs/{lote}")));
        bigZip.Entries.Should().HaveCount(people.Count);
        bigZip.Entries.Select(e => e.Name).Should().OnlyHaveUniqueItems();
        var noRegistration = (await SlipsAsync(owner.Client, big)).Single(s => s.GetProperty("employeeId").GetGuid() == people[0].Id).GetProperty("id").GetGuid();
        bigZip.Entries.Select(e => e.Name).Should().Contain($"holerite-2031-04-{PayslipDocumentRules.ShortId(noRegistration)}.pdf", "sem matrícula, o id curto");
        Text(ReadEntry(bigZip.Entries[0])).Should().Contain("RASCUNHO", "folha em rascunho: todos com a marca d'água");
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
