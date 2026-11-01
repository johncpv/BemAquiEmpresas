using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Domain;
using BemAqui.HR.Application;
using BemAqui.HR.Application.TimeClock;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Settings;

public partial class PrivacyAnonymizationTests
{
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task Tabela_de_retencao_pre_configurada_editavel_com_conferencia_auditada_e_so_do_Proprietario()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var rows = (await JsonAsync(await owner.Client.GetAsync("/api/v1/settings/privacy-retention"))).EnumerateArray().ToList();
        rows.Should().HaveCount(RetentionGroups.Defaults.Count, "pré-configurada pela ADR 0018 (CA 1)");
        var labor = rows.Single(r => r.GetProperty("subjectType").GetString() == "Employee" && r.GetProperty("fieldGroup").GetString() == RetentionGroups.Labor);
        labor.GetProperty("rule").GetString().Should().Be("AfterYears");
        labor.GetProperty("years").GetInt32().Should().Be(5);
        labor.GetProperty("baseDate").GetString().Should().Be("Termination");
        var id = labor.GetProperty("id").GetGuid();
        var confirmedBefore = labor.GetProperty("confirmed").GetBoolean();

        (await owner.Client.PutAsJsonAsync($"/api/v1/settings/privacy-retention/{id}", new { rule = "AfterYears", years = 31, version = labor.GetProperty("version").GetUInt32() }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var saved = await owner.Client.PutAsJsonAsync($"/api/v1/settings/privacy-retention/{id}", new { rule = "AfterYears", years = 6, version = labor.GetProperty("version").GetUInt32() });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var edited = await JsonAsync(saved);
        edited.GetProperty("years").GetInt32().Should().Be(6);
        edited.GetProperty("confirmed").GetBoolean().Should().BeFalse("mudou, volta a \"A confirmar\"");
        var confirmed = await JsonAsync(await owner.Client.SendAsync(Post($"/api/v1/settings/privacy-retention/{id}/confirm", new { version = edited.GetProperty("version").GetUInt32() })));
        confirmed.GetProperty("confirmed").GetBoolean().Should().BeTrue();
        confirmed.GetProperty("confirmedByName").GetString().Should().NotBeNullOrEmpty();

        // CA 1: cada mudança na auditoria, com antes e depois.
        var audit = await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.Where(a => a.Entity == nameof(RetentionRule) && a.EntityId == id.ToString()).ToListAsync());
        audit.Should().Contain(a => a.Action == AuditActions.Updated && a.Changes.Replace(" ", "", StringComparison.Ordinal).Contains("\"years\":{\"new\":6,\"old\":5}", StringComparison.Ordinal));
        audit.Should().Contain(a => a.Changes.Contains("confirmedAt"));
        _ = confirmedBefore;

        // CA 11: settings.edit não basta.
        var admin = await ActorAsync("settings:view,edit");
        foreach (var response in new[]
        {
            await admin.Client.GetAsync("/api/v1/settings/privacy-retention"),
            await admin.Client.PutAsJsonAsync($"/api/v1/settings/privacy-retention/{id}", new { rule = "Anytime", years = 0, version = 0 }),
            await admin.Client.SendAsync(Post($"/api/v1/settings/privacy-retention/{id}/confirm", new { version = 0 })),
        })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // Devolve o valor sugerido para os outros testes desta classe.
        var current = (await JsonAsync(await owner.Client.GetAsync("/api/v1/settings/privacy-retention"))).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
        (await owner.Client.PutAsJsonAsync($"/api/v1/settings/privacy-retention/{id}", new { rule = "AfterYears", years = 5, version = current.GetProperty("version").GetUInt32() })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Terceiro_PF_e_anonimizado_com_nome_digitado_e_2FA_mantendo_titulos_sem_valor_original_no_banco_nem_no_log()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var name = $"Canario{tag} Silva Pereira";
        var email = $"canario.{tag}@exemplo.com";
        var phone = "119" + tag.Where(char.IsAsciiDigit).Concat("12345678").Take(8).Aggregate(string.Empty, (s, c) => s + c);
        var street = $"Rua Canario {tag}";
        var notes = $"Observacao canario {tag}";
        var cpf = NewCpf();
        var (partyId, receivableId, itemId) = await InTenantAsync(async sp =>
        {
            var parties = sp.GetRequiredService<IPartiesDb>();
            var party = new Party { Kind = PartyKind.PF, LegalName = name, Document = cpf, Email = email, Phone = phone, Notes = notes, CreatedAt = DateTimeOffset.UtcNow };
            party.Addresses.Add(new PartyAddress { PartyId = party.Id, Kind = AddressKind.Billing, ZipCode = "01001000", Street = street, Number = "10", District = "Centro", City = "São Paulo", State = "SP", IsDefault = true });
            parties.Parties.Add(party);
            await parties.SaveChangesAsync();

            // Título de 2019: o exercício + 5 anos já passou, então nome e CPF também saem (CA 3); o título fica (CA 6).
            var finance = sp.GetRequiredService<IFinanceDb>();
            var account = await finance.Accounts.Select(a => a.Id).FirstAsync();
            var receivable = new Receivable { Number = "CR-T-" + tag, PartyId = party.Id, Description = "Elevador", AccountId = account, Amount = 1_234.56m, IssueDate = new DateOnly(2019, 3, 10), DueDate = new DateOnly(2019, 4, 10) };
            finance.Receivables.Add(receivable);
            var payable = new Payable
            {
                Number = "CP-T-" + tag, PayeeType = PayeeType.Party, PartyId = party.Id, Description = "Serviço", AccountId = account, Amount = 500m, PaidAmount = 500m,
                IssueDate = new DateOnly(2019, 5, 1), Status = PayableStatus.Paid, PaymentMethod = PaymentMethodKind.Pix,
                PaymentDetails = PaymentDetails.Serialize(new PaymentDetailsInput(PaymentMethodKind.Pix, PixKeyType.Email, email, name, cpf)),
            };
            finance.Payables.Add(payable);
            var order = new PaymentOrder { Number = "OP-T-" + tag, Status = PaymentOrderStatus.Paid, Total = 500m, ExpectedDate = new DateOnly(2019, 5, 2), SubmittedAt = DateTimeOffset.UtcNow, PaidOn = new DateOnly(2019, 5, 2) };
            finance.PaymentOrders.Add(order);
            var snapshot = new PayeeSnapshot(party.Id, name, cpf, null, payable.Number, "Serviço", 500m, 0m, 500m, 0m, 500m, null, new PaymentDetailsInput(PaymentMethodKind.Pix, PixKeyType.Email, email, name, cpf), null);
            var item = new PaymentOrderItem
            {
                PaymentOrderId = order.Id, PayableId = payable.Id, Amount = 500m, ApprovedTotal = 500m,
                PayeeSnapshot = JsonSerializer.Serialize(snapshot, SnapshotJson),
            };
            finance.PaymentOrderItems.Add(item);
            await finance.SaveChangesAsync();

            var shared = sp.GetRequiredService<BemAquiDbContext>();
            shared.EmailLogs.Add(new EmailLog { To = email, Template = "quote", Subject = "Seu orçamento", Model = $"{{\"nome\":\"{name}\"}}", Cc = "copia.outra@exemplo.com", Status = EmailStatus.Sent, CreatedAt = DateTimeOffset.UtcNow });
            await shared.SaveChangesAsync();
            return (party.Id, receivable.Id, item.Id);
        });

        var request = await RequestAsync(owner.Client, "Party", partyId, kind: "Deletion");
        var id = request.GetProperty("id").GetGuid();
        request.GetProperty("canAnonymize").GetBoolean().Should().BeTrue();
        request.GetProperty("backupNotice").GetString().Should().Contain("30 dias", "CA 10");

        // CA 2: prévia por módulo, eliminação atendida por anonimização.
        var preview = await PreviewAsync(owner.Client, id);
        preview.GetProperty("deletionNotice").GetString().Should().Be("A eliminação é atendida por anonimização: documentos com movimento não podem ser apagados.");
        preview.GetProperty("confirmationText").GetString().Should().Be(name);
        preview.GetProperty("marker").GetString().Should().Be("Titular anonimizado " + request.GetProperty("number").GetString());
        Items(preview, "parties").Should().Contain(i => i.Action == "Anonymize" && i.Label.StartsWith("Nome e CPF", StringComparison.Ordinal));
        Items(preview, "finance").Should().Contain(i => i.Action == "Keep" && i.Label.StartsWith("Títulos a receber", StringComparison.Ordinal) && i.Count == 1);
        Items(preview, "shared").Should().Contain(i => i.Action == "Anonymize" && i.Count == 1);
        (await DbAsync(db => db.Parties.IgnoreQueryFilters().SingleAsync(p => p.Id == partyId))).LegalName.Should().Be(name, "a prévia não grava nada");

        // CA 8 e 11: só o Proprietário; o nome digitado e o 2FA de novo antes de qualquer gravação.
        var admin = await ActorAsync("settings:view,edit");
        (await admin.Client.SendAsync(Post($"{Api}/{id}/anonymization/preview", null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AnonymizeAsync(admin, request, name, "123456")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AnonymizeAsync(owner, request, "outro nome", owner.Codes!.Next())).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var noCode = await AnonymizeAsync(owner, request, name, null);
        noCode.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(noCode)).Should().Be("mfa_step_up_required");
        (await DbAsync(db => db.PrivacyRequests.SingleAsync(r => r.Id == id))).AnonymizationStatus.Should().Be(PrivacyAnonymizationStatus.None, "sem 2FA nada grava");

        var started = await AnonymizeAsync(owner, request, name, owner.Codes.Next());
        started.StatusCode.Should().Be(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync());
        (await JsonAsync(started)).GetProperty("request").GetProperty("anonymizationStatus").GetString().Should().Be("Running");
        var done = await RunJobAsync(owner, id);
        done.GetProperty("anonymizationStatus").GetString().Should().Be("Done", done.ToString());
        done.GetProperty("anonymizationSteps").EnumerateArray().Should().OnlyContain(s => s.GetProperty("status").GetString() == "Done");
        done.GetProperty("anonymizationSummary").GetProperty("anonymized").GetInt32().Should().BePositive();
        done.GetProperty("anonymizedAt").GetString().Should().NotBeNull();
        done.GetProperty("canAnonymize").GetBoolean().Should().BeFalse();
        done.GetProperty("subjectName").GetString().Should().Be(PrivacyRequestRules.MaskName(name), "o pedido fica só com \"Nome S.\"");

        // CA 4: marcador, inativo e marcado "Anonimizado"; o título fica com valor, data e id (CA 6).
        var marker = "Titular anonimizado " + request.GetProperty("number").GetString();
        var party = await DbAsync(db => db.Parties.IgnoreQueryFilters().Include(p => p.Addresses).SingleAsync(p => p.Id == partyId));
        party.LegalName.Should().Be(marker);
        party.Document.Should().BeNull();
        party.Email.Should().BeNull();
        party.IsActive.Should().BeFalse();
        party.AnonymizedAt.Should().NotBeNull();
        party.AnonymizationRequest.Should().Be(request.GetProperty("number").GetString());
        party.Addresses.Single().Street.Should().Be(marker);
        var receivable = await DbAsync(db => db.Receivables.SingleAsync(r => r.Id == receivableId));
        receivable.Amount.Should().Be(1_234.56m);
        receivable.IssueDate.Should().Be(new DateOnly(2019, 3, 10));
        receivable.PartyId.Should().Be(partyId);
        var detail = await JsonAsync(await owner.Client.GetAsync($"/api/v1/parties/{partyId}"));
        detail.GetProperty("anonymizationRequest").GetString().Should().Be(request.GetProperty("number").GetString());
        var edit = await owner.Client.SendAsync(Post($"/api/v1/parties/{partyId}/reactivate", null));
        edit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(edit)).Should().Be(PartyService.AnonymizedCode, "cadastro anonimizado é só leitura");

        // CA 9: o SubjectAnonymized troca o retrato do favorecido da OP paga.
        var outbox = await DbAsync(db => db.OutboxMessages.Where(m => m.Type == typeof(SubjectAnonymized).FullName).OrderByDescending(m => m.OccurredAt).FirstAsync());
        outbox.Payload.Should().Contain(partyId.ToString()).And.NotContain(name).And.NotContain(email);
        await DrainAsync();
        var snapshotAfter = await DbAsync(db => db.PaymentOrderItems.Where(i => i.Id == itemId).Select(i => i.PayeeSnapshot).SingleAsync());
        snapshotAfter.Should().Contain(marker).And.Contain("500");
        (await DbAsync(db => db.EmailLogs.CountAsync(l => l.To == marker && l.Model == "{}"))).Should().Be(1, "destinatário trocado e modelo limpo");

        // CA 6: canário em todas as tabelas do tenant (fora da auditoria), no outbox e no log.
        (await CanaryHitsAsync(name, email, phone, street, notes, cpf)).Should().BeEmpty("nenhum valor original fica no banco fora da auditoria");
        _logs.Lines.Should().NotContain(l => l.Contains(name, StringComparison.OrdinalIgnoreCase) || l.Contains(email, StringComparison.OrdinalIgnoreCase) || l.Contains(cpf, StringComparison.Ordinal));
        var anonymizedAudit = await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.Where(a => a.Action == AuditActions.Anonymized && a.EntityId == partyId.ToString()).ToListAsync());
        anonymizedAudit.Should().NotBeEmpty("a auditoria registra a ação Anonymized");
        anonymizedAudit.Should().OnlyContain(a => a.Changes.Replace(" ", "", StringComparison.Ordinal).Contains("\"anonymized\":true", StringComparison.Ordinal) && !a.Changes.Contains("\"old\"", StringComparison.Ordinal) && !a.Changes.Contains(name, StringComparison.Ordinal));

        // CA 8 e 11: irreversível, e o histórico do pedido tem a prévia e a anonimização.
        var again = await AnonymizeAsync(owner, done, PrivacyRequestRules.MaskName(name), owner.Codes.Next());
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(again)).Should().Be("privacy_anonymization_done");
        var history = (await JsonAsync(await owner.Client.GetAsync($"{Api}/{id}/history"))).EnumerateArray().Select(h => h.GetProperty("action").GetString()).ToList();
        history.Should().Contain([AuditActions.AnonymizationPreviewed, AuditActions.Anonymized]);
    }

    [Fact]
    public async Task Funcionario_ativo_retem_o_trabalhista_e_o_desligado_ha_mais_de_5_anos_tem_os_arquivos_apagados()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);

        // CA 3: ativo, só o que não tem obrigação legal sai; o resto aparece retido com o motivo.
        var active = await EmployeeAsync(owner.Client);
        var activeDocument = await InTenantAsync(async sp => (await sp.GetRequiredService<AttachmentService>().UploadCheckedAsync(EmployeeComplementService.DocumentOwner, active.Id, "rg-ativo.pdf",
            new MemoryStream("%PDF-1.7 documento ativo"u8.ToArray()), tenancy.TenantA.Id.ToString("N"))).Value);
        var activeRequest = await RequestAsync(owner.Client, "Employee", active.Id);
        var preview = await PreviewAsync(owner.Client, activeRequest.GetProperty("id").GetGuid());
        var items = Items(preview, "hr");
        items.Should().Contain(i => i.Action == "Retain" && i.Label.StartsWith("Nome, CPF", StringComparison.Ordinal) && i.Until == null);
        items.Should().Contain(i => i.Action == "Retain" && i.Label.StartsWith("Documentos, atestados", StringComparison.Ordinal) && i.Count == 1);
        preview.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("module").GetString() == "hr").GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("action").GetString() == "Retain").Should().OnlyContain(i => i.GetProperty("reason").GetString() == "obrigação trabalhista e previdenciária" || i.GetProperty("reason").GetString() == "pagamento em aberto");
        (await AnonymizeAsync(owner, activeRequest, active.Name, owner.Codes!.Next())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await RunJobAsync(owner, activeRequest.GetProperty("id").GetGuid())).GetProperty("anonymizationStatus").GetString().Should().Be("Done");
        var stillActive = await DbAsync(db => db.Employees.SingleAsync(e => e.Id == active.Id));
        (stillActive.Name, stillActive.Cpf, stillActive.AnonymizedAt).Should().Be((active.Name, active.Cpf, (DateTimeOffset?)null), "sem desligamento, nada trabalhista sai e o cadastro continua em uso");
        (await DbAsync(db => db.Attachments.SingleAsync(a => a.Id == activeDocument.Id))).ContentRemovedAt.Should().BeNull("arquivo retido continua");
        (await owner.Client.GetAsync($"/api/v1/attachments/{activeDocument.Id}/content")).StatusCode.Should().Be(HttpStatusCode.OK, "acesso pela ação do dono (hr.view_sensitive)");

        // CA 7: desligado em 2019 (+5 anos já passou): o conteúdo sai do storage e a linha fica com o marcador.
        var gone = await EmployeeAsync(owner.Client);
        var document = await InTenantAsync(async sp => (await sp.GetRequiredService<AttachmentService>().UploadCheckedAsync(EmployeeComplementService.DocumentOwner, gone.Id, "atestado.pdf",
            new MemoryStream("%PDF-1.7 atestado"u8.ToArray()), tenancy.TenantA.Id.ToString("N"))).Value);
        var path = await DbAsync(db => db.Attachments.Where(a => a.Id == document.Id).Select(a => a.StoragePath).SingleAsync());
        await DbAsync(async db =>
        {
            var e = await db.Employees.SingleAsync(x => x.Id == gone.Id);
            e.Status = EmployeeStatus.Terminated;
            e.TerminationDate = new DateOnly(2019, 6, 30);
            e.Phone = "11987654321";
            return await db.SaveChangesAsync();
        });
        var request = await RequestAsync(owner.Client, "Employee", gone.Id);
        (await AnonymizeAsync(owner, request, gone.Name, owner.Codes.Next())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var done = await RunJobAsync(owner, request.GetProperty("id").GetGuid());
        done.GetProperty("anonymizationStatus").GetString().Should().Be("Done", done.ToString());
        var marker = "Titular anonimizado " + request.GetProperty("number").GetString();
        var employee = await DbAsync(db => db.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == gone.Id));
        employee.Name.Should().Be(marker);
        employee.Cpf.Should().NotBe(gone.Cpf).And.HaveLength(11);
        employee.Phone.Should().BeNull();
        employee.AnonymizedAt.Should().NotBeNull("desligado fica marcado \"Anonimizado\"");
        var attachment = await DbAsync(db => db.Attachments.SingleAsync(a => a.Id == document.Id));
        attachment.ContentRemovedAt.Should().NotBeNull();
        attachment.FileName.Should().Be(marker);
        File.Exists(Path.Combine(_storage, path)).Should().BeFalse("o conteúdo sai do storage do tenant");
        var download = await owner.Client.GetAsync($"/api/v1/attachments/{document.Id}/content");
        download.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(download)).Should().Be("attachment.content_removed");
        var edit = await owner.Client.PostAsJsonAsync($"/api/v1/hr/employees/{gone.Id}/dependents", new { name = "Novo", birthDate = "2015-03-02", kinship = "Child", irDependent = false, familyAllowance = false, cpf = (string?)null });
        edit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(edit)).Should().Be("employee_anonymized");
        (await CanaryHitsAsync(gone.Name, gone.Cpf)).Should().BeEmpty();
    }

    [Fact]
    public async Task Espelho_de_ponto_retem_os_totais_e_perde_saude_comentarios_e_acordo_so_depois_do_prazo_do_desligamento()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var reopen = $"Reaberto canario {tag}: atestado da hernia";
        var comment = $"Revisao canario {tag}: consulta no cardiologista";
        var snapshotJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        // Espelho fechado com abono de saúde, afastamento, reabertura, revisão do dia, PDF guardado e acordo de banco de horas (RH-01c).
        async Task<(Guid Employee, Guid Sheet, Guid Pdf, string Name)> SeedAsync(DateOnly? terminated)
        {
            var employee = await EmployeeAsync(owner.Client);
            var day = new DateOnly(2019, 5, 6);
            var days = new List<TimesheetDayResult>
            {
                new(day, TimesheetDayStatus.Justified, 480, 0, 0, 0, 0, 480, 0, null, [], true, [], [new TimesheetAbsenceInput(day, null, AbsenceReason.MedicalCertificate)], null, null),
                new(day.AddDays(1), TimesheetDayStatus.OnLeave, 480, 0, 0, 0, 0, 480, 0, null, [], false, [], [], null, new TimesheetLeaveInput(day.AddDays(1), day.AddDays(20), AbsenceReason.WorkAccident)),
                new(day.AddDays(2), TimesheetDayStatus.Justified, 480, 420, 0, 0, 0, 60, 0, null, [], false, [], [new TimesheetAbsenceInput(day.AddDays(2), 60, AbsenceReason.AttendanceNote)], null, null),
            };
            var timesheet = new Timesheet
            {
                EmployeeId = employee.Id, Period = new DateOnly(2019, 5, 1), PeriodStart = new DateOnly(2019, 5, 1), PeriodEnd = new DateOnly(2019, 5, 31),
                Status = TimesheetStatus.Closed, ExpectedMinutes = 1440, WorkedMinutes = 420, JustifiedMinutes = 1020, WorkedDays = 1,
                JustifiedByReason = "MedicalCertificate=480;WorkAccident=480;AttendanceNote=60", DaysSnapshot = JsonSerializer.Serialize(days, snapshotJson),
                ReopenReason = reopen, EmployeeAckSummary = "Esperado 24:00; trabalhado 7:00", ClosedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
            };
            var sheetId = timesheet.Id;
            await DbAsync(async db =>
            {
                var e = await db.Employees.SingleAsync(x => x.Id == employee.Id);
                e.HourBankAgreementOn = new DateOnly(2018, 1, 2);
                if (terminated is { } t)
                {
                    e.Status = EmployeeStatus.Terminated;
                    e.TerminationDate = t;
                }

                db.Timesheets.Add(timesheet);
                db.TimesheetDayReviews.Add(new TimesheetDayReview { EmployeeId = employee.Id, Day = day, Comment = comment + " " + employee.Id.ToString("N")[^6..], ReviewedAt = DateTimeOffset.UtcNow });
                return await db.SaveChangesAsync();
            });
            var pdf = await InTenantAsync(async sp =>
            {
                var stored = await sp.GetRequiredService<AttachmentService>().StoreGeneratedAsync(TimesheetPdfService.OwnerType, sheetId, "espelho.pdf", "application/pdf",
                    "%PDF-1.7 espelho"u8.ToArray());
                await sp.GetRequiredService<BemAquiDbContext>().SaveChangesAsync(); // o chamador salva
                return stored;
            });
            await DbAsync(async db =>
            {
                (await db.Timesheets.SingleAsync(s => s.Id == sheetId)).PdfAttachmentId = pdf.Id;
                return await db.SaveChangesAsync();
            });
            return (employee.Id, sheetId, pdf.Id, employee.Name);
        }

        // Ativo: tudo do espelho fica retido (sem desligamento, nada trabalhista sai).
        var active = await SeedAsync(null);
        var activeRequest = await RequestAsync(owner.Client, "Employee", active.Employee);
        Items(await PreviewAsync(owner.Client, activeRequest.GetProperty("id").GetGuid()), "hr")
            .Should().Contain(i => i.Action == "Retain" && i.Label.StartsWith("Motivos de saúde, afastamentos e comentários do espelho", StringComparison.Ordinal) && i.Count == 3 && i.Until == null);
        (await AnonymizeAsync(owner, activeRequest, active.Name, owner.Codes!.Next())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await RunJobAsync(owner, activeRequest.GetProperty("id").GetGuid())).GetProperty("anonymizationStatus").GetString().Should().Be("Done");
        var kept = await DbAsync(db => db.Timesheets.SingleAsync(s => s.Id == active.Sheet));
        (kept.ReopenReason, kept.JustifiedByReason, kept.PdfAttachmentId).Should().Be((reopen, "MedicalCertificate=480;WorkAccident=480;AttendanceNote=60", (Guid?)active.Pdf));
        kept.DaysSnapshot.Should().Contain("MedicalCertificate");
        (await DbAsync(db => db.Employees.SingleAsync(e => e.Id == active.Employee))).HourBankAgreementOn.Should().Be(new DateOnly(2018, 1, 2));

        // Desligado em 2019 (+5 anos já passou): os totais ficam; saúde, afastamento, textos livres, acordo e o PDF saem.
        var gone = await SeedAsync(new DateOnly(2019, 6, 30));
        var request = await RequestAsync(owner.Client, "Employee", gone.Employee);
        var items = Items(await PreviewAsync(owner.Client, request.GetProperty("id").GetGuid()), "hr");
        items.Should().Contain(i => i.Action == "Anonymize" && i.Label.StartsWith("Motivos de saúde, afastamentos e comentários do espelho", StringComparison.Ordinal) && i.Count == 3);
        items.Should().Contain(i => i.Action == "Keep" && i.Label.StartsWith("Espelhos de ponto", StringComparison.Ordinal) && i.Count == 1);
        (await AnonymizeAsync(owner, request, gone.Name, owner.Codes.Next())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await RunJobAsync(owner, request.GetProperty("id").GetGuid())).GetProperty("anonymizationStatus").GetString().Should().Be("Done");
        var marker = "Titular anonimizado " + request.GetProperty("number").GetString();

        var sheet = await DbAsync(db => db.Timesheets.SingleAsync(s => s.Id == gone.Sheet));
        (sheet.ExpectedMinutes, sheet.WorkedMinutes, sheet.JustifiedMinutes, sheet.Status, sheet.EmployeeAckSummary)
            .Should().Be((1440, 420, 1020, TimesheetStatus.Closed, "Esperado 24:00; trabalhado 7:00"), "os totais do espelho fechado são obrigação trabalhista");
        sheet.JustifiedByReason.Should().Be("AttendanceNote=60;JustifiedAbsence=960", "as horas abonadas continuam batendo, sem o motivo de saúde");
        sheet.ReopenReason.Should().Be(marker);
        sheet.PdfAttachmentId.Should().BeNull();
        var frozen = JsonSerializer.Deserialize<List<TimesheetDayResult>>(sheet.DaysSnapshot!, snapshotJson)!;
        frozen.Select(d => d.JustifiedMinutes).Should().Equal(480, 480, 60);
        frozen.SelectMany(d => d.Absences).Select(a => a.Reason).Should().Equal(AbsenceReason.JustifiedAbsence, AbsenceReason.AttendanceNote);
        frozen[1].Leave.Should().NotBeNull();
        frozen[1].Leave!.Reason.Should().BeNull("o tipo do afastamento é dado de saúde");
        sheet.DaysSnapshot.Should().NotContain("MedicalCertificate").And.NotContain("WorkAccident");
        (await DbAsync(db => db.TimesheetDayReviews.SingleAsync(r => r.EmployeeId == gone.Employee))).Comment.Should().Be(marker);
        (await DbAsync(db => db.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == gone.Employee))).HourBankAgreementOn.Should().BeNull();
        (await DbAsync(db => db.Attachments.SingleAsync(a => a.Id == gone.Pdf))).ContentRemovedAt.Should().NotBeNull();

        // Canário: o comentário da revisão e o motivo da reabertura do desligado não sobram em nenhuma tabela fora da auditoria.
        (await CanaryHitsAsync(comment + " " + gone.Employee.ToString("N")[^6..], gone.Name)).Should().BeEmpty();
        (await CanaryHitsAsync(reopen)).Should().Equal(["hr.timesheets.reopen_reason"], "só o espelho do funcionário ativo continua com o motivo, retido");
    }

    /// <summary>
    /// FLH-02a: o retrato das entradas, os avisos, a memória de cálculo e o motivo do avulso do holerite calculado seguem a retenção trabalhista:
    /// retidos no ativo; no desligado há mais de 5 anos, saem (o motivo vira o marcador) e os valores do holerite ficam.
    /// </summary>
    [Fact]
    public async Task Folha_calculada_retem_retrato_e_memoria_e_perde_depois_do_prazo_do_desligamento()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var tag = Guid.NewGuid().ToString("N")[..8];

        async Task<(Guid Employee, Guid Payslip, string Name, string Note)> SeedAsync(DateOnly? terminated, int month)
        {
            var employee = await EmployeeAsync(owner.Client);
            var note = $"Pensao canario {tag} {employee.Id.ToString("N")[^6..]}: oficio da vara de familia";
            var run = new PayrollRun { Period = new DateOnly(2019, month, 1), Type = PayrollRunType.Monthly, Mode = PayrollRunMode.Calculated, CreatedAt = DateTimeOffset.UtcNow, CalculatedAt = DateTimeOffset.UtcNow, CalculatorVersion = PayrollCalculator.Version };
            var snapshot = new PayrollCalcSnapshot(3000m, new DateOnly(2015, 1, 2), terminated, true, 220m, null, false, false, [new PayrollCalcDependent(new DateOnly(2014, 3, 9), true, true, true)],
                [new PayrollCalcLeave(new DateOnly(2019, 5, 2), new DateOnly(2019, 5, 30), AbsenceReason.MedicalCertificate)], true, 0, 0, [], PayrollCalcParameters.Default);
            var payslip = new Payslip
            {
                PayrollRunId = run.Id, EmployeeId = employee.Id, Gross = 3000m, Deductions = 500m, Net = 2500m, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                CalculatedAt = DateTimeOffset.UtcNow, CalculatorVersion = PayrollCalculator.Version, InputsSnapshot = PayrollCalculator.WriteSnapshot(snapshot),
                CalculationIssues = PayrollCalculator.WriteIssues([new PayrollCalcIssue("family_allowance_docs", "documentos do salário-família pendentes.", false)]),
                EmployerChargesMemory = PayrollCalculator.WriteMemory(["Lucro Presumido sobre R$ 3.000,00"]),
            };
            await DbAsync(async db =>
            {
                var item = await db.PayrollItems.Where(i => i.Code == "PENSAO").Select(i => i.Id).SingleAsync();
                var salary = await db.PayrollItems.Where(i => i.Code == "SAL").Select(i => i.Id).SingleAsync();
                var e = await db.Employees.SingleAsync(x => x.Id == employee.Id);
                if (terminated is { } t)
                {
                    e.Status = EmployeeStatus.Terminated;
                    e.TerminationDate = t;
                }

                db.PayrollRuns.Add(run);
                db.Payslips.Add(payslip);
                db.PayslipLines.Add(new PayslipLine { PayslipId = payslip.Id, PayrollItemId = salary, Code = "SAL", Description = "Salário", Type = PayrollItemKind.Earning, Amount = 3000m, Calculated = true, Memory = PayrollCalculator.WriteMemory(["Salário base R$ 3.000,00."]) });
                db.PayslipLines.Add(new PayslipLine { PayslipId = payslip.Id, PayrollItemId = item, Code = "PENSAO", Description = "Pensão alimentícia", Type = PayrollItemKind.Deduction, Amount = 500m, Sort = 1, Note = note, Memory = PayrollCalculator.WriteMemory(["Lançamento avulso: " + note]) });
                return await db.SaveChangesAsync();
            });
            return (employee.Id, payslip.Id, employee.Name, note);
        }

        // Ativo: retrato, memória e motivo ficam retidos.
        var active = await SeedAsync(null, 5);
        var activeRequest = await RequestAsync(owner.Client, "Employee", active.Employee);
        Items(await PreviewAsync(owner.Client, activeRequest.GetProperty("id").GetGuid()), "hr")
            .Should().Contain(i => i.Action == "Retain" && i.Label.StartsWith("Retrato das entradas", StringComparison.Ordinal) && i.Count == 3 && i.Until == null);
        (await AnonymizeAsync(owner, activeRequest, active.Name, owner.Codes!.Next())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await RunJobAsync(owner, activeRequest.GetProperty("id").GetGuid())).GetProperty("anonymizationStatus").GetString().Should().Be("Done");
        (await DbAsync(db => db.Payslips.SingleAsync(p => p.Id == active.Payslip))).InputsSnapshot.Should().NotBeNull();

        // Desligado em 2019 (+5 anos já passou): o retrato, os avisos e as memórias saem; o motivo vira o marcador; os valores ficam.
        var gone = await SeedAsync(new DateOnly(2019, 6, 30), 6);
        var request = await RequestAsync(owner.Client, "Employee", gone.Employee);
        Items(await PreviewAsync(owner.Client, request.GetProperty("id").GetGuid()), "hr")
            .Should().Contain(i => i.Action == "Anonymize" && i.Label.StartsWith("Retrato das entradas", StringComparison.Ordinal) && i.Count == 3);
        (await AnonymizeAsync(owner, request, gone.Name, owner.Codes.Next())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await RunJobAsync(owner, request.GetProperty("id").GetGuid())).GetProperty("anonymizationStatus").GetString().Should().Be("Done");
        var marker = "Titular anonimizado " + request.GetProperty("number").GetString();
        var slip = await DbAsync(db => db.Payslips.SingleAsync(p => p.Id == gone.Payslip));
        (slip.InputsSnapshot, slip.CalculationIssues, slip.EmployerChargesMemory).Should().Be(((string?)null, (string?)null, (string?)null));
        (slip.Gross, slip.Net).Should().Be((3000m, 2500m), "os valores do holerite são documento com movimento");
        var lines = await DbAsync(db => db.PayslipLines.Where(l => l.PayslipId == gone.Payslip).OrderBy(l => l.Sort).ToListAsync());
        lines.Should().OnlyContain(l => l.Memory == null);
        lines.Select(l => l.Note).Should().Equal(null, marker);
        lines.Select(l => l.Amount).Should().Equal(3000m, 500m);

        // Canário: o motivo do avulso do desligado não sobra em nenhuma tabela; o do ativo, retido, só nas linhas.
        (await CanaryHitsAsync(gone.Note, gone.Name)).Should().BeEmpty();
        (await CanaryHitsAsync(active.Note)).Should().BeEquivalentTo(["hr.payslip_lines.note", "hr.payslip_lines.memory"]);
    }

    [Fact]
    public async Task NFe_emitida_fica_com_o_destinatario_ate_o_prazo_fiscal_e_perde_o_contato_ja_e_o_rascunho_descartado_sai_inteiro()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var name = $"Canario{tag} Nota Pereira";
        var email = $"nota.{tag}@exemplo.com";
        var oldStreet = $"Rua Canario {tag} Antiga";
        var draftStreet = $"Rua Canario {tag} Rascunho";
        var phone = "11" + string.Concat(tag.Where(char.IsAsciiDigit).Concat("987654321").Take(9));
        var cpf = NewCpf();
        var today = DateOnly.Parse(Today, System.Globalization.CultureInfo.InvariantCulture);
        Guid old = default, recent = default, discarded = default, rejected = default;
        var partyId = await InTenantAsync(async sp =>
        {
            var parties = sp.GetRequiredService<IPartiesDb>();
            var party = new Party { Kind = PartyKind.PF, LegalName = name, Document = cpf, Email = email, CreatedAt = DateTimeOffset.UtcNow };
            parties.Parties.Add(party);
            await parties.SaveChangesAsync();

            var fiscal = sp.GetRequiredService<IFiscalDb>();
            OutgoingInvoice Note(string street, OutgoingInvoiceStatus status, DateOnly? issuedOn, long? number, bool discard = false)
            {
                var invoice = new OutgoingInvoice
                {
                    PartyId = party.Id, RecipientName = name, RecipientDocument = cpf, RecipientStateRegistration = "ISENTO", RecipientEmail = email, RecipientPhone = phone,
                    RecipientStreet = street, RecipientNumber = "10", RecipientDistrict = "Centro", RecipientPostalCode = "01001000", RecipientCityName = "São Paulo", RecipientState = "SP",
                    EmailCc = $"{email}, fiscal.outra@exemplo.com", TotalAmount = 4_321.09m, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                };
                fiscal.OutgoingInvoices.Add(invoice);
                var entry = fiscal.Entry(invoice);
                entry.Property(i => i.Status).CurrentValue = status;
                entry.Property(i => i.IssuedOn).CurrentValue = issuedOn;
                entry.Property(i => i.Number).CurrentValue = number;
                entry.Property(i => i.Series).CurrentValue = number is null ? null : 1;
                entry.Property(i => i.AccessKey).CurrentValue = number is null ? null : "3519" + tag + number;
                entry.Property(i => i.DiscardedAt).CurrentValue = discard ? DateTimeOffset.UtcNow : null;
                return invoice;
            }

            // Autorizada em 2019 (exercício + 5 anos já passou), autorizada hoje (retida), rascunho descartado e rejeitada à espera da correção.
            old = Note(oldStreet, OutgoingInvoiceStatus.Authorized, new DateOnly(2019, 3, 10), 101).Id;
            recent = Note("Rua Atual 1", OutgoingInvoiceStatus.Authorized, today, 102).Id;
            discarded = Note(draftStreet, OutgoingInvoiceStatus.Draft, null, null, discard: true).Id;
            rejected = Note("Rua Atual 1", OutgoingInvoiceStatus.Rejected, null, 103).Id;
            await fiscal.SaveChangesAsync();
            return party.Id;
        });

        var request = await RequestAsync(owner.Client, "Party", partyId);
        var id = request.GetProperty("id").GetGuid();
        var items = Items(await PreviewAsync(owner.Client, id), "fiscal");
        var until = new DateOnly(today.Year + 5, 12, 31).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        items.Should().Contain(i => i.Action == "Keep" && i.Label.StartsWith("Notas fiscais de saída emitidas", StringComparison.Ordinal) && i.Count == 2);
        items.Should().Contain(i => i.Action == "Anonymize" && i.Label.StartsWith("Nome, CPF", StringComparison.Ordinal) && i.Count == 1);
        items.Should().Contain(i => i.Action == "Retain" && i.Label.StartsWith("Nome, CPF", StringComparison.Ordinal) && i.Count == 1 && i.Until == until, "fim do exercício da nota + 5 anos");
        items.Should().Contain(i => i.Action == "Retain" && i.Label.StartsWith("Endereço", StringComparison.Ordinal) && i.Count == 1 && i.Until == until);
        items.Should().Contain(i => i.Action == "Anonymize" && i.Label.StartsWith("E-mail e telefone", StringComparison.Ordinal) && i.Count == 2);
        items.Should().Contain(i => i.Action == "Anonymize" && i.Label.StartsWith("Destinatário dos rascunhos", StringComparison.Ordinal) && i.Count == 1);
        items.Should().Contain(i => i.Action == "Retain" && i.Label.StartsWith("Destinatário das notas em andamento", StringComparison.Ordinal) && i.Count == 1);

        (await AnonymizeAsync(owner, request, name, owner.Codes!.Next())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await RunJobAsync(owner, id)).GetProperty("anonymizationStatus").GetString().Should().Be("Done");
        var marker = "Titular anonimizado " + request.GetProperty("number").GetString();
        var notes = await InTenantAsync(sp => sp.GetRequiredService<IFiscalDb>().OutgoingInvoices.AsNoTracking().Where(i => i.PartyId == partyId).ToDictionaryAsync(i => i.Id));

        var o = notes[old];
        (o.Number, o.Status, o.TotalAmount, o.AccessKey).Should().Be((101L, OutgoingInvoiceStatus.Authorized, 4_321.09m, "3519" + tag + "101"), "a nota emitida continua existindo");
        (o.RecipientName, o.RecipientDocument, o.RecipientStateRegistration, o.RecipientStreet, o.RecipientPostalCode).Should().Be((marker, (string?)null, (string?)null, (string?)null, (string?)null));
        (o.RecipientCityName, o.RecipientState).Should().Be(("São Paulo", "SP"), "município e UF não identificam");
        var r = notes[recent];
        (r.RecipientName, r.RecipientDocument, r.RecipientStreet).Should().Be((name, cpf, "Rua Atual 1"), "retido até o fim do prazo fiscal");
        foreach (var n in new[] { o, r })
        {
            (n.RecipientEmail, n.RecipientPhone, n.EmailCc).Should().Be(((string?)null, (string?)null, $"{marker}, fiscal.outra@exemplo.com"), "contato não é exigido no XML");
        }

        var d = notes[discarded];
        (d.RecipientName, d.RecipientDocument, d.RecipientStreet, d.RecipientEmail, d.RecipientPhone).Should().Be((marker, (string?)null, (string?)null, (string?)null, (string?)null));
        var j = notes[rejected];
        (j.RecipientName, j.RecipientEmail).Should().Be((name, email), "nota em andamento fica até o desfecho");

        // Canário: o endereço da nota antiga e o do rascunho não sobram em nenhuma tabela fora da auditoria; o telefone e o e-mail, só na nota em andamento.
        (await CanaryHitsAsync(oldStreet, draftStreet)).Should().BeEmpty();
        (await CanaryHitsAsync(phone, email)).Should().BeEquivalentTo(["fiscal.outgoing_invoices.recipient_email", "fiscal.outgoing_invoices.recipient_phone", "fiscal.outgoing_invoices.email_cc"],
            "o contato sai do cadastro e das notas emitidas; fica só na nota em andamento");
    }

    [Fact]
    public async Task Movimentacao_de_bem_fica_e_o_motivo_em_texto_livre_vira_o_marcador()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var reason = $"Entregue a canario {tag} na obra";
        var employee = await EmployeeAsync(owner.Client);
        var (assetId, movementId) = await InTenantAsync(async sp =>
        {
            var assets = sp.GetRequiredService<BemAqui.Assets.Application.IAssetsDb>();
            var asset = new BemAqui.Assets.Domain.Asset { Name = "Furadeira " + tag, Type = BemAqui.Assets.Contracts.AssetType.Tool, CreatedAt = DateTimeOffset.UtcNow };
            assets.Assets.Add(asset);
            assets.Entry(asset).Property(a => a.Tag).CurrentValue = "LG" + tag;
            var movement = BemAqui.Assets.Domain.AssetMovement.Transfer(asset, new BemAqui.Assets.Domain.AssetPlacement(null, employee.Id, null), new DateOnly(2026, 1, 5), reason, null, DateTimeOffset.UtcNow);
            assets.AssetMovements.Add(movement);
            await assets.SaveChangesAsync();
            return (asset.Id, movement.Id);
        });

        var request = await RequestAsync(owner.Client, "Employee", employee.Id);
        var items = Items(await PreviewAsync(owner.Client, request.GetProperty("id").GetGuid()), "assets");
        items.Should().Contain(i => i.Action == "Keep" && i.Label.StartsWith("Bens sob responsabilidade", StringComparison.Ordinal) && i.Count == 1);
        items.Should().Contain(i => i.Action == "Keep" && i.Label.StartsWith("Movimentações de bens", StringComparison.Ordinal) && i.Count == 1);
        items.Should().Contain(i => i.Action == "Anonymize" && i.Label.StartsWith("Motivo das movimentações", StringComparison.Ordinal) && i.Count == 1);
        (await AnonymizeAsync(owner, request, employee.Name, owner.Codes!.Next())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await RunJobAsync(owner, request.GetProperty("id").GetGuid())).GetProperty("anonymizationStatus").GetString().Should().Be("Done");

        var moved = await InTenantAsync(sp => sp.GetRequiredService<BemAqui.Assets.Application.IAssetsDb>().AssetMovements.AsNoTracking().SingleAsync(m => m.Id == movementId));
        (moved.Reason, moved.ToEmployeeId, moved.AssetId, moved.MovedOn).Should().Be(("Titular anonimizado " + request.GetProperty("number").GetString(), (Guid?)employee.Id, assetId, new DateOnly(2026, 1, 5)),
            "o movimento fica; só o texto livre sai");
        (await CanaryHitsAsync(reason)).Should().BeEmpty();
    }

    [Fact]
    public async Task Modulo_que_falha_deixa_parcial_e_tentar_de_novo_com_2FA_refaz_so_o_que_falta_de_forma_idempotente()
    {
        var state = new FlakyState();
        await using var flaky = Build(s => s.AddScoped<IPersonalDataProvider>(_ => new FlakyProvider(state)));
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true, factory: flaky);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var contactName = $"Contato{tag} Souza";
        var contactId = await InTenantAsync(async sp =>
        {
            var parties = sp.GetRequiredService<IPartiesDb>();
            var company = new Party { Kind = PartyKind.PJ, LegalName = "Empresa " + tag, CreatedAt = DateTimeOffset.UtcNow };
            var contact = new PartyContact { PartyId = company.Id, Name = contactName, Email = $"contato.{tag}@exemplo.com", Phone = "11977776666", Role = "Compras" };
            company.Contacts.Add(contact);
            parties.Parties.Add(company);
            await parties.SaveChangesAsync();
            return contact.Id;
        }, flaky);

        var request = await RequestAsync(owner.Client, "Contact", contactId);
        var id = request.GetProperty("id").GetGuid();
        var first = await AnonymizeAsync(owner, request, contactName, owner.Codes!.Next());
        first.StatusCode.Should().Be(HttpStatusCode.Accepted, await first.Content.ReadAsStringAsync());
        var partial = await RunJobAsync(owner, id, flaky);
        partial.GetProperty("anonymizationStatus").GetString().Should().Be("Partial", "CA 5");
        var steps = partial.GetProperty("anonymizationSteps").EnumerateArray().ToDictionary(s => s.GetProperty("module").GetString()!, s => s.GetProperty("status").GetString());
        steps["zz.teste"].Should().Be("Failed");
        steps["shared"].Should().Be("Done", "os módulos que rodaram ficam feitos (uma transação por módulo)");
        steps["parties"].Should().Be("Pending", "o dono do cadastro só roda com os outros feitos");
        (await DbAsync(db => db.PartyContacts.SingleAsync(c => c.Id == contactId))).Name.Should().Be(contactName);
        partial.GetProperty("canAnonymize").GetBoolean().Should().BeTrue("\"Tentar de novo\"");
        (await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().Notifications.CountAsync(n => n.UserId == owner.Id && EF.Functions.Like(n.Title, "Anonimização parcial%")))).Should().Be(1);

        // Tentar de novo exige o 2FA de novo e refaz só o que falta.
        var retry = await AnonymizeAsync(owner, partial, contactName, null);
        (await CodeAsync(retry)).Should().Be("mfa_step_up_required");
        (await AnonymizeAsync(owner, partial, contactName, owner.Codes.Next())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var done = await RunJobAsync(owner, id, flaky);
        done.GetProperty("anonymizationStatus").GetString().Should().Be("Done", done.ToString());
        state.Next().Should().Be(3, "o módulo de teste rodou duas vezes (falha e retentativa); os já feitos não rodaram de novo");
        var contact = await DbAsync(db => db.PartyContacts.SingleAsync(c => c.Id == contactId));
        (contact.Name, contact.Email, contact.Phone).Should().Be(("Titular anonimizado " + request.GetProperty("number").GetString(), (string?)null, (string?)null));

        // Idempotente pelo número do pedido: rodar o provedor de novo dá o mesmo resultado.
        await InTenantAsync(async sp =>
        {
            var provider = sp.GetServices<IPersonalDataProvider>().Single(p => p.Module == "parties");
            var subject = new PersonalDataSubject(PersonalDataSubjectKind.Contact, contactId, contact.Name, [], PartyId: contact.PartyId);
            var context = new PersonalDataAnonymizationContext(subject, request.GetProperty("number").GetString()!, DateOnly.FromDateTime(DateTime.UtcNow), PersonalDataRetention.Defaults, new Dictionary<string, DateOnly>());
            await provider.AnonymizeAsync(context);
            await provider.AnonymizeAsync(context);
            return 0;
        }, flaky);
        var after = await DbAsync(db => db.PartyContacts.SingleAsync(c => c.Id == contactId));
        (after.Name, after.Email, after.Phone, after.Role).Should().Be((contact.Name, contact.Email, contact.Phone, contact.Role));
    }

    [Fact]
    public async Task Usuario_fica_bloqueado_com_email_trocado_e_ninguem_anonimiza_o_proprio_usuario()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var user = await ActorAsync("settings:view");
        await InTenantAsync(async sp =>
        {
            sp.GetRequiredService<BemAqui.BuildingBlocks.Notifications.INotificationService>().Notify(user.Id, "teste", "Aviso de teste do usuário", "/app");
            return await sp.GetRequiredService<BemAquiDbContext>().SaveChangesAsync();
        });
        var request = await RequestAsync(owner.Client, "User", user.Id);
        var name = request.GetProperty("subjectName").GetString()!;
        (await AnonymizeAsync(owner, request, name, owner.Codes!.Next())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await RunJobAsync(owner, request.GetProperty("id").GetGuid())).GetProperty("anonymizationStatus").GetString().Should().Be("Done");
        var stored = await DbAsync(db => db.Users.SingleAsync(u => u.Id == user.Id));
        stored.Status.Should().Be(UserStatus.Inactive);
        stored.Email.Should().EndWith("@anonimizado.invalid").And.NotBe(user.Email);
        stored.PasswordHash.Should().BeNull();
        stored.AnonymizedAt.Should().NotBeNull();
        (await user.Client.GetAsync("/api/v1/settings/privacy-retention")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a sessão cai pela troca do sstamp");
        (await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().Notifications.Where(n => n.UserId == user.Id).Select(n => n.Title).ToListAsync()))
            .Should().OnlyContain(t => t.StartsWith(PersonalDataMarker.Prefix, StringComparison.Ordinal));
        (await CanaryHitsAsync(user.Email)).Should().BeEmpty();

        // Ninguém anonimiza o próprio usuário; pedido de acesso não anonimiza.
        var self = await RequestAsync(owner.Client, "User", owner.Id);
        var blocked = await owner.Client.SendAsync(Post($"{Api}/{self.GetProperty("id").GetGuid()}/anonymization/preview", null));
        (await JsonAsync(blocked)).GetProperty("blockers").EnumerateArray().Should().Contain(b => b.GetString()!.Contains("próprio usuário", StringComparison.Ordinal));
        var access = await RequestAsync(owner.Client, "User", user.Id, kind: "Access");
        (await CodeAsync(await owner.Client.SendAsync(Post($"{Api}/{access.GetProperty("id").GetGuid()}/anonymization/preview", null)))).Should().Be("privacy_request_not_anonymization");
        var unverified = await RequestAsync(owner.Client, "User", owner.Id, verify: false);
        (await CodeAsync(await owner.Client.SendAsync(Post($"{Api}/{unverified.GetProperty("id").GetGuid()}/anonymization/preview", null)))).Should().Be("subject_identity_not_verified");
    }
}
