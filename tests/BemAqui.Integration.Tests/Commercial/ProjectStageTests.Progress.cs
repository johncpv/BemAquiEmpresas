using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>
/// EXE-03b: andamento das etapas (iniciar chama o "Iniciar execução", checklist com IDOR, concluir publica o evento com a versão, Entrega e Aceite
/// pelos serviços da EXE-01a, autoaprovação nos três modos, paralelo com um 409, idempotência e 403 sem o recurso).
/// </summary>
public partial class ProjectStageTests
{
    private static string Stages(Guid projectId) => $"/api/v1/projects/{projectId}/stages";

    private static Guid StageId(JsonElement view, string code) =>
        view.GetProperty("stages").EnumerateArray().Single(s => s.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();

    private static JsonElement Stage(JsonElement view, string code) =>
        view.GetProperty("stages").EnumerateArray().Single(s => s.GetProperty("code").GetString() == code);

    private static HttpRequestMessage Put(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private Task<int> OutboxCountAsync(string typeSuffix, string payloadPart) =>
        WithDbAsync(db => db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM shared.outbox_messages WHERE type LIKE {"%" + typeSuffix} AND payload::text LIKE {"%" + payloadPart + "%"}").SingleAsync());

    private Task<int> SetStageSelfApprovalAsync(decimal? value) => WithDbAsync(async db =>
    {
        await db.CommercialParameters.Where(p => p.Key == CommercialParametersService.StageSelfApproval).ExecuteDeleteAsync();
        if (value is { } v)
        {
            db.CommercialParameters.Add(new CommercialParameter { Key = CommercialParametersService.StageSelfApproval, Value = v, UpdatedAt = DateTimeOffset.UtcNow });
        }

        return await db.SaveChangesAsync();
    });

    /// <summary>Usuário num grupo novo só com as ações pedidas de <c>projects</c>.</summary>
    private async Task<(Guid Id, HttpClient Client)> GroupActorAsync(params string[] actions)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "g" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await TenantScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = "Etapas " + Guid.NewGuid().ToString("N")[..8], Description = "Teste das etapas" };
            db.Groups.Add(group);
            db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "projects", Enabled = true });
            db.GroupPermissions.AddRange(actions.Select(a => new GroupGrant { GroupId = group.Id, ModuleCode = "projects", Action = a }));
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    [Fact]
    public async Task Etapas_andam_do_inicio_ao_aceite_com_eventos_versao_autoaprovacao_e_concorrencia()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, editor) = await GroupActorAsync("view", "edit");
        var (approverId, approver) = await GroupActorAsync("view", "edit", "approve");
        await ServicesAsync(true);
        try
        {
            var item = await ServiceItemAsync();
            await TemplateAsync(item);
            var (quoteId, quote) = await QuoteAsync(owner, "Service", item, 1m);
            var (_, projectId) = await ApproveAsync(owner, quoteId, quote);
            await WithDbAsync(db => db.ProjectServiceStages.Where(s => s.ProjectId == projectId && s.Code == "T").ExecuteUpdateAsync(u => u.SetProperty(s => s.RequiresApproval, true)));
            var view = await OkAsync(await owner.GetAsync(Stages(projectId)));
            var k = StageId(view, "K");

            // CA 9: sem Idempotency-Key, 422. CA 1 e 9: dois "Iniciar etapa" em paralelo com a mesma versão = uma gravação e um 409; o projeto
            // Planned vai a InProgress pelo StartAsync (um ProjectStarted).
            (await owner.PostAsJsonAsync($"{Stages(projectId)}/{k}/start", new { version = Version(view), expectedStatus = "Planned" })).StatusCode
                .Should().Be(HttpStatusCode.UnprocessableEntity);
            var both = await Task.WhenAll(
                owner.SendAsync(Post($"{Stages(projectId)}/{k}/start", new { version = Version(view), expectedStatus = "Planned" })),
                owner.SendAsync(Post($"{Stages(projectId)}/{k}/start", new { version = Version(view), expectedStatus = "Planned", date = Iso(Today) })));
            both.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
            (await OutboxCountAsync("ProjectStarted", projectId.ToString())).Should().Be(1);
            var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"));
            project.GetProperty("status").GetString().Should().Be("InProgress");
            view = await OkAsync(await owner.GetAsync(Stages(projectId)));
            Stage(view, "K").GetProperty("status").GetString().Should().Be("InProgress");

            // CA 3 e 9: duplo clique no "Concluir etapa" com a mesma Idempotency-Key = uma gravação e a mesma resposta; a próxima fica Ready.
            var complete = new HttpRequestMessage(HttpMethod.Post, $"{Stages(projectId)}/{k}/complete") { Content = JsonContent.Create(new { date = Iso(Today), version = Version(view) }) };
            var key = Guid.NewGuid().ToString();
            complete.Headers.Add("Idempotency-Key", key);
            var first = await (await editor.SendAsync(complete)).Content.ReadAsStringAsync();
            var again = new HttpRequestMessage(HttpMethod.Post, $"{Stages(projectId)}/{k}/complete") { Content = JsonContent.Create(new { date = Iso(Today), version = Version(view) }) };
            again.Headers.Add("Idempotency-Key", key);
            var replay = await editor.SendAsync(again);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
            (await replay.Content.ReadAsStringAsync()).Should().Be(first);
            view = JsonDocument.Parse(first).RootElement.Clone();
            Stage(view, "K").GetProperty("status").GetString().Should().Be("Done");
            Stage(view, "K").GetProperty("completionVersion").GetInt32().Should().Be(1);
            Stage(view, "T").GetProperty("status").GetString().Should().Be("Ready");
            (await OutboxCountAsync("ProjectStageCompleted", projectId.ToString())).Should().Be(1);
            (await WithDbAsync(db => db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM shared.outbox_messages WHERE type LIKE {"%ProjectStageCompleted"} AND payload->>'ProjectId' = {projectId.ToString()} AND payload->>'StageCode' = 'K' AND (payload->>'Version')::int = 1").SingleAsync()))
                .Should().Be(1);

            // CA 2: checklist só com a etapa em andamento; item de outra etapa, de etapa de outro projeto ou inexistente, 404 (IDOR).
            var t = StageId(view, "T");
            (await editor.SendAsync(Put($"{Stages(projectId)}/{t}/checklist/1", new { result = "Done", version = Version(view) }))).StatusCode.Should().Be(HttpStatusCode.Conflict);
            view = await OkAsync(await editor.SendAsync(Post($"{Stages(projectId)}/{t}/start", new { version = Version(view) })));
            (await editor.SendAsync(Put($"{Stages(projectId)}/{k}/checklist/1", new { result = "Done", version = Version(view) }))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await editor.SendAsync(Put($"{Stages(projectId)}/{t}/checklist/3", new { result = "Done", version = Version(view) }))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            var other = await ServiceProjectAsync(owner);
            (await editor.SendAsync(Put($"{Stages(other)}/{t}/checklist/1", new { result = "Done", version = Version(view) }))).StatusCode.Should().Be(HttpStatusCode.NotFound);

            // CA 3: checklist incompleto não conclui; marcado (Feito e Não se aplica), grava quem e quando.
            var incomplete = await editor.SendAsync(Post($"{Stages(projectId)}/{t}/complete", new { date = Iso(Today), version = Version(view) }));
            incomplete.StatusCode.Should().Be(HttpStatusCode.Conflict);
            Code(await JsonAsync(incomplete)).Should().Be("stage_checklist_incomplete");
            view = await OkAsync(await editor.SendAsync(Put($"{Stages(projectId)}/{t}/checklist/1", new { result = "Done", version = Version(view) })));
            view = await OkAsync(await editor.SendAsync(Put($"{Stages(projectId)}/{t}/checklist/2", new { result = "NotApplicable", version = Version(view) })));
            Stage(view, "T").GetProperty("checklistDone").GetInt32().Should().Be(2);
            Stage(view, "T").GetProperty("checklist")[0].GetProperty("markedByName").GetString().Should().NotBeNullOrEmpty();
            (await WithDbAsync(db => db.ProjectServiceStageChecks.AsNoTracking().Where(c => c.StageId == t).Select(c => c.MarkedAt).ToListAsync())).Should().NotContainNulls();

            // CA 5: "Exige aprovação": o aprovador que não é Proprietário conclui e a etapa aguarda (padrão Só o Proprietário); quem só edita não aprova.
            view = await OkAsync(await approver.SendAsync(Post($"{Stages(projectId)}/{t}/complete", new { date = Iso(Today), version = Version(view) })));
            Stage(view, "T").GetProperty("status").GetString().Should().Be("AwaitingApproval");
            (await editor.SendAsync(Post($"{Stages(projectId)}/{t}/approve", new { version = Version(view) }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // Autoaprovação nos três modos: Nunca e Só o Proprietário recusam quem concluiu (não é Proprietário); Qualquer aprovador aprova com o selo.
            foreach (var mode in new decimal?[] { 0m, 1m })
            {
                await SetStageSelfApprovalAsync(mode);
                var refused = await approver.SendAsync(Post($"{Stages(projectId)}/{t}/approve", new { version = Version(view) }));
                refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                (await refused.Content.ReadAsStringAsync()).Should().Contain(StageApprovalRules.SelfRefusal);
            }

            await SetStageSelfApprovalAsync(2m);
            view = await OkAsync(await approver.SendAsync(Post($"{Stages(projectId)}/{t}/approve", new { version = Version(view) })));
            Stage(view, "T").GetProperty("status").GetString().Should().Be("Done");
            Stage(view, "T").GetProperty("selfApproved").GetBoolean().Should().BeTrue();
            (await WithDbAsync(db => db.ProjectServiceStages.AsNoTracking().SingleAsync(s => s.Id == t))).ApprovedBy.Should().Be(approverId);
            await SetStageSelfApprovalAsync(null);

            // CA 6: Entrega exige projects.approve e a situação vista; conclui pelo RegisterDeliveryAsync (ProjectDelivered) e publica o evento da etapa.
            var g = StageId(view, "G");
            view = await OkAsync(await editor.SendAsync(Post($"{Stages(projectId)}/{g}/start", new { version = Version(view) })));
            var forbidden = await editor.SendAsync(Post($"{Stages(projectId)}/{g}/complete", new { date = Iso(Today), version = Version(view), expectedStatus = "InProgress" }));
            forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(forbidden)).Should().Be("forbidden");
            (await owner.SendAsync(Post($"{Stages(projectId)}/{g}/complete", new { date = Iso(Today), version = Version(view) }))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await owner.SendAsync(Post($"{Stages(projectId)}/{g}/complete", new { date = Iso(Today.AddDays(1)), version = Version(view), expectedStatus = "InProgress" }))).StatusCode
                .Should().Be(HttpStatusCode.UnprocessableEntity);
            view = await OkAsync(await owner.SendAsync(Post($"{Stages(projectId)}/{g}/complete", new { date = Iso(Today), version = Version(view), expectedStatus = "InProgress" })));
            Stage(view, "G").GetProperty("status").GetString().Should().Be("Done");
            (await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"))).GetProperty("status").GetString().Should().Be("Delivered");
            (await OutboxCountAsync("ProjectDelivered", projectId.ToString())).Should().Be(1);

            // CA 7: Aceite pelo RegisterAcceptanceAsync (TechnicalAcceptance, quem aceitou) e o evento da etapa.
            var a = StageId(view, "A");
            view = await OkAsync(await editor.SendAsync(Post($"{Stages(projectId)}/{a}/start", new { version = Version(view) })));
            (await editor.SendAsync(Post($"{Stages(projectId)}/{a}/complete", new { date = Iso(Today), version = Version(view), expectedStatus = "Delivered", acceptedByName = "Joana Prado" })))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await owner.SendAsync(Post($"{Stages(projectId)}/{a}/complete", new { date = Iso(Today), version = Version(view), expectedStatus = "Delivered" })))
                .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            view = await OkAsync(await owner.SendAsync(Post($"{Stages(projectId)}/{a}/complete",
                new { date = Iso(Today), version = Version(view), expectedStatus = "Delivered", acceptedByName = "Joana Prado", acceptedByRole = "Diretora" })));
            Stage(view, "A").GetProperty("status").GetString().Should().Be("Done");
            (await OutboxCountAsync("TechnicalAcceptance", projectId.ToString())).Should().Be(1);
            (await OutboxCountAsync("ProjectStageCompleted", projectId.ToString())).Should().Be(4);
            (await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"))).GetProperty("currentStage").GetProperty("code").GetString().Should().Be("A");

            // Versão velha em qualquer comando: 409.
            (await owner.SendAsync(Post($"{Stages(projectId)}/{k}/start", new { version = 1u }))).StatusCode.Should().Be(HttpStatusCode.Conflict);
            ownerId.Should().NotBeEmpty();
        }
        finally
        {
            await SetStageSelfApprovalAsync(null);
            await ServicesAsync(false);
        }
    }

    [Fact]
    public async Task Sem_o_recurso_comando_de_etapa_e_403_e_fabricacao_nao_muda()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        Guid projectId;
        Guid stageId;
        try
        {
            var item = await ServiceItemAsync();
            await TemplateAsync(item);
            var (quoteId, quote) = await QuoteAsync(owner, "Service", item, 1m);
            projectId = (await ApproveAsync(owner, quoteId, quote)).Project;
            stageId = StageId(await OkAsync(await owner.GetAsync(Stages(projectId))), "K");
        }
        finally
        {
            await ServicesAsync(false);
        }

        // CA 10: tenant sem o perfil Serviços (como a Konebras): 403 module_not_released, nada gravado.
        var refused = await owner.SendAsync(Post($"{Stages(projectId)}/{stageId}/start", new { version = 1u, expectedStatus = "Planned" }));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(refused)).Should().Be("module_not_released");
        (await WithDbAsync(db => db.ProjectServiceStages.AsNoTracking().SingleAsync(s => s.Id == stageId))).Status.Should().Be(ProjectStageStatus.Ready);
        (await owner.SendAsync(Put($"{Stages(projectId)}/{stageId}/checklist/1", new { result = "Done", version = 1u }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
