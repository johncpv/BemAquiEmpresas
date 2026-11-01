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
using BemAqui.HR.Application;
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
        (await AnonymizeAsync(owner, request, contactName, owner.Codes!.Next())).StatusCode.Should().Be(HttpStatusCode.Accepted);
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
