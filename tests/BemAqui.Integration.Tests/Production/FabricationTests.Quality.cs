using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Identity.Domain;
using BemAqui.Identity.Infrastructure;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

public abstract partial class FabricationTestBase
{
    protected const string Quality = "/api/v1/production/quality";
    protected const string Ncs = "/api/v1/production/non-conformities";

    /// <summary>Roteiro C (corte) → M (montagem, centro do operador) → Q (qualidade com checklist) [→ E (embalagem)], com uma OS.</summary>
    protected async Task<(HttpClient Owner, Guid Center, Guid Order)> QualityDatasetAsync(bool withPacking = false)
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var center = await CenterAsync("Montagem", "Linha 1");
        object S(string name, string code, bool quality = false, string[]? items = null) =>
            new { id = (Guid?)null, name, code, workCenterId = center, standardHours = 1m, isQualityStep = quality, requiresApproval = false, checklist = items ?? [] };
        var steps = new List<object> { S("Corte", "C"), S("Montagem", "M"), S("Qualidade", "Q", quality: true, items: ["Conferir nivelamento", "Ruído do motor"]) };
        if (withPacking)
        {
            steps.Add(S("Embalagem", "E"));
        }

        var routing = await RoutingAsync(owner, center, null, null, [.. steps]);
        var (_, _, items) = await ApprovedAsync(owner, [("Equipment", (Guid?)await ProductAsync(), 1m)], routingOnItems: routing);
        return (owner, center, (await OrderOfAsync(items[0]))!.Id);
    }

    /// <summary>O operador inicia e finaliza as etapas produtivas até a qualidade (com o checklist marcado).</summary>
    protected async Task WorkUntilQualityAsync(HttpClient operatorClient, Guid orderId, params int[] sequences)
    {
        foreach (var sequence in sequences)
        {
            var step = await StepIdAsync(orderId, sequence);
            await OkAsync(await operatorClient.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")));
            await CheckAllAsync(operatorClient, step);
            await OkAsync(await operatorClient.SendAsync(Tap($"{Shopfloor}/steps/{step}/finish", new { forAll = false })));
        }
    }

    protected static async Task<JsonElement> QualityStepAsync(HttpClient client, Guid stepId) => await OkAsync(await client.GetAsync($"{Quality}/steps/{stepId}"));

    /// <summary>Marca todos os itens do checklist da qualidade com o resultado dado.</summary>
    protected static async Task MarkQualityAsync(HttpClient client, Guid stepId, string result = "Done")
    {
        var detail = await QualityStepAsync(client, stepId);
        foreach (var item in detail.GetProperty("checklist").EnumerateArray())
        {
            (await client.PutAsJsonAsync($"{Quality}/steps/{stepId}/checklist/{item.GetProperty("id").GetGuid()}", new { result })).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    protected static async Task<HttpResponseMessage> ApproveQualityAsync(HttpClient client, Guid stepId, uint version) => await client.SendAsync(Post($"{Quality}/steps/{stepId}/approve", new { version }));

    protected static async Task<HttpResponseMessage> RejectQualityAsync(HttpClient client, Guid stepId, uint version, Guid? returnTo = null, Guid? responsible = null, string description = "Painel riscado na lateral") =>
        await client.SendAsync(Post($"{Quality}/steps/{stepId}/reject", new { description, cause = (string?)null, action = (string?)null, responsibleUserId = responsible, returnToStepId = returnTo, version }));

    protected async Task<uint> OrderVersionAsync(Guid orderId) => (await OrderAsync(orderId)).Version;
}

/// <summary>
/// PRD-05, qualidade e não conformidade: ação e seed patch (CA 1), entrada na qualidade e fila (CA 2), aprovar (CA 3 e 4), segregação
/// com "Autoinspeção" (CA 5), reprovar com NC (CA 6), nova tentativa (CA 7), anexos (CA 8), ciclo e custo da NC (CA 9 e 10), lista e chip
/// (CA 11), três camadas e IDOR (CA 12), a ação sozinha fora da qualidade (CA 13), decisão concorrente (CA 14) e auditoria (CA 15).
/// </summary>
public partial class FabricationBoardTests
{
    [Fact]
    public async Task Acao_approve_quality_no_catalogo_e_seed_patch_da_supervisao_uma_vez()
    {
        ModuleCatalog.IsKnownPermission("production.approve_quality").Should().BeTrue("CA 1: ação específica no catálogo");
        ModuleCatalog.Find("production")!.Specific.Should().ContainSingle(a => a.Code == "approve_quality");
        var supervision = await WithDbAsync(db => db.Groups.AsNoTracking().SingleAsync(g => g.SeedKey == "production_supervision"));
        await WithDbAsync(async db => (await db.SeedPatches.AsNoTracking().CountAsync(p => p.Key == "production-quality")).Should().Be(1));
        (await WithDbAsync(db => db.GroupPermissions.AsNoTracking().CountAsync(p => p.GroupId == supervision.Id && p.ModuleCode == "production" && p.Action == "approve_quality")))
            .Should().Be(1);

        // Rodar de novo (migrate) não duplica: o patch já aplicado não roda.
        await using (var scope = await ScopeAsync())
        {
            await new SeedPatchSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        (await WithDbAsync(db => db.GroupPermissions.AsNoTracking().CountAsync(p => p.GroupId == supervision.Id && p.ModuleCode == "production" && p.Action == "approve_quality")))
            .Should().Be(1);
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        (await owner.GetStringAsync("/api/v1/me/permissions")).Should().Contain("production.approve_quality", "o Proprietário recebe a ação");
    }

    [Fact]
    public async Task Etapa_anterior_concluida_poe_a_OS_em_qualidade_e_na_fila_de_quem_decide()
    {
        var (_, center, order) = await QualityDatasetAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        await WorkUntilQualityAsync(operatorClient, order, 1, 2);
        var q = await StepIdAsync(order, 3);
        (await OrderAsync(order)).Status.Should().Be(ProductionOrderStatus.Quality, "CA 2");
        (await StepAsync(q)).Status.Should().Be(ProductionStepStatus.Ready);

        var (_, inspector) = await ActorAsync("production:view,approve_quality");
        var queue = await OkAsync(await inspector.GetAsync($"{Quality}/queue"));
        queue.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("stepId").GetGuid() == q, "Aguardando você é o filtro padrão");
        var (_, supervisorOnly) = await ActorAsync("production:view,approve");
        (await supervisorOnly.GetAsync($"{Quality}/queue")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "production.approve sozinha não decide a qualidade");
        (await operatorClient.GetAsync($"{Shopfloor}/steps/{q}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "a qualidade fica fora da fila do operador");
    }

    [Fact]
    public async Task Aprovar_a_ultima_etapa_deixa_a_OS_pronta_para_concluir_nunca_concluida()
    {
        var (_, center, order) = await QualityDatasetAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        await WorkUntilQualityAsync(operatorClient, order, 1, 2);
        var q = await StepIdAsync(order, 3);
        var (_, inspector) = await ActorAsync("production:view,approve_quality");

        var incomplete = await ApproveQualityAsync(inspector, q, await OrderVersionAsync(order));
        incomplete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(incomplete)).GetProperty("detail").GetString().Should().Be("Marque os 2 itens que faltam.");

        await MarkQualityAsync(inspector, q);
        var result = await OkAsync(await ApproveQualityAsync(inspector, q, await OrderVersionAsync(order)));
        result.GetProperty("message").GetString().Should().Be("Qualidade aprovada");
        result.GetProperty("readyToComplete").GetBoolean().Should().BeTrue("D12: Pronta para concluir");
        result.GetProperty("canCompleteOrder").GetBoolean().Should().BeFalse("CA 4: só production.approve conclui a OS");
        var saved = await OrderAsync(order);
        saved.Status.Should().Be(ProductionOrderStatus.ReadyToComplete, "nunca Completed: a conclusão é explícita (PRD-06a1 CA 1)");
        (await StepAsync(q)).Status.Should().Be(ProductionStepStatus.Done);
        (await OutboxAsync<QualityApproved>(order)).Should().Be(1);
        (await WithDbAsync(db => db.QualityDecisions.AsNoTracking().CountAsync(d => d.StepId == q && d.Result == QualityResult.Approved && d.Attempt == 1))).Should().Be(1);
    }

    [Fact]
    public async Task Aprovar_no_meio_libera_a_proxima_e_o_supervisor_ve_concluir_OS()
    {
        var (_, center, order) = await QualityDatasetAsync(withPacking: true);
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        await WorkUntilQualityAsync(operatorClient, order, 1, 2);
        var q = await StepIdAsync(order, 3);
        var (_, supervisor) = await ActorAsync("production:view,approve,approve_quality");
        await MarkQualityAsync(supervisor, q);
        var result = await OkAsync(await ApproveQualityAsync(supervisor, q, await OrderVersionAsync(order)));
        result.GetProperty("nextStepName").GetString().Should().Be("Embalagem");
        result.GetProperty("readyToComplete").GetBoolean().Should().BeFalse();
        result.GetProperty("canCompleteOrder").GetBoolean().Should().BeTrue();
        (await StepAsync(await StepIdAsync(order, 4))).Status.Should().Be(ProductionStepStatus.Ready);
        (await OrderAsync(order)).Status.Should().Be(ProductionOrderStatus.InProgress);
    }

    [Fact]
    public async Task Quem_apontou_a_tentativa_nao_aprova_salvo_sem_outro_elegivel_com_autoinspecao()
    {
        var (_, center, order) = await QualityDatasetAsync();
        var (inspectorId, _, inspector, _) = await OperatorAsync(center, group: "production_supervision");
        await WorkUntilQualityAsync(inspector, order, 1, 2);
        var q = await StepIdAsync(order, 3);
        await ActorAsync("production:view,approve_quality");
        await MarkQualityAsync(inspector, q);

        var refused = await ApproveQualityAsync(inspector, q, await OrderVersionAsync(order));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(refused)).GetProperty("detail").GetString().Should().Be("Quem fez uma etapa desta tentativa não aprova a qualidade dela.");

        var others = await WithDbAsync(db => db.Users.Where(u => u.Id != inspectorId && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync());
        var ownerGroup = await WithDbAsync(db => db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync());
        await WithDbAsync(db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Inactive)));
        await WithDbAsync(async db => { db.UserGroups.Add(new UserGroup { UserId = inspectorId, GroupId = ownerGroup }); return await db.SaveChangesAsync(); });
        try
        {
            await using (var scope = await ScopeAsync())
            {
                await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([inspectorId]);
            }

            var allowed = await OkAsync(await ApproveQualityAsync(inspector, q, await OrderVersionAsync(order)));
            allowed.GetProperty("selfInspection").GetBoolean().Should().BeTrue("marca Autoinspeção");
            (await StepAsync(q)).SelfInspection.Should().BeTrue();
            var saved = await OrderAsync(order);
            saved.SelfInspection.Should().BeTrue("a marca também fica na OS");
            (await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == inspectorId && n.Type == QualityService.SelfInspectionType).Select(n => n.Title).ToListAsync()))
                .Should().ContainSingle().Which.Should().StartWith($"Autoinspeção na {saved.Number}:").And.EndWith("aprovou a qualidade do próprio trabalho.");
        }
        finally
        {
            await WithDbAsync(db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Active)));
            await WithDbAsync(db => db.UserGroups.Where(ug => ug.UserId == inspectorId && ug.GroupId == ownerGroup).ExecuteDeleteAsync());
        }
    }

    [Fact]
    public async Task Reprovar_abre_a_NC_devolve_a_etapa_e_avisa_na_mesma_transacao_e_a_segunda_tentativa_aprova()
    {
        var (_, center, order) = await QualityDatasetAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        await WorkUntilQualityAsync(operatorClient, order, 1, 2);
        var (c, m, q) = (await StepIdAsync(order, 1), await StepIdAsync(order, 2), await StepIdAsync(order, 3));
        var (inspectorId, inspector) = await ActorAsync("production:view,approve_quality");
        var (supervisorId, _) = await ActorAsync("production:view,approve");

        var detail = await QualityStepAsync(inspector, q);
        detail.GetProperty("returnOptions").EnumerateArray().First().GetProperty("suggested").GetBoolean().Should().BeTrue();
        detail.GetProperty("returnOptions").EnumerateArray().First().GetProperty("stepId").GetGuid().Should().Be(m, "a produtiva imediatamente anterior");
        detail.GetProperty("defaultResponsibleId").GetGuid().Should().Be(inspectorId, "ponto 77: quem reprovou");

        (await inspector.PutAsJsonAsync($"{Quality}/steps/{q}/checklist/{detail.GetProperty("checklist")[0].GetProperty("id").GetGuid()}", new { result = "NonConforming" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ApproveQualityAsync(inspector, q, await OrderVersionAsync(order))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "item não conforme pede reprovação");

        var rejected = await OkAsync(await RejectQualityAsync(inspector, q, await OrderVersionAsync(order)));
        var ncNumber = rejected.GetProperty("nonConformityNumber").GetString()!;
        ncNumber.Should().MatchRegex(@"^NC-\d{4}-\d{4}$");
        rejected.GetProperty("message").GetString().Should().Be($"NC {ncNumber} aberta");
        var nc = await WithDbAsync(db => db.NonConformities.AsNoTracking().SingleAsync(n => n.OrderId == order));
        nc.Status.Should().Be(NonConformityStatus.Open);
        nc.ResponsibleUserId.Should().Be(inspectorId);
        nc.ReturnToStepId.Should().Be(m);
        nc.Attempt.Should().Be(1);
        (await StepAsync(q)).Status.Should().Be(ProductionStepStatus.Rejected);
        (await StepAsync(m)).Status.Should().Be(ProductionStepStatus.Ready);
        (await StepAsync(c)).Status.Should().Be(ProductionStepStatus.Done);
        var saved = await OrderAsync(order);
        saved.Status.Should().Be(ProductionOrderStatus.InProgress);
        saved.QualityAttempt.Should().Be(2);
        (await OutboxAsync<QualityRejected>(nc.Id)).Should().Be(1);
        var titles = await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.Type == QualityService.RejectedType && (n.UserId == inspectorId || n.UserId == supervisorId)).ToListAsync());
        titles.Select(n => n.UserId).Should().BeEquivalentTo([inspectorId, supervisorId]);
        titles.Should().OnlyContain(n => n.Title.StartsWith($"NC {ncNumber} na {saved.Number}:", StringComparison.Ordinal) && n.Title.EndsWith("Voltou para Montagem.", StringComparison.Ordinal)
            && n.Link == QualityService.NonConformityLink + nc.Id);
        (await EntriesAsync(m)).Should().NotBeEmpty("os apontamentos da primeira tentativa ficam");

        // CA 7: refazer a Montagem leva à MESMA etapa de qualidade, com o checklist zerado (tentativa 2), e outro usuário aprova.
        await WorkUntilQualityAsync(operatorClient, order, 2);
        (await WithDbAsync(db => db.ProductionOrderSteps.AsNoTracking().CountAsync(s => s.OrderId == order && s.IsQualityStep))).Should().Be(1);
        (await StepAsync(q)).Status.Should().Be(ProductionStepStatus.Ready);
        var second = await QualityStepAsync(inspector, q);
        second.GetProperty("step").GetProperty("attempt").GetInt32().Should().Be(2);
        second.GetProperty("checklist").EnumerateArray().Should().OnlyContain(i => i.GetProperty("result").ValueKind == JsonValueKind.Null, "checklist zerado");
        var (_, another) = await ActorAsync("production:view,approve_quality");
        await MarkQualityAsync(another, q);
        await OkAsync(await ApproveQualityAsync(another, q, await OrderVersionAsync(order)));

        var tab = await OkAsync(await inspector.GetAsync($"{Base}/{order}/quality"));
        var attempts = tab.GetProperty("attempts").EnumerateArray().ToList();
        attempts.Select(a => a.GetProperty("attempt").GetInt32()).Should().Equal(2, 1);
        attempts[1].GetProperty("result").GetString().Should().Be("Rejected");
        attempts[1].GetProperty("nonConformityNumber").GetString().Should().Be(ncNumber);
        attempts[1].GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("result").GetString() == "NonConforming", "o histórico da tentativa anterior fica guardado");
        attempts[0].GetProperty("result").GetString().Should().Be("Approved");
    }

    [Fact]
    public async Task Etapa_de_retorno_de_outra_OS_ou_posterior_e_recusada()
    {
        var (_, center, order) = await QualityDatasetAsync(withPacking: true);
        var (_, center2, other) = await QualityDatasetAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        await WorkUntilQualityAsync(operatorClient, order, 1, 2);
        var q = await StepIdAsync(order, 3);
        var (_, inspector) = await ActorAsync("production:view,approve_quality");
        var foreign = await RejectQualityAsync(inspector, q, await OrderVersionAsync(order), returnTo: await StepIdAsync(other, 1));
        foreign.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var later = await RejectQualityAsync(inspector, q, await OrderVersionAsync(order), returnTo: await StepIdAsync(order, 4));
        later.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await WithDbAsync(db => db.NonConformities.AsNoTracking().CountAsync(n => n.OrderId == order))).Should().Be(0);
        center2.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Ciclo_da_NC_custo_informativo_anexos_lista_e_chip_no_Kanban()
    {
        var (owner, center, order) = await QualityDatasetAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        await WorkUntilQualityAsync(operatorClient, order, 1, 2);
        var q = await StepIdAsync(order, 3);
        var (inspectorId, inspector) = await ActorAsync("production:view,approve_quality");
        var nc = (await OkAsync(await RejectQualityAsync(inspector, q, await OrderVersionAsync(order)))).GetProperty("nonConformityId").GetGuid();

        // CA 11: lista "Aguardando você" do responsável e o chip "NC aberta" no Kanban.
        var mine = await OkAsync(await inspector.GetAsync(Ncs));
        mine.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("id").GetGuid() == nc);
        var board = await OkAsync(await owner.GetAsync("/api/v1/production/board"));
        Card(board, order).GetProperty("openNonConformities").GetInt32().Should().Be(1);

        // CA 8: anexo com production.approve_quality; só ver não anexa.
        using (var png = new MultipartFormDataContent { { new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]) { Headers = { ContentType = new("image/png") } }, "file", "foto.png" } })
        {
            (await inspector.PostAsync($"{Ncs}/{nc}/attachments", png)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var (_, viewer) = await ActorAsync("production:view");
        using (var png = new MultipartFormDataContent { { new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]) { Headers = { ContentType = new("image/png") } }, "file", "foto.png" } })
        {
            (await viewer.PostAsync($"{Ncs}/{nc}/attachments", png)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var files = await OkAsync(await viewer.GetAsync($"{Ncs}/{nc}/attachments"));
        var attachmentId = files.EnumerateArray().Single().GetProperty("id").GetGuid();
        (await viewer.GetAsync($"{Ncs}/{Guid.CreateVersion7()}/attachments/{attachmentId}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR: anexo de outra NC");

        // CA 9: editar e encerrar só com production.edit; iniciar ação e encerrar exigem causa e ação.
        var (_, editor) = await ActorAsync("production:view,edit");
        var detail = await OkAsync(await editor.GetAsync($"{Ncs}/{nc}"));
        (await inspector.PostAsJsonAsync($"{Ncs}/{nc}/start-action", new { cause = "x", action = "y", version = Version(detail) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var early = await editor.PostAsJsonAsync($"{Ncs}/{nc}/close", new { result = "Ok", version = Version(detail) });
        early.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(early)).GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Contain(["cause", "action"]);
        var missing = await editor.PostAsJsonAsync($"{Ncs}/{nc}/start-action", new { cause = (string?)null, action = "Trocar painel", version = Version(detail) });
        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var inAction = await OkAsync(await editor.PostAsJsonAsync($"{Ncs}/{nc}/start-action", new { cause = "Transporte sem proteção", action = "Trocar painel e proteger", version = Version(detail) }));
        inAction.GetProperty("status").GetString().Should().Be("InAction");

        // CA 10: custo só com projects.view_cost e fora do realizado do projeto.
        var costsBefore = await WithDbAsync(db => db.ProjectCosts.AsNoTracking().CountAsync());
        (await editor.PutAsJsonAsync($"{Ncs}/{nc}/cost", new { cost = 350m, version = Version(inAction) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (_, costEditor) = await ActorAsync("production:view,edit;projects:view,view_cost");
        var withCost = await OkAsync(await costEditor.PutAsJsonAsync($"{Ncs}/{nc}/cost", new { cost = 350.456m, version = Version(inAction) }));
        withCost.GetProperty("cost").GetDecimal().Should().Be(350.46m);
        (await OkAsync(await editor.GetAsync($"{Ncs}/{nc}"))).GetProperty("cost").ValueKind.Should().Be(JsonValueKind.Null, "sem projects.view_cost não vê o custo");
        (await WithDbAsync(db => db.ProjectCosts.AsNoTracking().CountAsync())).Should().Be(costsBefore, "ponto 78: custo da NC é só informativo");

        var closed = await OkAsync(await editor.PostAsJsonAsync($"{Ncs}/{nc}/close", new { result = "Painel trocado e aprovado.", version = Version(withCost) }));
        closed.GetProperty("status").GetString().Should().Be("Closed");
        closed.GetProperty("closedByName").ValueKind.Should().NotBe(JsonValueKind.Null);
        (await editor.PutAsJsonAsync($"{Ncs}/{nc}", new { description = "Nova", cause = "c", action = "a", responsibleUserId = inspectorId, version = Version(closed) }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "NC encerrada fica em modo leitura");

        // CA 15: histórico da NC e da OS.
        (await OkAsync(await editor.GetAsync($"{Ncs}/{nc}/history"))).GetProperty("items").GetArrayLength().Should().BeGreaterThan(1);
        var history = (await OkAsync(await editor.GetAsync($"{Base}/{order}/history"))).GetRawText();
        history.Should().Contain(nameof(QualityDecision)).And.Contain(nameof(NonConformity));
        (await OkAsync(await inspector.GetAsync(Ncs))).GetProperty("items").EnumerateArray().Should().NotContain(i => i.GetProperty("id").GetGuid() == nc, "encerrada sai de Aguardando você");
    }

    [Fact]
    public async Task Tres_camadas_IDOR_e_approve_quality_sozinha_fora_da_qualidade()
    {
        var (_, center, order) = await QualityDatasetAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        await WorkUntilQualityAsync(operatorClient, order, 1, 2);
        var q = await StepIdAsync(order, 3);
        var (_, noGroup) = await ActorAsync(null);
        (await noGroup.GetAsync($"{Quality}/queue")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (_, supervisorOnly) = await ActorAsync("production:view,approve");
        (await RejectQualityAsync(supervisorOnly, q, await OrderVersionAsync(order))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "CA 12: production.approve sozinha não reprova");

        // CA 13: só production.approve_quality não conclui etapa, não corrige apontamento, não cancela e não aprova etapa.
        var (_, quality) = await ActorAsync("production:view,approve_quality");
        var version = await OrderVersionAsync(order);
        var m = await StepIdAsync(order, 2);
        (await quality.PostAsJsonAsync($"{Base}/{order}/steps/{m}/complete", new { reason = "Teste", version })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await quality.PostAsJsonAsync($"{Base}/{order}/steps/{m}/approve", new { version })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await quality.PostAsJsonAsync($"{Base}/{order}/cancel", new { reason = "Teste", version })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var entry = (await EntriesAsync(m)).First();
        (await quality.PostAsJsonAsync($"{Base}/{order}/labor-entries/{entry.Id}/correct", new { startedAt = entry.StartedAt, endedAt = entry.EndedAt, pauses = Array.Empty<object>(), reason = "x", version = entry.Version }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await quality.GetAsync($"{Quality}/steps/{m}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "etapa produtiva não é decidida na qualidade");

        // IDOR da NC pela OS.
        var nc = (await OkAsync(await RejectQualityAsync(quality, q, await OrderVersionAsync(order)))).GetProperty("nonConformityId").GetGuid();
        (await quality.GetAsync($"{Base}/{order}/non-conformities/{nc}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await quality.GetAsync($"{Base}/{Guid.CreateVersion7()}/non-conformities/{nc}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await SetModuleReleasedAsync("production", false);
        try
        {
            var refused = await quality.GetAsync($"{Quality}/queue");
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(refused)).Should().Be("module_not_released");
        }
        finally
        {
            await SetModuleReleasedAsync("production", true);
        }
    }

    [Fact]
    public async Task Aprovar_e_reprovar_juntos_so_um_grava_e_a_NC_nao_duplica()
    {
        var (_, center, order) = await QualityDatasetAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        await WorkUntilQualityAsync(operatorClient, order, 1, 2);
        var q = await StepIdAsync(order, 3);
        var (_, a) = await ActorAsync("production:view,approve_quality");
        var (_, b) = await ActorAsync("production:view,approve_quality");
        await MarkQualityAsync(a, q);
        var version = await OrderVersionAsync(order);

        var responses = await Task.WhenAll(ApproveQualityAsync(a, q, version), RejectQualityAsync(b, q, version), RejectQualityAsync(a, q, version));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "CA 14: xmin");
        responses.Where(r => r.StatusCode != HttpStatusCode.OK).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        (await WithDbAsync(db => db.NonConformities.AsNoTracking().CountAsync(n => n.OrderId == order))).Should().BeLessThanOrEqualTo(1);
        (await WithDbAsync(db => db.QualityDecisions.AsNoTracking().CountAsync(d => d.OrderId == order))).Should().Be(1);
    }

    [Fact]
    public async Task Inspecao_iniciada_fecha_o_apontamento_do_inspetor_na_decisao()
    {
        var (_, center, order) = await QualityDatasetAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        await WorkUntilQualityAsync(operatorClient, order, 1, 2);
        var q = await StepIdAsync(order, 3);
        var (_, _, inspector, _) = await OperatorAsync(center, group: "production_supervision");
        var started = await OkAsync(await inspector.SendAsync(Tap($"{Quality}/steps/{q}/start")));
        started.GetProperty("inspectionStarted").GetBoolean().Should().BeTrue();
        (await OrderAsync(order)).Status.Should().Be(ProductionOrderStatus.Quality, "a OS continua em qualidade durante a inspeção");
        await MarkQualityAsync(inspector, q);
        await OkAsync(await ApproveQualityAsync(inspector, q, await OrderVersionAsync(order)));
        (await EntriesAsync(q)).Should().ContainSingle().Which.EndedAt.Should().NotBeNull("CA 3: o apontamento do inspetor fecha");
    }
}