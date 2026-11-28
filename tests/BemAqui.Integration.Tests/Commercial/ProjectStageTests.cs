using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Application.Documents;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Commercial.Endpoints;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>
/// EXE-03a: etapas do modelo no projeto de serviço (cópia na aprovação, "Usar um modelo", edição antes de começar, leitura, API v1 e PDF). O tenant
/// nasce só com <c>factory</c> (como a Konebras); cada teste liga o perfil Serviços quando precisa. Banco desta classe.
/// </summary>
public partial class ProjectStageTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Quotes = "/api/v1/quotes";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static uint Version(JsonElement e) => e.GetProperty("version").GetUInt32();

    private static object Milestone(string description, decimal percent, string? triggerEvent = "Signature", int days = 0) =>
        new { description, percent, trigger = "Event", triggerEvent, dueDate = (string?)null, daysAfterEvent = days };

    private static readonly object[] ServiceMilestones =
        [Milestone("Sinal", 30m), Milestone("Entrega", 30m, "Delivered", 5), Milestone("Aceite", 40m, "TechnicalAcceptance", 10)];

    [Fact]
    public async Task Aprovacao_copia_o_modelo_do_item_congela_no_snapshot_e_mostra_a_etapa_atual()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        try
        {
            var item = await ServiceItemAsync();
            var (templateId, templateName) = await TemplateAsync(item);
            var (quoteId, quote) = await QuoteAsync(owner, "Service", item, 3m);
            var (contractId, projectId) = await ApproveAsync(owner, quoteId, quote);

            // CA 2: etapas copiadas (a primeira Ready), horas = horas por unidade × quantidade do item, checklist em linhas; modelo gravado no projeto.
            var stages = await WithDbAsync(db => db.ProjectServiceStages.AsNoTracking().Where(s => s.ProjectId == projectId).OrderBy(s => s.Sequence).ToListAsync());
            stages.Select(s => s.Code).Should().Equal("K", "T", "G", "A");
            stages.Select(s => s.Status).Should().Equal(ProjectStageStatus.Ready, ProjectStageStatus.Waiting, ProjectStageStatus.Waiting, ProjectStageStatus.Waiting);
            stages.Select(s => s.PlannedHours).Should().Equal(6m, 4.5m, 1.5m, 0m);
            var trainingId = stages[1].Id;
            (await WithDbAsync(db => db.ProjectServiceStageChecks.CountAsync(c => c.StageId == trainingId))).Should().Be(2);
            var row = await WithDbAsync(db => db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId));
            row.StageTemplateId.Should().Be(templateId);
            row.StageTemplateRevision.Should().Be(1);

            // Snapshot com as etapas congeladas (código, nome, papel).
            var snapshot = (await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"))).GetProperty("snapshot");
            snapshot.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("code").GetString() + ":" + s.GetProperty("role").GetString())
                .Should().Equal("K:Execution", "T:Execution", "G:Delivery", "A:Acceptance");

            // CA 8 e 9: currentStage no detalhe; CA 4: histórico; EXE-02 CA 6: "Usado em 1 projeto".
            var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"));
            var current = project.GetProperty("currentStage");
            (current.GetProperty("code").GetString(), current.GetProperty("sequence").GetInt32(), current.GetProperty("total").GetInt32()).Should().Be(("K", 1, 4));
            project.GetProperty("stage").GetProperty("letters").EnumerateArray().Select(l => l.GetString()).Should().Equal("P", "I", "E", "A", "C");
            var timeline = await (await owner.GetAsync($"/api/v1/projects/{projectId}/timeline")).Content.ReadAsStringAsync();
            timeline.Should().Contain($"Etapas do modelo {templateName} (revisão 1)");
            (await JsonAsync(await owner.GetAsync($"/api/v1/stage-templates/{templateId}"))).GetProperty("usageCount").GetInt32().Should().Be(1);

            // CA 7: leitura com o progresso do checklist.
            var view = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}/stages"));
            view.GetProperty("started").GetBoolean().Should().BeFalse();
            view.GetProperty("stages")[1].GetProperty("checklistTotal").GetInt32().Should().Be(2);
            view.GetProperty("stages")[1].GetProperty("checklistDone").GetInt32().Should().Be(0);

            // CA 9: API v1 e PDF com currentStage, sem o responsável.
            await using (var scope = await TenantScopeAsync())
            {
                var partyId = project.GetProperty("partyId").GetGuid();
                var summary = (await scope.ServiceProvider.GetRequiredService<IProjects>().ListByPartyAsync(partyId)).Single();
                var status = IntegrationProjectEndpoints.Map(summary, Today);
                status.CurrentStage.Should().Be(new IntegrationCurrentStage("K", stages[0].Name, 1, 4));
                typeof(IntegrationProjectStatus).GetProperties().Concat(typeof(IntegrationCurrentStage).GetProperties())
                    .Should().NotContain(p => p.Name.Contains("Responsible", StringComparison.Ordinal));
                var detail = (await scope.ServiceProvider.GetRequiredService<ProjectService>().GetAsync(projectId)).Value;
                var pdf = ProjectSummaryPdf.Build(detail, null, null, null, null, Today);
                pdf.CurrentStage.Should().Be(new ProjectCurrentStage("K", stages[0].Name, 1, 4));
                ProjectSummaryPdf.CurrentStageText(pdf.CurrentStage!).Should().Be($"Etapa atual: K {stages[0].Name} (1 de 4)");
            }

            (await owner.GetAsync($"/api/v1/projects/{projectId}/pdf")).StatusCode.Should().Be(HttpStatusCode.OK);

            // CA 5: edição antes de começar — reordenar, renomear e responsável; versão antiga, 409; linha inválida, 422 na linha; etapa de outro projeto, 404.
            var lines = Lines(view);
            (lines[0], lines[1]) = (lines[1], lines[0]);
            lines[0]["name"] = "Treinamento da equipe";
            lines[0]["responsibleUserId"] = ownerId;
            var saved = await owner.PutAsJsonAsync($"/api/v1/projects/{projectId}/stages", new { stages = lines, version = Version(view) });
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            var after = await JsonAsync(saved);
            after.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("code").GetString()).Should().Equal("T", "K", "G", "A");
            after.GetProperty("stages")[0].GetProperty("status").GetString().Should().Be("Ready");
            after.GetProperty("stages")[0].GetProperty("responsibleName").GetString().Should().NotBeNullOrEmpty();
            (await owner.PutAsJsonAsync($"/api/v1/projects/{projectId}/stages", new { stages = lines, version = Version(view) })).StatusCode.Should().Be(HttpStatusCode.Conflict);

            var invalid = Lines(after);
            invalid[1]["role"] = "Delivery";
            invalid[0]["responsibleUserId"] = Guid.NewGuid();
            var refused = await owner.PutAsJsonAsync($"/api/v1/projects/{projectId}/stages", new { stages = invalid, version = Version(after) });
            refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var errors = (await JsonAsync(refused)).GetProperty("errors");
            errors.TryGetProperty("stages[0].responsibleUserId", out _).Should().BeTrue(errors.ToString());
            errors.EnumerateObject().Should().Contain(e => e.Name.StartsWith("stages[", StringComparison.Ordinal) && e.Name.EndsWith(".role", StringComparison.Ordinal));

            var foreign = Lines(after);
            foreign[0]["id"] = Guid.NewGuid();
            (await owner.PutAsJsonAsync($"/api/v1/projects/{projectId}/stages", new { stages = foreign, version = Version(after) })).StatusCode.Should().Be(HttpStatusCode.NotFound);

            // Depois da primeira etapa iniciada (EXE-03b; aqui direto no banco): só responsável e datas; o resto, 409; trocar o modelo, 409.
            await WithDbAsync(db => db.ProjectServiceStages.Where(s => s.ProjectId == projectId && s.Sequence == 1)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, ProjectStageStatus.InProgress).SetProperty(s => s.StartedAt, DateTimeOffset.UtcNow)));
            var started = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}/stages"));
            started.GetProperty("started").GetBoolean().Should().BeTrue();
            started.GetProperty("canApplyTemplate").GetBoolean().Should().BeFalse();
            var renamed = Lines(started);
            renamed[1]["name"] = "Outro nome";
            var locked = await owner.PutAsJsonAsync($"/api/v1/projects/{projectId}/stages", new { stages = renamed, version = Version(started) });
            locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await locked.Content.ReadAsStringAsync()).Should().Contain("O projeto já começou: as etapas não mudam.");
            var dates = Lines(started);
            dates[1]["plannedStart"] = Iso(Today);
            dates[1]["plannedEnd"] = Iso(Today.AddDays(3));
            (await owner.PutAsJsonAsync($"/api/v1/projects/{projectId}/stages", new { stages = dates, version = Version(started) })).StatusCode.Should().Be(HttpStatusCode.OK);
            var reloaded = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}/stages"));
            reloaded.GetProperty("stages")[1].GetProperty("plannedEnd").GetString().Should().Be(Iso(Today.AddDays(3)));
            (await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/stages/apply-template", new { templateId, version = Version(reloaded) })).StatusCode
                .Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            await ServicesAsync(false);
        }
    }

    [Fact]
    public async Task Usar_modelo_em_planejado_em_execucao_e_entregue_sem_evento_nem_titulo_e_recusa_encerrado()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        try
        {
            var (templateId, _) = await TemplateAsync(null);
            var (otherId, _) = await TemplateAsync(null);

            // Planejado sem modelo sugerido: nasce sem etapas (EXE-01a); vazio com "Usar um modelo"; horas × 1 (sem item ligado).
            var planned = await ServiceProjectAsync(owner);
            var empty = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{planned}/stages"));
            empty.GetProperty("stages").GetArrayLength().Should().Be(0);
            empty.GetProperty("canApplyTemplate").GetBoolean().Should().BeTrue();
            empty.GetProperty("templates").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).Should().Contain(templateId);
            var applied = await JsonAsync(await ApplyAsync(owner, planned, templateId, Version(empty)));
            applied.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("plannedHours").GetDecimal()).Should().Equal(2m, 1.5m, 0.5m, 0m);
            applied.GetProperty("stages")[0].GetProperty("status").GetString().Should().Be("Ready");
            // CA 5: trocar o modelo antes de começar.
            var swapped = await JsonAsync(await ApplyAsync(owner, planned, otherId, Version(applied)));
            swapped.GetProperty("templateId").GetGuid().Should().Be(otherId);
            swapped.GetProperty("stages").GetArrayLength().Should().Be(4);
            (await WithDbAsync(db => db.ProjectServiceStages.CountAsync(s => s.ProjectId == planned))).Should().Be(4);

            // Em execução: a primeira Ready.
            var running = await ServiceProjectAsync(owner);
            var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{running}"));
            (await owner.SendAsync(Post($"/api/v1/projects/{running}/start", new { date = Iso(Today), version = Version(project), expectedStatus = "Planned" }))).StatusCode.Should().Be(HttpStatusCode.OK);
            var runningView = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{running}/stages"));
            (await JsonAsync(await ApplyAsync(owner, running, templateId, Version(runningView)))).GetProperty("stages")[0].GetProperty("status").GetString().Should().Be("Ready");

            // Entregue: Execução e Entrega concluídas na data da entrega, Aceite Ready; nenhum evento e nenhum título mudam.
            var delivered = await ServiceProjectAsync(owner);
            project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{delivered}"));
            var deliveredResponse = await owner.SendAsync(Post($"/api/v1/projects/{delivered}/delivery", new { date = Iso(Today), version = Version(project), expectedStatus = "Planned" }));
            deliveredResponse.StatusCode.Should().Be(HttpStatusCode.OK, await deliveredResponse.Content.ReadAsStringAsync());
            await DrainAsync();
            var contractId = (await JsonAsync(deliveredResponse)).GetProperty("contractId").GetGuid();
            var outboxBefore = await WithDbAsync(db => db.OutboxMessages.CountAsync());
            var receivablesBefore = await ReceivablesAsync(contractId);
            var deliveredView = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{delivered}/stages"));
            var deliveredStages = await JsonAsync(await ApplyAsync(owner, delivered, templateId, Version(deliveredView)));
            deliveredStages.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("status").GetString()).Should().Equal("Done", "Done", "Done", "Ready");
            deliveredStages.GetProperty("stages")[0].GetProperty("completedOn").GetString().Should().Be(Iso(Today));
            (await WithDbAsync(db => db.OutboxMessages.CountAsync())).Should().Be(outboxBefore, "usar o modelo não publica evento");
            (await ReceivablesAsync(contractId)).Should().Equal(receivablesBefore, "nenhum título muda");

            // IDOR (CA 6): etapa de outro projeto na rota deste, 404.
            var foreign = Lines(deliveredStages);
            (await owner.PutAsJsonAsync($"/api/v1/projects/{planned}/stages", new { stages = foreign, version = Version(swapped) })).StatusCode.Should().Be(HttpStatusCode.NotFound);

            // CA 3: encerrado ou cancelado, 409 com a mensagem.
            await WithDbAsync(db => db.Projects.Where(p => p.Id == running).ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, ProjectStatus.Closed)));
            var closedView = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{running}/stages"));
            closedView.GetProperty("canApplyTemplate").GetBoolean().Should().BeFalse();
            var closed = await ApplyAsync(owner, running, otherId, Version(closedView));
            closed.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await closed.Content.ReadAsStringAsync()).Should().Contain("Projeto encerrado ou cancelado: as etapas não mudam.");

            // CA 6: sem o recurso projects.service_execution, 403 module_not_released.
            await ServicesAsync(false);
            var forbidden = await owner.GetAsync($"/api/v1/projects/{planned}/stages");
            forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(forbidden)).Should().Be("module_not_released");
        }
        finally
        {
            await ServicesAsync(false);
        }
    }

    [Fact]
    public async Task Konebras_sem_perfil_servicos_nao_ganha_etapas_nem_currentStage()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var item = await ServiceItemAsync();
        await TemplateAsync(item);
        var (quoteId, quote) = await QuoteAsync(owner, "Service", item, 1m);
        var (contractId, projectId) = await ApproveAsync(owner, quoteId, quote);
        (await WithDbAsync(db => db.ProjectServiceStages.CountAsync(s => s.ProjectId == projectId))).Should().Be(0);
        var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"));
        project.GetProperty("executionKind").GetString().Should().Be("Fabrication");
        project.GetProperty("currentStage").ValueKind.Should().Be(JsonValueKind.Null);
        (await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"))).GetProperty("snapshot").TryGetProperty("stages", out _).Should().BeFalse();
        await using (var scope = await TenantScopeAsync())
        {
            var summary = (await scope.ServiceProvider.GetRequiredService<IProjects>().ListByPartyAsync(project.GetProperty("partyId").GetGuid())).Single();
            JsonSerializer.Serialize(IntegrationProjectEndpoints.Map(summary, Today), WebJson).Should().NotContain("currentStage");
        }

        var stages = await owner.GetAsync($"/api/v1/projects/{projectId}/stages");
        stages.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(stages)).Should().Be("module_not_released");
    }

    // ----- Apoio -----

    private static List<Dictionary<string, object?>> Lines(JsonElement view) =>
        view.GetProperty("stages").EnumerateArray().Select(s => new Dictionary<string, object?>
        {
            ["id"] = s.GetProperty("id").GetGuid(), ["name"] = s.GetProperty("name").GetString(), ["code"] = s.GetProperty("code").GetString(),
            ["role"] = s.GetProperty("role").GetString(), ["plannedHours"] = s.GetProperty("plannedHours").GetDecimal(),
            ["positionId"] = s.GetProperty("positionId").ValueKind == JsonValueKind.Null ? null : s.GetProperty("positionId").GetGuid(),
            ["requiresApproval"] = s.GetProperty("requiresApproval").GetBoolean(),
            ["checklist"] = s.GetProperty("checklist").EnumerateArray().Select(c => c.GetProperty("text").GetString()).ToList(),
            ["responsibleUserId"] = s.GetProperty("responsibleUserId").ValueKind == JsonValueKind.Null ? null : s.GetProperty("responsibleUserId").GetGuid(),
            ["plannedStart"] = s.GetProperty("plannedStart").GetString(), ["plannedEnd"] = s.GetProperty("plannedEnd").GetString(),
        }).ToList();

    private static Task<HttpResponseMessage> ApplyAsync(HttpClient owner, Guid projectId, Guid templateId, uint version) =>
        owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/stages/apply-template", new { templateId, version });

    private Task<List<string>> ReceivablesAsync(Guid contractId) => WithDbAsync(db => db.Receivables.AsNoTracking().Where(r => r.ContractId == contractId).OrderBy(r => r.Number)
        .Select(r => r.Number + "|" + r.Status + "|" + r.Amount + "|" + r.DueDate + "|" + r.Version).ToListAsync());

    /// <summary>Modelo ativo direto no banco (o cadastro tem os próprios testes): K e T de Execução, G Entrega, A Aceite; ligado ao item, se houver.</summary>
    private Task<(Guid Id, string Name)> TemplateAsync(Guid? itemId) => WithDbAsync(async db =>
    {
        var template = new StageTemplate { Name = "Implantação " + Guid.NewGuid().ToString("N")[..6], CreatedAt = DateTimeOffset.UtcNow };
        db.StageTemplates.Add(template);
        db.StageTemplateSteps.AddRange(
            new StageTemplateStep { TemplateId = template.Id, Sequence = 1, Code = "K", Name = "Kickoff", Role = StageRole.Execution, StandardHoursPerUnit = 2m },
            new StageTemplateStep { TemplateId = template.Id, Sequence = 2, Code = "T", Name = "Treinamento", Role = StageRole.Execution, StandardHoursPerUnit = 1.5m, Checklist = ["Lista de presença", "Material"] },
            new StageTemplateStep { TemplateId = template.Id, Sequence = 3, Code = "G", Name = "Go-live", Role = StageRole.Delivery, StandardHoursPerUnit = 0.5m },
            new StageTemplateStep { TemplateId = template.Id, Sequence = 4, Code = "A", Name = "Aceite", Role = StageRole.Acceptance, StandardHoursPerUnit = 0m });
        if (itemId is { } item)
        {
            db.StageTemplateItems.Add(new StageTemplateItem { TemplateId = template.Id, ItemId = item, CreatedAt = DateTimeOffset.UtcNow });
        }

        await db.SaveChangesAsync();
        return (template.Id, template.Name);
    });

    private Task<Guid> ServiceItemAsync() => WithDbAsync(async db =>
    {
        var unit = await db.Units.Select(u => u.Id).FirstAsync();
        var item = new BemAqui.Inventory.Domain.Item
        {
            Type = BemAqui.Inventory.Contracts.ItemType.Service, Code = "SRV-" + Guid.NewGuid().ToString("N")[..5], Name = "Implantação", UnitId = unit, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    });

    /// <summary>Orçamento enviado com um item (do cadastro, se informado) e os marcos de serviço.</summary>
    private static async Task<(Guid Id, JsonElement Quote)> QuoteAsync(HttpClient owner, string kind, Guid? itemId, decimal quantity)
    {
        var customer = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Cliente " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Customer" } });
        customer.StatusCode.Should().Be(HttpStatusCode.Created, await customer.Content.ReadAsStringAsync());
        var partyId = (await JsonAsync(customer)).GetProperty("id").GetGuid();
        var quote = await JsonAsync(await owner.SendAsync(Post(Quotes, new { partyId, contactId = (Guid?)null, siteAddressId = (Guid?)null })));
        var id = quote.GetProperty("id").GetGuid();
        var added = await owner.PostAsJsonAsync($"{Quotes}/{id}/items",
            new { kind, itemId, description = "Implantação do sistema", quantity, unitPrice = (decimal?)10_000m, resetPrice = false, version = Version(quote) });
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        quote = await JsonAsync(await owner.GetAsync($"{Quotes}/{id}"));
        var conditions = await owner.PutAsJsonAsync($"{Quotes}/{id}/conditions", new { milestones = ServiceMilestones, validUntil = Iso(Today.AddDays(15)), version = Version(quote) });
        conditions.StatusCode.Should().Be(HttpStatusCode.OK, await conditions.Content.ReadAsStringAsync());
        quote = await JsonAsync(await owner.GetAsync($"{Quotes}/{id}"));
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Quotes}/{id}/send", new { version = Version(quote) }));
        return (id, quote);
    }

    private async Task<Guid> ServiceProjectAsync(HttpClient owner)
    {
        var (quoteId, quote) = await QuoteAsync(owner, "Service", null, 1m);
        return (await ApproveAsync(owner, quoteId, quote)).Project;
    }

    private async Task<(Guid Contract, Guid Project)> ApproveAsync(HttpClient owner, Guid id, JsonElement quote)
    {
        var account = (await JsonAsync(await owner.GetAsync($"{Quotes}/{id}/approval"))).GetProperty("suggestedRevenueAccountId").GetGuid();
        var approved = await owner.SendAsync(Post($"{Quotes}/{id}/approve", new { signedAt = Iso(Today), revenueAccountId = account, version = Version(quote) }));
        approved.StatusCode.Should().Be(HttpStatusCode.Created, await approved.Content.ReadAsStringAsync());
        var body = await JsonAsync(approved);
        await DrainAsync();
        return (body.GetProperty("contractId").GetGuid(), body.GetProperty("projectId").GetGuid());
    }

    private static HttpRequestMessage Post(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }

    private async Task DrainAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        for (var round = 0; round < 60; round++)
        {
            await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
            {
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(info);
            }

            if (await WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= DateTimeOffset.UtcNow.AddSeconds(1))) == 0)
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("O outbox não esvaziou.");
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await TenantScopeAsync();
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<AsyncServiceScope> TenantScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "e" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await TenantScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

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
}
