using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>EXE-01a: projeto de serviço (tipo, início, entrega, aceite do cliente, desfazer, conclusão e o ajuste automático do tipo).</summary>
public partial class QuoteTests
{
    private const string AcceptanceName = "Joana Prado Albuquerque";

    /// <summary>Liga ou desliga o perfil Serviços no tenant A (o tenant de teste nasce só com <c>factory</c>, como a Konebras).</summary>
    private async Task ServicesAsync(bool on)
    {
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<BemAqui.Platform.Application.Modules.TenantModuleService>();
        var result = on
            ? await service.ApplyProfileAsync(tenancy.TenantA.Id, BusinessProfileCatalog.Services, null, null)
            : await service.RemoveProfileAsync(tenancy.TenantA.Id, BusinessProfileCatalog.Services, null, null);
        result.IsSuccess.Should().BeTrue();
        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>Orçamento enviado só com item de serviço e os marcos pedidos.</summary>
    private static async Task<(Guid Id, JsonElement Quote)> ServiceQuoteAsync(HttpClient owner, object[] milestones, string itemKind = "Service")
    {
        var customer = await CustomerAsync(owner);
        var quote = await JsonAsync(await owner.SendAsync(Post(Base, new { partyId = customer, contactId = (Guid?)null, siteAddressId = (Guid?)null })));
        var id = quote.GetProperty("id").GetGuid();
        var added = await owner.PostAsJsonAsync($"{Base}/{id}/items",
            new { kind = itemKind, itemId = (Guid?)null, description = "Implantação do sistema", quantity = 1m, unitPrice = (decimal?)10_000m, resetPrice = false, version = Version(quote) });
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        quote = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        var conditions = await owner.PutAsJsonAsync($"{Base}/{id}/conditions", new { milestones, validUntil = Iso(Today.AddDays(15)), version = Version(quote) });
        conditions.StatusCode.Should().Be(HttpStatusCode.OK, await conditions.Content.ReadAsStringAsync());
        quote = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/send", new { version = Version(quote) }));
        return (id, quote);
    }

    private static readonly object[] ServiceMilestones =
        [Milestone("Sinal", 30m), Milestone("Entrega", 30m, triggerEvent: "Delivered", days: 5), Milestone("Aceite", 40m, triggerEvent: "TechnicalAcceptance", days: 10)];

    private static async Task<JsonElement> ProjectAsync(HttpClient client, Guid id) => await JsonAsync(await client.GetAsync($"/api/v1/projects/{id}"));

    private static object Start(JsonElement project, DateOnly date, string? reason = null) =>
        new { date = Iso(date), version = Version(project), expectedStatus = project.GetProperty("status").GetString(), backdateReason = reason };

    private static object Accept(JsonElement project, DateOnly date, string name = AcceptanceName, Guid? attachmentId = null) =>
        new { date = Iso(date), version = Version(project), acceptedByName = name, acceptedByRole = "Diretora", expectedStatus = "Delivered", attachmentId };

    [Fact]
    public async Task Servico_inicia_entrega_aceita_desfaz_e_reaceita_liberando_so_a_parcela_certa()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        try
        {
            // CA 27: no orçamento só de serviço, marco de fabricação é recusado no campo do gatilho.
            var (bad, badQuote) = await ServiceQuoteAsync(owner, ServiceMilestones);
            var draft = await JsonAsync(await owner.SendAsync(Post(Base, new { partyId = badQuote.GetProperty("partyId").GetGuid(), contactId = (Guid?)null, siteAddressId = (Guid?)null })));
            var draftId = draft.GetProperty("id").GetGuid();
            draft = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{draftId}/items",
                new { kind = "Service", itemId = (Guid?)null, description = "Consultoria", quantity = 1m, unitPrice = (decimal?)100m, resetPrice = false, version = Version(draft) }));
            var refused = await owner.PutAsJsonAsync($"{Base}/{draftId}/conditions",
                new { milestones = new[] { Milestone("Sinal", 50m), Milestone("Fabricação", 50m, triggerEvent: "FabricationDone") }, validUntil = Iso(Today.AddDays(15)), version = Version(draft) });
            refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var problem = await JsonAsync(refused);
            Code(problem).Should().Be("milestone_trigger_not_allowed");
            problem.GetProperty("errors").TryGetProperty("milestones[1].triggerEvent", out _).Should().BeTrue();

