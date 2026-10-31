using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.Identity.Application.Privacy;
using BemAqui.Identity.Domain;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Settings;

public partial class PrivacyRequestTests
{
    [Fact]
    public async Task Pedido_nasce_aberto_com_numero_e_prazo_em_dias_corridos_e_so_o_Proprietario_atende()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var created = await UnknownAsync(owner.Client, requestedOn: "2026-10-01");
        created.GetProperty("number").GetString().Should().MatchRegex(@"^LGPD-\d{4}-\d{4}$");
        created.GetProperty("status").GetString().Should().Be("Open");
        created.GetProperty("dueOn").GetString().Should().Be("2026-10-16", "15 dias corridos (CA 1)");

        var settings = await JsonAsync(await owner.Client.GetAsync($"{Api}/settings"));
        settings.GetProperty("responseDays").GetInt32().Should().Be(15, "pré-configurado");
        (await owner.Client.PutAsJsonAsync($"{Api}/settings", new { responseDays = 40 })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.Client.PutAsJsonAsync($"{Api}/settings", new { responseDays = 10 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await UnknownAsync(owner.Client, requestedOn: "2026-10-01")).GetProperty("dueOn").GetString().Should().Be("2026-10-11");
        (await owner.Client.PutAsJsonAsync($"{Api}/settings", new { responseDays = 15 })).StatusCode.Should().Be(HttpStatusCode.OK);

        // CA 2: settings.edit e hr.view_sensitive não bastam; só o Proprietário.
        var admin = await ActorAsync("settings:view,edit;hr:view,view_sensitive");
        var id = created.GetProperty("id").GetGuid();
        foreach (var response in new[]
        {
            await admin.Client.GetAsync(Api), await admin.Client.GetAsync($"{Api}/{id}"), await admin.Client.GetAsync($"{Api}/{id}/history"),
            await admin.Client.GetAsync($"{Api}/settings"), await admin.Client.GetAsync($"{Api}/subjects?kind=Employee&q=fu"),
            await admin.Client.SendAsync(Post(Api, new { subjectType = "Unknown", subjectName = "X Y", subjectContact = "x@y.com", kind = "Access", requestedOn = Today, channel = "Email" })),
            await admin.Client.SendAsync(Post($"{Api}/{id}/identity", new { method = "InPerson", checkedOn = Today, version = 0 })),
            await admin.Client.SendAsync(Post($"{Api}/{id}/export", new { version = 0 })),
        })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CodeAsync(response)).Should().Be("forbidden");
        }