            // CA 2 e 3: aprovado, o projeto nasce Service (Approval) e o snapshot leva o tipo.
            var (contractId, projectId) = await ApproveAsync(owner, bad, badQuote, Today);
            var row = await WithDbAsync(db => db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId));
            row.ExecutionKind.Should().Be(ExecutionKind.Service);
            row.ExecutionKindSource.Should().Be(ExecutionKindSource.Approval);
            (await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"))).GetProperty("snapshot").GetProperty("executionKind").GetString().Should().Be("Service");
            var project = await ProjectAsync(owner, projectId);
            project.GetProperty("executionKind").GetString().Should().Be("Service");
            project.GetProperty("stage").GetProperty("letters").EnumerateArray().Select(l => l.GetString()).Should().Equal("P", "I", "E", "A", "C");
            project.GetProperty("acceptanceRequired").GetBoolean().Should().BeTrue();

            // CA 23: sem Idempotency-Key, 422; duplo clique com chaves diferentes = uma gravação e um 409.
            (await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/start", Start(project, Today))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var both = await Task.WhenAll(
                owner.SendAsync(Post($"/api/v1/projects/{projectId}/start", Start(project, Today))),
                owner.SendAsync(Post($"/api/v1/projects/{projectId}/start", Start(project, Today.AddDays(-1), "Começou ontem"))));
            both.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
            (await OutboxCountAsync("ProjectStarted", projectId.ToString())).Should().Be(1);
            project = await ProjectAsync(owner, projectId);
            project.GetProperty("status").GetString().Should().Be("InProgress");
            var started = DateOnly.Parse(project.GetProperty("startedOn").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

            // CA 24: data futura, 422; CA 11: a entrega não libera o aceite; vai a Delivered e libera só a parcela de entrega.
            var future = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/delivery", new { date = Iso(Today.AddDays(1)), version = Version(project), expectedStatus = "InProgress" }));
            future.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await future.Content.ReadAsStringAsync()).Should().Contain("A data não pode ser depois de hoje.");
            var deliveredOn = started;
            var delivered = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/delivery",
                new { date = Iso(deliveredOn), version = Version(project), expectedStatus = "InProgress", backdateReason = deliveredOn < Today ? "Entregue ontem" : null }));
            delivered.StatusCode.Should().Be(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync());
            project = await JsonAsync(delivered);
            project.GetProperty("status").GetString().Should().Be("Delivered");
            await DrainAsync();
            var parcels = await ParcelsAsync(contractId);
            parcels.Single(p => p.Description.Contains("Entrega")).Status.Should().Be(ReceivableStatus.Open);
            var aceite = parcels.Single(p => p.Description.Contains("Aceite"));
            aceite.Status.Should().Be(ReceivableStatus.Planned, "a entrega não é o aceite do cliente");
            (await OutboxCountAsync("ProjectFabricated", projectId.ToString())).Should().Be(0, "sem marco de fabricação");

            // CA 25: anexo que não é do aceite deste projeto, 422 no campo; projeto inexistente, 404. CA 13: aceite antes da entrega, 422.
            var idor = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance", Accept(project, Today, attachmentId: Guid.NewGuid())));
            idor.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await JsonAsync(idor)).GetProperty("errors").TryGetProperty("attachmentId", out _).Should().BeTrue();
            (await owner.SendAsync(Post($"/api/v1/projects/{Guid.NewGuid()}/acceptance", Accept(project, Today)))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance", Accept(project, deliveredOn.AddDays(-1))))).StatusCode
                .Should().Be(HttpStatusCode.UnprocessableEntity);

            // CA 14: segregação ligada, quem registrou a entrega não registra o aceite.
            await WithDbAsync(async db =>
            {
                db.CommercialParameters.Add(new CommercialParameter { Key = CommercialParametersService.AcceptanceSegregation, Value = 1m, UpdatedAt = DateTimeOffset.UtcNow });
                return await db.SaveChangesAsync();
            });
            var segregated = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance", Accept(project, Today)));
            segregated.StatusCode.Should().Be(HttpStatusCode.Conflict);
            Code(await JsonAsync(segregated)).Should().Be("acceptance_segregation");
            await WithDbAsync(db => db.CommercialParameters.Where(p => p.Key == CommercialParametersService.AcceptanceSegregation).ExecuteDeleteAsync());

            // CA 13 e 18: aceite versão 1 libera a parcela de aceite; o mesmo aceite de novo devolve 200; outro, 409.
            var accepted = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance", Accept(project, Today)));
            accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
            project = await JsonAsync(accepted);
            project.GetProperty("acceptance").GetProperty("acceptedByName").GetString().Should().Be(AcceptanceName);
            project.GetProperty("stage").GetProperty("letter").GetString().Should().Be("A");
            (await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance", Accept(project, Today)))).StatusCode.Should().Be(HttpStatusCode.OK);
            var other = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance", Accept(project, Today, "Outra Pessoa Silva")));
            other.StatusCode.Should().Be(HttpStatusCode.Conflict);
            Code(await JsonAsync(other)).Should().Be("already_accepted");
            await DrainAsync();
            aceite = (await ParcelsAsync(contractId)).Single(p => p.Id == aceite.Id);
            aceite.Status.Should().Be(ReceivableStatus.Open);
            aceite.DueDate.Should().Be(Today.AddDays(10));

            // LGPD: o nome fica fora do evento e mascarado na auditoria.
            (await OutboxCountAsync(string.Empty, AcceptanceName)).Should().Be(0);
            (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == "ProjectAcceptance"))).Should().BeGreaterThan(0);
            (await WithDbAsync(db => db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM audit.audit_log WHERE entity = 'ProjectAcceptance' AND changes::text LIKE {"%" + AcceptanceName + "%"}").SingleAsync()))
                .Should().Be(0, "nome de quem aceitou é mascarado na auditoria");

            // CA 15 e 18: desfazer sem motivo, 422; com motivo, a parcela de aceite volta a prevista.
            (await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance/revert", new { version = Version(project), reason = "", expectedStatus = "Delivered" })))
                .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var reverted = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance/revert", new { version = Version(project), reason = "Aceite registrado no projeto errado", expectedStatus = "Delivered" }));
            reverted.StatusCode.Should().Be(HttpStatusCode.OK, await reverted.Content.ReadAsStringAsync());
            project = await JsonAsync(reverted);
            await DrainAsync();
            aceite = (await ParcelsAsync(contractId)).Single(p => p.Id == aceite.Id);
            aceite.Status.Should().Be(ReceivableStatus.Planned);
            aceite.DueDate.Should().BeNull();
            (await WithDbAsync(db => db.ProjectTriggerEvents.AsNoTracking().SingleAsync(t => t.ProjectId == projectId && t.Event == TriggerEvent.TechnicalAcceptance)))
                .Should().BeEquivalentTo(new { Version = 1, Reverted = true });

            // CA 17: reaceite em sequência, versão 2; evento velho (versão 1) reprocessado é descartado.
            var again = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance", Accept(project, Today)));
            again.StatusCode.Should().Be(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
            (await WithDbAsync(db => db.ProjectAcceptances.AsNoTracking().Where(a => a.ProjectId == projectId).OrderBy(a => a.Version).Select(a => a.Version).ToListAsync()))
                .Should().Equal(1, 2);
            await DrainAsync();
            (await ParcelsAsync(contractId)).Single(p => p.Id == aceite.Id).Status.Should().Be(ReceivableStatus.Open);
            (await WithDbAsync(db => db.ProjectTriggerEvents.AsNoTracking().SingleAsync(t => t.ProjectId == projectId && t.Event == TriggerEvent.TechnicalAcceptance)))
                .Should().BeEquivalentTo(new { Version = 2, Reverted = false });

            // CA 20: quitadas as três, o contrato conclui (o aceite vigente existe); CA 16: encerrado não desfaz.
            var bank = await SettleBankAsync(owner);
            foreach (var parcel in await ParcelsAsync(contractId))
            {
                await SettleAsync(owner, bank, parcel.Id);
            }

            await DrainAsync();
            (await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"))).GetProperty("status").GetString().Should().Be("Completed");
            project = await ProjectAsync(owner, projectId);
            var closed = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance/revert", new { version = Version(project), reason = "Tarde demais", expectedStatus = "Closed" }));
            closed.StatusCode.Should().Be(HttpStatusCode.Conflict);
            Code(await JsonAsync(closed)).Should().Be("project_closed");
        }
        finally
        {
            await ServicesAsync(false);
        }
    }

    [Fact]
    public async Task Desfazer_aceite_com_parcela_recebida_e_bloqueado_e_a_corrida_notifica_o_financeiro()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        try
        {
            var (id, quote) = await ServiceQuoteAsync(owner, ServiceMilestones);
            var (contractId, projectId) = await ApproveAsync(owner, id, quote, Today);
            var project = await ProjectAsync(owner, projectId);

            // CA 11: entrega direto de Planned com "também aceitou": o início fica na data da entrega e o aceite nasce FromDelivery.
            var delivered = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/delivery",
                new { date = Iso(Today), version = Version(project), expectedStatus = "Planned", alsoAccept = true, acceptedByName = AcceptanceName }));
            delivered.StatusCode.Should().Be(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync());
            project = await JsonAsync(delivered);
            project.GetProperty("startedOn").GetString().Should().Be(Iso(Today));
            (await WithDbAsync(db => db.ProjectAcceptances.AsNoTracking().SingleAsync(a => a.ProjectId == projectId))).Kind.Should().Be(AcceptanceKind.FromDelivery);
            await DrainAsync();
            var aceite = (await ParcelsAsync(contractId)).Single(p => p.Description.Contains("Aceite"));
            aceite.Status.Should().Be(ReceivableStatus.Open);

            // Corrida (CA 18): o Commercial deixa desfazer, mas a parcela é recebida antes do consumidor do Finance: não volta e avisa finance.edit.
            var reverted = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance/revert", new { version = Version(project), reason = "Cliente pediu revisão", expectedStatus = "Delivered" }));
            reverted.StatusCode.Should().Be(HttpStatusCode.OK, await reverted.Content.ReadAsStringAsync());
            var bank = await SettleBankAsync(owner);
            await SettleAsync(owner, bank, aceite.Id);
            await DrainAsync();
            (await ParcelsAsync(contractId)).Single(p => p.Id == aceite.Id).Status.Should().Be(ReceivableStatus.Paid);
            (await WithDbAsync(db => db.ProjectTriggerEvents.AsNoTracking().SingleAsync(t => t.ProjectId == projectId && t.Event == TriggerEvent.TechnicalAcceptance)))
                .Should().BeEquivalentTo(new { Reverted = false, RevertNote = BemAqui.Finance.Application.AcceptanceRevertedConsumer.BlockedNote });
            (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == ownerId && n.Type == BemAqui.Finance.Application.AcceptanceRevertedConsumer.NotificationType))).Should().BeGreaterThan(0);

            // CA 15: com a parcela de aceite recebida, o Commercial recusa o desfazer do novo aceite.
            project = await ProjectAsync(owner, projectId);
            var again = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance", Accept(project, Today)));
            again.StatusCode.Should().Be(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
            project = await JsonAsync(again);
            var locked = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance/revert", new { version = Version(project), reason = "De novo", expectedStatus = "Delivered" }));
            locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await JsonAsync(locked);
            Code(body).Should().Be("acceptance_locked");
            body.GetProperty("detail").GetString().Should().Be(ProjectExecutionService.AcceptanceLockedReceivable);
        }
        finally
        {
            await ServicesAsync(false);
        }
    }

    [Fact]
    public async Task Entrega_do_servico_com_marco_antigo_de_fabricacao_libera_uma_vez_e_fabrica_nao_ve_as_rotas()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);

        // Contrato antigo (aprovado como fabricação, com marco "fabricação concluída") que o ajuste automático tornou serviço.
        var (id, quote) = await ServiceQuoteAsync(owner,
            [Milestone("Sinal", 30m), Milestone("Fabricação", 30m, triggerEvent: "FabricationDone"), Milestone("Aceite", 40m, triggerEvent: "TechnicalAcceptance")]);
        var (contractId, projectId) = await ApproveAsync(owner, id, quote, Today);
        (await WithDbAsync(db => db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId))).ExecutionKind.Should().Be(ExecutionKind.Fabrication, "fábrica não tem o recurso");

        // CA 22: sem o recurso, as rotas novas respondem 403 module_not_released; o detalhe não traz início nem aceite.
        var project = await ProjectAsync(owner, projectId);
        var denied = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/start", Start(project, Today)));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(denied)).Should().Be("module_not_released");
        project.GetProperty("serviceExecution").GetBoolean().Should().BeFalse();

        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET execution_kind = 'Service', execution_kind_source = 'Backfill' WHERE id = {projectId}"));
        project = await ProjectAsync(owner, projectId);
        (await owner.SendAsync(Post($"/api/v1/projects/{projectId}/delivery", new { date = Iso(Today), version = Version(project), expectedStatus = "Planned" })))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "CA 4: tenant sem o perfil Serviços não age no projeto de serviço");

        await ServicesAsync(true);
        try
        {
            // CA 12: a entrega publica antes o ProjectFabricated de compatibilidade e grava a data; a parcela de fabricação é liberada.
            var delivered = await owner.SendAsync(Post($"/api/v1/projects/{projectId}/delivery", new { date = Iso(Today), version = Version(project), expectedStatus = "Planned" }));
            delivered.StatusCode.Should().Be(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync());
            await DrainAsync();
            (await WithDbAsync(db => db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId))).FabricationCompatOn.Should().Be(Today);
            (await ParcelsAsync(contractId)).Single(p => p.Description.Contains("Fabricação")).Status.Should().Be(ReceivableStatus.Open);
            (await OutboxCountAsync("ProjectFabricated", projectId.ToString())).Should().Be(1);

        }
        finally
        {
            await ServicesAsync(false);
        }
    }

    [Fact]
    public async Task Ajuste_automatico_converte_so_o_que_passa_sem_evento_sem_titulo_e_uma_vez()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        try
        {
            // Projeto de serviço entregue antes da migration (Default) e um de equipamento (fabricação).
            var (sid, squote) = await ServiceQuoteAsync(owner, [Milestone("Sinal", 50m), Milestone("Aceite", 50m, triggerEvent: "TechnicalAcceptance")]);
            var (_, serviceProject) = await ApproveAsync(owner, sid, squote, Today);
            var (fid, fquote) = await ServiceQuoteAsync(owner, [Milestone("Sinal", 100m)], itemKind: "Product");
            var (_, productProject) = await ApproveAsync(owner, fid, fquote, Today);
            var deliveredOn = Today.AddDays(-3);
            await WithDbAsync(db => db.Database.ExecuteSqlAsync(
                $"UPDATE commercial.projects SET execution_kind = 'Fabrication', execution_kind_source = 'Default', status = 'Delivered', delivered_on = {deliveredOn}, accepted_on = NULL WHERE id = {serviceProject}"));
            await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET execution_kind_source = 'Default' WHERE id = {productProject}"));
            await WithDbAsync(db => db.CommercialParameters.Where(p => p.Key == CommercialParametersService.ExecutionKindBackfillDone).ExecuteDeleteAsync());
            await DrainAsync();

            var outboxBefore = await WithDbAsync(db => db.OutboxMessages.CountAsync());
            var titlesBefore = await WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT id::text || ':' || xmin::text AS \"Value\" FROM finance.receivables ORDER BY id").ToListAsync());
            var triggersBefore = await WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT id::text || ':' || xmin::text AS \"Value\" FROM finance.project_trigger_events ORDER BY id").ToListAsync());

            (await RunBackfillAsync()).Should().BeGreaterThan(0);

            var service = await WithDbAsync(db => db.Projects.AsNoTracking().SingleAsync(p => p.Id == serviceProject));
            service.Should().BeEquivalentTo(new { ExecutionKind = ExecutionKind.Service, ExecutionKindSource = ExecutionKindSource.Backfill, AcceptedOn = (DateOnly?)deliveredOn, DeliveredOn = (DateOnly?)deliveredOn });
            (await WithDbAsync(db => db.ProjectAcceptances.AsNoTracking().SingleAsync(a => a.ProjectId == serviceProject)))
                .Should().BeEquivalentTo(new { Kind = AcceptanceKind.Inherited, Version = 1, OccurredOn = deliveredOn, AcceptedByName = (string?)null });
            (await WithDbAsync(db => db.Projects.AsNoTracking().SingleAsync(p => p.Id == productProject)))
                .Should().BeEquivalentTo(new { ExecutionKind = ExecutionKind.Fabrication, ExecutionKindSource = ExecutionKindSource.Backfill });
            (await WithDbAsync(db => db.Projects.CountAsync(p => p.ExecutionKindSource == ExecutionKindSource.Default))).Should().Be(0);

            // CA 7: nenhum evento, nenhum título nem gatilho alterado, auditoria com a origem; a segunda execução não muda nada.
            (await WithDbAsync(db => db.OutboxMessages.CountAsync())).Should().Be(outboxBefore);
            (await WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT id::text || ':' || xmin::text AS \"Value\" FROM finance.receivables ORDER BY id").ToListAsync())).Should().Equal(titlesBefore);
            (await WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT id::text || ':' || xmin::text AS \"Value\" FROM finance.project_trigger_events ORDER BY id").ToListAsync())).Should().Equal(triggersBefore);
            (await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == "Project" && a.EntityId == serviceProject.ToString()).ToListAsync()))
                .Should().Contain(a => a.Origin == ExecutionKindBackfill.AuditLabel && a.UserId == null);
            var xminAfter = await WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT id::text || ':' || xmin::text AS \"Value\" FROM commercial.projects ORDER BY id").ToListAsync());
            (await RunBackfillAsync()).Should().Be(0);
            (await WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT id::text || ':' || xmin::text AS \"Value\" FROM commercial.projects ORDER BY id").ToListAsync())).Should().Equal(xminAfter);
        }
        finally
        {
            await ServicesAsync(false);
        }

        // CA 8: sem o recurso (Konebras), todos ficam Fabrication + Backfill e nada mais muda.
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET execution_kind_source = 'Default' WHERE execution_kind = 'Fabrication'"));
        await WithDbAsync(db => db.CommercialParameters.Where(p => p.Key == CommercialParametersService.ExecutionKindBackfillDone).ExecuteDeleteAsync());
        var before = await WithDbAsync(db => db.Projects.AsNoTracking().Where(p => p.ExecutionKind == ExecutionKind.Fabrication).OrderBy(p => p.Id).ToListAsync());
        await RunBackfillAsync();
        var after = await WithDbAsync(db => db.Projects.AsNoTracking().Where(p => p.ExecutionKind == ExecutionKind.Fabrication).OrderBy(p => p.Id).ToListAsync());
        after.Should().BeEquivalentTo(before, o => o.Excluding(p => p.ExecutionKindSource).Excluding(p => p.Version));
        after.Should().OnlyContain(p => p.ExecutionKindSource == ExecutionKindSource.Backfill);
    }

    private Task<int> OutboxCountAsync(string typeSuffix, string payloadPart) =>
        WithDbAsync(db => db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM shared.outbox_messages WHERE type LIKE {"%" + typeSuffix} AND payload::text LIKE {"%" + payloadPart + "%"}").SingleAsync());

    private async Task<int> RunBackfillAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await scope.ServiceProvider.GetRequiredService<ExecutionKindBackfill>().RunAsync();
    }
}