        // A lista mascara o titular (padrão 8); o detalhe do Proprietário mostra em claro.
        var list = await JsonAsync(await owner.Client.GetAsync(Api + "?pageSize=100"));
        var row = list.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
        row.GetProperty("subjectMasked").GetString().Should().EndWith(" S.").And.NotContain("Souza");
        created.GetProperty("subjectName").GetString().Should().EndWith("Souza");
    }

    [Fact]
    public async Task Sem_identidade_conferida_nao_exporta_e_o_pedido_e_concluido_ou_recusado_com_historico()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var request = await UnknownAsync(owner.Client);
        var id = request.GetProperty("id").GetGuid();
        request.GetProperty("canExport").GetBoolean().Should().BeFalse();
        var blocked = await owner.Client.SendAsync(Post($"{Api}/{id}/export", new { version = request.GetProperty("version").GetUInt32() }));
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(blocked)).Should().Be("subject_identity_not_verified", "CA 5");

        request = await VerifyAsync(owner.Client, request);
        request.GetProperty("identityVerified").GetBoolean().Should().BeTrue();
        request.GetProperty("identityMethod").GetString().Should().Be("DocumentPresented");
        request.GetProperty("canExport").GetBoolean().Should().BeTrue();

        var missing = await owner.Client.SendAsync(Post($"{Api}/{id}/conclude", new { respondedOn = Today, response = "", fileSent = false, version = request.GetProperty("version").GetUInt32() }));
        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var done = await owner.Client.SendAsync(Post($"{Api}/{id}/conclude", new { respondedOn = Today, response = "Enviamos o arquivo por e-mail.", fileSent = true, version = request.GetProperty("version").GetUInt32() }));
        done.StatusCode.Should().Be(HttpStatusCode.OK, await done.Content.ReadAsStringAsync());
        var doneBody = await JsonAsync(done);
        doneBody.GetProperty("status").GetString().Should().Be("Done");
        (await owner.Client.SendAsync(Post($"{Api}/{id}/refuse", new { reason = "x", version = doneBody.GetProperty("version").GetUInt32() }))).StatusCode.Should().Be(HttpStatusCode.Conflict, "fechado não muda");

        var other = await UnknownAsync(owner.Client, "Deletion");
        var noReason = await owner.Client.SendAsync(Post($"{Api}/{other.GetProperty("id").GetGuid()}/refuse", new { reason = " ", version = other.GetProperty("version").GetUInt32() }));
        noReason.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "recusar exige o motivo (CA 4)");
        var refused = await JsonAsync(await owner.Client.SendAsync(Post($"{Api}/{other.GetProperty("id").GetGuid()}/refuse", new { reason = "Retenção legal: documentos fiscais.", version = other.GetProperty("version").GetUInt32() })));
        refused.GetProperty("status").GetString().Should().Be("Refused");
        (await DbAsync(db => db.PrivacyRequests.CountAsync(r => r.Id == id || r.Id == other.GetProperty("id").GetGuid()))).Should().Be(2, "nenhum pedido é apagado");

        // CA 12: histórico com quem, quando, o quê e o IP; o titular sai mascarado na auditoria.
        var history = (await JsonAsync(await owner.Client.GetAsync($"{Api}/{id}/history"))).EnumerateArray().ToList();
        history.Select(h => h.GetProperty("action").GetString()).Should().Contain([AuditActions.Created, AuditActions.Updated]);
        history.Should().OnlyContain(h => h.GetProperty("userName").GetString()!.Length > 0);
        // O TestServer não tem IP remoto; a captura do IP pela auditoria já é coberta em AuditingTests. Aqui basta o campo sair no histórico.
        history.Select(h => h.TryGetProperty("ip", out var ip)).Should().OnlyContain(present => present,"o histórico expõe o IP da auditoria");
        string.Join(' ', history.Select(h => h.GetProperty("changes").GetString())).Should().NotContain("Souza", "nome do titular mascarado na auditoria (ADR 0018)");
    }

    [Fact]
    public async Task Exportacao_do_funcionario_traz_cada_modulo_mascara_terceiros_e_baixa_por_link_de_uso_unico()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var employee = await EmployeeAsync(owner.Client);
        var dependentCpf = NewCpf();
        (await owner.Client.PostAsJsonAsync($"/api/v1/hr/employees/{employee.Id}/dependents", new { name = "Ana Dependente Lima", birthDate = "2015-03-02", kinship = "Child", irDependent = true, familyAllowance = false, cpf = dependentCpf }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var linked = await ActorAsync(null, totp: true, employeeId: employee.Id);
        var document = await InTenantAsync(async sp => (await sp.GetRequiredService<AttachmentService>().UploadCheckedAsync("EmployeeDocument", employee.Id, "rg-frente.pdf",
            new MemoryStream("%PDF-1.7 documento"u8.ToArray()), tenancy.TenantA.Id.ToString("N"))).Value);
        var canary = await InTenantAsync(async sp =>
        {
            var db = sp.GetRequiredService<BemAqui.Identity.Application.IIdentityDb>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == linked.Id);
            return (user.PasswordHash!, user.TotpSecretCipher!, user.SecurityStamp);
        });

        var request = await CreateAsync(owner.Client, new { subjectType = "Employee", subjectId = employee.Id, kind = "Access", requestedOn = Today, channel = "InPerson", notes = (string?)null });
        request.GetProperty("subjectName").GetString().Should().Be(employee.Name);
        request = await VerifyAsync(owner.Client, request);
        var id = request.GetProperty("id").GetGuid();
        var reads = await AuditCountAsync(id, AuditActions.SensitiveRead);
        var exported = await ExportAsync(owner, request);
        exported.GetProperty("exportStatus").GetString().Should().Be("Ready", exported.ToString());

        var (json, entries, response) = await DownloadAsync(owner.Client, id);
        response.Headers.CacheControl!.NoStore.Should().BeTrue("CA 10");
        entries.Should().Contain(["dados.json", "dados.pdf", "anexos/rg-frente.pdf"]);
        json.Should().Contain(employee.Name).And.Contain(BemAqui.SharedKernel.BrazilianDocuments.Format(employee.Cpf), "o CPF do titular sai inteiro");
        json.Should().Contain("Ana Dependente Lima").And.NotContain(dependentCpf, "CPF de dependente mascarado (CA 9)").And.Contain(BemAqui.SharedKernel.BrazilianDocuments.MaskCpf(dependentCpf));
        json.Should().Contain("\"modulo\": \"hr\"").And.Contain("\"modulo\": \"identity\"").And.Contain("hr.employee_dependents").And.Contain("identity.refresh_tokens");
        // Canário (CA 10): hash de senha, segredo TOTP, security stamp e o nome do arquivo no storage nunca saem.
        json.Should().NotContain(canary.Item1).And.NotContain(canary.Item2).And.NotContain(canary.Item3).And.NotContain(document.Id.ToString());
        (await AuditCountAsync(id, AuditActions.SensitiveRead)).Should().Be(reads + 2, "exportar e baixar ficam registrados com o número do pedido");
        (await AuditCountAsync(id, AuditActions.Exported)).Should().Be(1);
        (await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().Notifications.CountAsync(n => n.UserId == owner.Id && n.Link.EndsWith(id.ToString())))).Should().Be(1, "aviso no sino");

        // Uso único e expiração; outro pedido não aceita o token (IDOR).
        var link = (await JsonAsync(await owner.Client.SendAsync(Post($"{Api}/{id}/export/link", null)))).GetProperty("url").GetString()!;
        (await owner.Client.GetAsync(link)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.Client.GetAsync(link)).StatusCode.Should().Be(HttpStatusCode.NotFound, "link de uso único");
        var other = await UnknownAsync(owner.Client);
        link = (await JsonAsync(await owner.Client.SendAsync(Post($"{Api}/{id}/export/link", null)))).GetProperty("url").GetString()!;
        (await owner.Client.GetAsync(link.Replace(id.ToString("D"), other.GetProperty("id").GetGuid().ToString("D"), StringComparison.Ordinal))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await DbAsync(async db =>
        {
            (await db.PrivacyRequests.SingleAsync(r => r.Id == id)).DownloadTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync();
        });
        (await owner.Client.GetAsync(link)).StatusCode.Should().Be(HttpStatusCode.NotFound, "vence em 15 minutos");
        (await linked.Client.GetAsync(link)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "só o Proprietário baixa");
        var attachmentId = await DbAsync(db => db.PrivacyRequests.Where(r => r.Id == id).Select(r => r.ExportAttachmentId!.Value).SingleAsync());
        (await owner.Client.GetAsync($"/api/v1/attachments/{attachmentId}/content")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a exportação só sai pelo link de uso único");
        (await owner.Client.GetAsync($"/api/v1/attachments/{PrivacyRequest.OwnerType}/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await DbAsync(db => db.Attachments.SingleAsync(a => a.Id == attachmentId))).StoragePath.Should().StartWith(tenancy.TenantA.Id.ToString("N"), "prefixo do tenant no storage");
    }

    [Fact]
    public async Task Exportacao_do_terceiro_PF_traz_cadastro_e_emails_e_a_do_usuario_traz_grupos_e_acessos()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var email = "cliente" + Guid.NewGuid().ToString("N")[..6] + "@exemplo.com";
        var cpf = NewCpf();
        var partyId = await InTenantAsync(async sp =>
        {
            var db = sp.GetRequiredService<IPartiesDb>();
            var party = new Party { Kind = PartyKind.PF, LegalName = "Carlos Cliente Pereira", Document = cpf, Email = email, Phone = "11988887777", CreatedAt = DateTimeOffset.UtcNow };
            party.Addresses.Add(new PartyAddress { PartyId = party.Id, Kind = AddressKind.Billing, ZipCode = "01001000", Street = "Rua do Titular", Number = "10", District = "Centro", City = "São Paulo", State = "SP", IsDefault = true });
            party.Contacts.Add(new PartyContact { PartyId = party.Id, Name = "Joana Outra Pessoa", Email = "joana.outra@exemplo.com", Phone = "11977776666" });
            db.Parties.Add(party);
            await db.SaveChangesAsync();
            var shared = sp.GetRequiredService<BemAquiDbContext>();
            shared.EmailLogs.Add(new EmailLog { To = email, Template = "quote", Subject = "Seu orçamento ORC-TESTE", Model = "{\"segredo\":\"nao-sai\"}", Cc = "copia.outra@exemplo.com", Status = EmailStatus.Sent, CreatedAt = DateTimeOffset.UtcNow });
            await shared.SaveChangesAsync();
            return party.Id;
        });

        var matches = await JsonAsync(await owner.Client.GetAsync($"{Api}/subjects?kind=Party&q=carlos cliente"));
        matches.EnumerateArray().Should().Contain(m => m.GetProperty("id").GetGuid() == partyId);
        var request = await VerifyAsync(owner.Client, await CreateAsync(owner.Client, new { subjectType = "Party", subjectId = partyId, kind = "Portability", requestedOn = Today, channel = "Email" }));
        await ExportAsync(owner, request);
        var (json, _, _) = await DownloadAsync(owner.Client, request.GetProperty("id").GetGuid());
        json.Should().Contain("Carlos Cliente Pereira").And.Contain("Rua do Titular").And.Contain("Seu orçamento ORC-TESTE").And.Contain(email);
        json.Should().NotContain("joana.outra@exemplo.com", "contato é outra pessoa").And.NotContain("Joana Outra Pessoa").And.Contain("Joana P.");
        json.Should().NotContain("copia.outra@exemplo.com", "cópia de outra pessoa mascarada").And.NotContain("nao-sai", "o modelo do e-mail não sai");

        // Usuário do sistema (CA 9): nome, e-mail, grupos, acessos e notificações.
        var user = await ActorAsync("settings:view");
        await InTenantAsync(async sp =>
        {
            sp.GetRequiredService<BemAqui.BuildingBlocks.Notifications.INotificationService>().Notify(user.Id, "teste", "Aviso de teste do usuário", "/app");
            return await sp.GetRequiredService<BemAquiDbContext>().SaveChangesAsync();
        });
        var userRequest = await VerifyAsync(owner.Client, await CreateAsync(owner.Client, new { subjectType = "User", subjectId = user.Id, kind = "Access", requestedOn = Today, channel = "Email" }));
        await ExportAsync(owner, userRequest);
        var (userJson, _, _) = await DownloadAsync(owner.Client, userRequest.GetProperty("id").GetGuid());
        userJson.Should().Contain(user.Email).And.Contain("Privacidade ").And.Contain("Aviso de teste do usuário").And.Contain("Acessos registrados");
    }

    [Fact]
    public async Task Correcao_abre_o_cadastro_e_guarda_o_link()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var employee = await EmployeeAsync(owner.Client);
        var request = await CreateAsync(owner.Client, new { subjectType = "Employee", subjectId = employee.Id, kind = "Correction", requestedOn = Today, channel = "Phone" });
        var id = request.GetProperty("id").GetGuid();
        var early = await owner.Client.SendAsync(Post($"{Api}/{id}/correction", new { version = request.GetProperty("version").GetUInt32() }));
        (await CodeAsync(early)).Should().Be("subject_identity_not_verified");
        request = await VerifyAsync(owner.Client, request);
        var opened = await JsonAsync(await owner.Client.SendAsync(Post($"{Api}/{id}/correction", new { version = request.GetProperty("version").GetUInt32() })));
        opened.GetProperty("link").GetString().Should().Be($"/app/rh/funcionarios/{employee.Id}");
        opened.GetProperty("request").GetProperty("correctionLink").GetString().Should().Be($"/app/rh/funcionarios/{employee.Id}");
    }

    [Fact]
    public async Task Aviso_do_prazo_uma_vez_por_dia_por_dono_e_so_no_tenant_do_pedido()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var requestedOn = DateOnly.Parse(Today, System.Globalization.CultureInfo.InvariantCulture).AddDays(-13).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var request = await UnknownAsync(owner.Client, requestedOn: requestedOn);
        var id = request.GetProperty("id").GetGuid();
        request.GetProperty("dueChip").GetString().Should().Be("warning", "a 2 dias do prazo");

        var job = ActivatorUtilities.CreateInstance<PrivacyDueJob>(Factory.Services);
        await job.ExecuteAsync(tenancy.TenantA.Id, new PrivacyDueArgs(), CancellationToken.None);
        await job.ExecuteAsync(tenancy.TenantA.Id, new PrivacyDueArgs(), CancellationToken.None);
        await job.ExecuteAsync(tenancy.TenantB.Id, new PrivacyDueArgs(), CancellationToken.None);
        var owners = await InTenantAsync(sp => sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.IUserDirectory>().OwnerIdsAsync());
        var notices = await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().Notifications.Where(n => n.DedupKey != null && n.DedupKey.StartsWith($"privacy-due:{id:D}:")).ToListAsync());
        notices.Should().HaveCount(owners.Count, "uma por dono, mesmo rodando duas vezes");
        notices.Should().Contain(n => n.UserId == owner.Id && n.Title == $"Pedido {request.GetProperty("number").GetString()} do titular vence em {DateOnly.Parse(request.GetProperty("dueOn").GetString()!, System.Globalization.CultureInfo.InvariantCulture):dd/MM/yyyy}." && n.Link == PrivacyRequestService.Link(id));
        (await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().Notifications.CountAsync(n => n.DedupKey != null && n.DedupKey.StartsWith($"privacy-due:{id:D}:")), tenancy.TenantB.Id))
            .Should().Be(0, "o outro tenant não vê o pedido");
    }

    [Fact]
    public async Task Expurgos_da_exportacao_e_do_outbox_e_provedores_registrados()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var request = await VerifyAsync(owner.Client, await UnknownAsync(owner.Client));
        var id = request.GetProperty("id").GetGuid();
        var exported = await ExportAsync(owner, request);
        await owner.Client.SendAsync(Post($"{Api}/{id}/conclude", new { respondedOn = Today, response = "Arquivo entregue.", fileSent = true, version = exported.GetProperty("version").GetUInt32() }));
        var attachmentId = await DbAsync(db => db.PrivacyRequests.Where(r => r.Id == id).Select(r => r.ExportAttachmentId!.Value).SingleAsync());
        var purge = ActivatorUtilities.CreateInstance<PrivacyExportPurgeJob>(Factory.Services);
        await purge.ExecuteAsync(tenancy.TenantA.Id, new PrivacyExportPurgeArgs(), CancellationToken.None);
        (await DbAsync(db => db.Attachments.AnyAsync(a => a.Id == attachmentId))).Should().BeTrue("antes de 30 dias fica");
        await DbAsync(async db =>
        {
            (await db.PrivacyRequests.SingleAsync(r => r.Id == id)).ClosedAt = DateTimeOffset.UtcNow.AddDays(-31);
            return await db.SaveChangesAsync();
        });
        await purge.ExecuteAsync(tenancy.TenantA.Id, new PrivacyExportPurgeArgs(), CancellationToken.None);
        await purge.ExecuteAsync(tenancy.TenantA.Id, new PrivacyExportPurgeArgs(), CancellationToken.None);
        (await DbAsync(db => db.Attachments.AnyAsync(a => a.Id == attachmentId))).Should().BeFalse("apagada 30 dias depois da conclusão (CA 10)");
        (await DbAsync(db => db.PrivacyRequests.SingleAsync(r => r.Id == id))).ExportStatus.Should().Be(PrivacyExportStatus.Expired);

        // Outbox (ADR 0037 §2): entregue há mais de 30 dias sai; recente e na fila de mortas ficam.
        var (old, recent, dead) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await DbAsync(async db =>
        {
            var past = DateTimeOffset.UtcNow.AddDays(-40);
            db.OutboxMessages.Add(new OutboxMessage { Id = old, Type = "teste", Payload = "{}", OccurredAt = past, ProcessedAt = past });
            db.OutboxMessages.Add(new OutboxMessage { Id = recent, Type = "teste", Payload = "{}", OccurredAt = DateTimeOffset.UtcNow, ProcessedAt = DateTimeOffset.UtcNow });
            db.OutboxMessages.Add(new OutboxMessage { Id = dead, Type = "teste", Payload = "{}", OccurredAt = past, ProcessedAt = past, DeadLetteredAt = past });
            db.InboxMessages.Add(new InboxMessage { MessageId = old, Handler = "teste", ProcessedAt = past });
            return await db.SaveChangesAsync();
        });
        await ActivatorUtilities.CreateInstance<OutboxPurgeJob>(Factory.Services).ExecuteAsync(tenancy.TenantA.Id, new OutboxPurgeArgs(), CancellationToken.None);
        (await DbAsync(db => db.OutboxMessages.Where(m => m.Id == old || m.Id == recent || m.Id == dead).Select(m => m.Id).ToListAsync())).Should().BeEquivalentTo([recent, dead]);
        (await DbAsync(db => db.InboxMessages.AnyAsync(i => i.MessageId == old))).Should().BeFalse();

        // CA 6: todo provedor do código está registrado no DI.
        var registered = await InTenantAsync(sp => Task.FromResult(sp.GetServices<IPersonalDataProvider>().Select(p => p.GetType()).ToList()));
        var declared = typeof(IPersonalDataProvider).Assembly.GetTypes().Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name!.StartsWith("BemAqui.", StringComparison.Ordinal) && a.GetName().Name!.EndsWith(".Application", StringComparison.Ordinal)).SelectMany(a => a.GetTypes()))
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPersonalDataProvider).IsAssignableFrom(t)).Distinct().ToList();
        registered.Select(t => t.FullName).Should().BeEquivalentTo(declared.Select(t => t.FullName));
        registered.Select(t => t.Name).Should().Contain(["PartiesPersonalDataProvider", "HrPersonalDataProvider", "IdentityPersonalDataProvider", "FinancePersonalDataProvider"]);
    }
}
