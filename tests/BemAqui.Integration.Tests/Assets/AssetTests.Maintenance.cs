using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Assets.Application;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Assets;

/// <summary>
/// PAT-03a: plano preventivo por dias e por horas (horímetro digitado), job diário (aviso, abertura única, execução dupla, dois tenants, módulo),
/// OS de manutenção (numeração MAN, Idempotency-Key, bem em manutenção e volta, checklist, conclusão, xmin, cancelamento), anexos, lista, pauta,
/// baixa recusada com OS aberta e histórico.
/// </summary>
public partial class AssetTests
{
    private const string Orders = "/api/v1/assets/maintenance-orders";

    private static string Day(int offset) => BemAqui.BuildingBlocks.Time.BusinessTime.Today(TimeProvider.System).AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<JsonElement> PlanAsync(HttpClient client, Guid asset, object input) => await CreatedAsync(await client.PostAsJsonAsync($"{Base}/{asset}/plans", input));

    private static async Task<HttpResponseMessage> PostOrderAsync(HttpClient client, object input, Guid? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Orders) { Content = JsonContent.Create(input) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> OrderAsync(HttpClient client, object input) => await CreatedAsync(await PostOrderAsync(client, input));

    private static async Task<JsonElement> StartAsync(HttpClient client, JsonElement order) =>
        await OkAsync(await client.PostAsJsonAsync($"{Orders}/{order.GetProperty("id").GetGuid()}/start", new { version = order.GetProperty("version").GetUInt32() }));

    private static async Task<string> AssetStatusAsync(HttpClient client, Guid id) => (await client.GetFromJsonAsync<JsonElement>($"{Base}/{id}")).GetProperty("status").GetString()!;

    private async Task<int> RunDueJobAsync(Tenant tenant)
    {
        var job = ActivatorUtilities.CreateInstance<MaintenanceDueJob>(Factory.Services);
        await job.ExecuteAsync(tenant.Id, new MaintenanceDueArgs(), CancellationToken.None);
        await using var scope = await ScopeOfAsync(tenant);
        return await scope.ServiceProvider.GetRequiredService<IAssetsDb>().MaintenanceOrders.CountAsync();
    }

    private async Task<AsyncServiceScope> ScopeOfAsync(Tenant tenant)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task SetAssetsReleasedAsync(bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "assets")
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>CA 1, 9 e 13: o plano por dias calcula a próxima data; a ficha, a aba e a leitura da etiqueta mostram; o plano é lido pela rota do bem.</summary>
    [Fact]
    public async Task Plano_por_dias_calcula_a_proxima_data_e_a_ficha_mostra_a_proxima_preventiva()
    {
        var supervisor = await SupervisorAsync();
        var supplier = await SupplierAsync("Manutenção Industrial");
        var id = (await CreateAsync(supervisor, Input(Unique("Torno CNC")))).GetProperty("id").GetGuid();
        var other = (await CreateAsync(supervisor, Input(Unique("Prensa")))).GetProperty("id").GetGuid();

        var plan = await PlanAsync(supervisor, id, new
        {
            description = "Lubrificação geral", frequencyType = "Days", frequencyValue = 30, defaultSupplierPartyId = supplier,
            checklist = new[] { "Trocar o óleo", " ", "Limpar o filtro" }, lastDoneAt = Day(-10),
        });
        plan.GetProperty("nextDueAt").GetString().Should().Be(Day(20), "última execução + 30 dias");
        plan.GetProperty("leadDays").GetInt32().Should().Be(7, "a antecedência padrão");
        plan.GetProperty("checklist").EnumerateArray().Select(c => c.GetString()).Should().Equal("Trocar o óleo", "Limpar o filtro");
        plan.GetProperty("defaultSupplierName").GetString().Should().StartWith("Manutenção Industrial");
        plan.GetProperty("overdue").GetBoolean().Should().BeFalse();
        var planId = plan.GetProperty("id").GetGuid();

        (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}")).GetProperty("nextMaintenanceOn").GetString().Should().Be(Day(20), "\"Próxima preventiva\" na ficha");
        (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/card")).GetProperty("nextMaintenanceDate").GetString().Should().Be(Day(20), "a ficha resumida da etiqueta");
        var tab = await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/maintenance");
        tab.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Should().Equal(planId);
        (await supervisor.GetAsync($"{Base}/{other}/plans/{planId}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR: o plano é do outro bem");

        var missing = await supervisor.PostAsJsonAsync($"{Base}/{id}/plans", new { description = "Sem data", frequencyType = "Days", frequencyValue = 30 });
        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o plano por dias exige a data da última execução");
        var viewer = await ActorAsync("assets:view,create");
        (await viewer.PostAsJsonAsync($"{Base}/{id}/plans", new { description = "Sem permissão", frequencyType = "Days", frequencyValue = 30, lastDoneAt = Day(0) }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "plano com assets.edit");

        // Editar: periodicidade nova conta da mesma última execução; versão antiga é 409.
        var version = plan.GetProperty("version").GetUInt32();
        var edited = await OkAsync(await supervisor.PutAsJsonAsync($"{Base}/{id}/plans/{planId}", new
        {
            description = "Lubrificação geral", frequencyType = "Days", frequencyValue = 15, lastDoneAt = Day(-10), version,
        }));
        edited.GetProperty("nextDueAt").GetString().Should().Be(Day(5));
        var stale = await supervisor.PutAsJsonAsync($"{Base}/{id}/plans/{planId}", new { description = "X", frequencyType = "Days", frequencyValue = 15, lastDoneAt = Day(-10), version });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(stale)).Should().Be("concurrency_conflict");
    }

    /// <summary>CA 2: leitura menor ou igual é 422 com o valor anterior; a previsão do plano por horas sai da média de uso das últimas leituras.</summary>
    [Fact]
    public async Task Horimetro_menor_e_recusado_e_a_previsao_vem_da_media_das_leituras()
    {
        var supervisor = await SupervisorAsync();
        var id = (await CreateAsync(supervisor, Input(Unique("Compressor")))).GetProperty("id").GetGuid();
        var plan = await PlanAsync(supervisor, id, new { description = "Troca do óleo", frequencyType = "Hours", frequencyValue = 250, lastDoneMeter = 1000m });
        plan.GetProperty("nextDueMeter").GetDecimal().Should().Be(1250m);
        plan.GetProperty("nextDueAt").ValueKind.Should().Be(JsonValueKind.Null, "sem leituras, sem estimativa");

        await CreatedAsync(await supervisor.PostAsJsonAsync($"{Base}/{id}/meter-readings", new { value = 1000m, readOn = Day(-10) }));
        var second = await CreatedAsync(await supervisor.PostAsJsonAsync($"{Base}/{id}/meter-readings", new { value = 1100m, readOn = Day(0) }));
        second.GetProperty("plans")[0].GetProperty("nextDueAt").GetString().Should().Be(Day(15), "10 h por dia: faltam 150 h, 15 dias");

        var lower = await supervisor.PostAsJsonAsync($"{Base}/{id}/meter-readings", new { value = 1050m });
        lower.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(lower);
        Code(problem).Should().Be(MaintenanceService.MeterNotGreaterCode);
        problem.GetProperty("detail").GetString().Should().Be("A leitura precisa ser maior que a anterior (1.100).");
        (await supervisor.PostAsJsonAsync($"{Base}/{id}/meter-readings", new { value = 1100m })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "igual também não");

        (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/meter-readings")).EnumerateArray().Select(r => r.GetProperty("value").GetDecimal()).Should().Equal(1100m, 1000m);
        var tab = await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/maintenance");
        tab.GetProperty("lastReading").GetProperty("value").GetDecimal().Should().Be(1100m);
        tab.GetProperty("plans")[0].GetProperty("currentMeter").GetDecimal().Should().Be(1100m);
    }

    /// <summary>
    /// CA 3: na antecedência, um aviso por plano e data a quem tem assets.edit; no vencimento, uma OS preventiva com o checklist, uma vez só
    /// (execução dupla); o job de um tenant não toca no outro; sem o módulo liberado, nada.
    /// </summary>
    [Fact]
    public async Task Job_avisa_na_antecedencia_abre_a_OS_uma_vez_e_respeita_o_tenant_e_o_modulo()
    {
        var supervisor = await SupervisorAsync();
        var soon = (await CreateAsync(supervisor, Input(Unique("Guilhotina")))).GetProperty("id").GetGuid();
        var due = (await CreateAsync(supervisor, Input(Unique("Dobradeira")))).GetProperty("id").GetGuid();
        var soonPlan = (await PlanAsync(supervisor, soon, new { description = "Revisão", frequencyType = "Days", frequencyValue = 30, lastDoneAt = Day(-27) })).GetProperty("id").GetGuid();
        var duePlan = (await PlanAsync(supervisor, due, new { description = "Ajuste", frequencyType = "Days", frequencyValue = 30, lastDoneAt = Day(-30), checklist = new[] { "Apertar", "Medir" } }))
            .GetProperty("id").GetGuid();

        // Tenant B: um plano vencido, gravado direto no banco dele.
        Guid planB;
        await using (var scopeB = await ScopeOfAsync(tenancy.TenantB))
        {
            var dbB = scopeB.ServiceProvider.GetRequiredService<IAssetsDb>();
            var assetB = new Asset { Name = "Máquina B", Type = BemAqui.Assets.Contracts.AssetType.Machine, CreatedAt = DateTimeOffset.UtcNow };
            assetB.AssignTag("PAT-B" + Guid.NewGuid().ToString("N")[..6]);
            var plan = new MaintenancePlan { AssetId = assetB.Id, Description = "Plano B", FrequencyType = BemAqui.Assets.Contracts.MaintenanceFrequency.Days, FrequencyValue = 10, LastDoneAt = DateOnly.Parse(Day(-10), CultureInfo.InvariantCulture), CreatedAt = DateTimeOffset.UtcNow };
            plan.Recalculate([]);
            dbB.Assets.Add(assetB);
            dbB.MaintenancePlans.Add(plan);
            await dbB.SaveChangesAsync();
            planB = plan.Id;
        }

        int CountIn(IAssetsDb db, Guid plan) => db.MaintenanceOrders.Count(o => o.PlanId == plan);
        await using (var scopeB = await ScopeOfAsync(tenancy.TenantB))
        {
            CountIn(scopeB.ServiceProvider.GetRequiredService<IAssetsDb>(), planB).Should().Be(0);
        }

        await RunDueJobAsync(tenancy.TenantA);
        await RunDueJobAsync(tenancy.TenantA);

        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAssetsDb>();
            var orders = await db.MaintenanceOrders.AsNoTracking().Where(o => o.PlanId == duePlan).ToListAsync();
            orders.Should().ContainSingle("execução dupla não duplica a OS do vencimento");
            orders[0].Status.Should().Be(BemAqui.Assets.Contracts.MaintenanceOrderStatus.Open);
            orders[0].Type.Should().Be(BemAqui.Assets.Contracts.MaintenanceOrderType.Preventive);
            orders[0].Number.Should().MatchRegex(@"^MAN-\d{4}-\d{4}$");
            (await db.MaintenanceOrderChecklist.AsNoTracking().Where(i => i.OrderId == orders[0].Id).OrderBy(i => i.Sequence).Select(i => i.Text).ToListAsync()).Should().Equal("Apertar", "Medir");
            CountIn(db, soonPlan).Should().Be(0, "na antecedência só avisa");

            var notices = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().Notifications.AsNoTracking()
                .Where(n => n.Type == MaintenanceDueNotifier.DueType && n.DedupKey == $"maint-due:{soonPlan}:{Day(3)}").ToListAsync();
            notices.Should().NotBeEmpty();
            notices.GroupBy(n => n.UserId).Should().OnlyContain(g => g.Count() == 1, "um aviso por pessoa, plano e data");
            notices[0].Title.Should().StartWith("Preventiva Revisão de PAT-").And.EndWith($"vence em {DateOnly.Parse(Day(3), CultureInfo.InvariantCulture):dd/MM/yyyy}.");
            notices[0].Link.Should().Be($"/app/patrimonio/bens/{soon}?aba=manutencao&plano={soonPlan}");
        }

        await using (var scopeB = await ScopeOfAsync(tenancy.TenantB))
        {
            CountIn(scopeB.ServiceProvider.GetRequiredService<IAssetsDb>(), planB).Should().Be(0, "o job do tenant A não toca no B");
        }

        await RunDueJobAsync(tenancy.TenantB);
        await using (var scopeB = await ScopeOfAsync(tenancy.TenantB))
        {
            CountIn(scopeB.ServiceProvider.GetRequiredService<IAssetsDb>(), planB).Should().Be(1, "o tenant B abre a dele");
        }

        var third = (await CreateAsync(supervisor, Input(Unique("Serra")))).GetProperty("id").GetGuid();
        var thirdPlan = (await PlanAsync(supervisor, third, new { description = "Afiação", frequencyType = "Days", frequencyValue = 7, lastDoneAt = Day(-8) })).GetProperty("id").GetGuid();
        await SetAssetsReleasedAsync(false);
        try
        {
            await using var scope = await ScopeAsync();
            (await scope.ServiceProvider.GetRequiredService<MaintenanceDueNotifier>().RunAsync()).Should().Be(0, "sem o módulo liberado o job não faz nada");
            CountIn(scope.ServiceProvider.GetRequiredService<IAssetsDb>(), thirdPlan).Should().Be(0);
        }
        finally
        {
            await SetAssetsReleasedAsync(true);
        }
    }

    /// <summary>
    /// CA 4, 5 e 7: corretiva com máquina parada nasce Open com MAN e o bem em manutenção; exige Idempotency-Key e a mesma chave devolve a mesma OS;
    /// iniciar põe o bem em manutenção; cancelar com motivo solta o bem só quando nenhuma outra OS o segura; abrir exige assets.create.
    /// </summary>
    [Fact]
    public async Task OS_corretiva_com_maquina_parada_poe_o_bem_em_manutencao_e_o_cancelamento_solta()
    {
        var supervisor = await SupervisorAsync();
        var id = (await CreateAsync(supervisor, Input(Unique("Injetora")))).GetProperty("id").GetGuid();
        var input = new { assetId = id, type = "Corrective", problem = "Vazamento de óleo no cilindro", machineStopped = true };

        var noKey = await supervisor.PostAsJsonAsync(Orders, input);
        noKey.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(noKey)).Should().Be("idempotency_key_required");

        var key = Guid.NewGuid();
        var first = await CreatedAsync(await PostOrderAsync(supervisor, input, key));
        first.GetProperty("number").GetString().Should().MatchRegex($@"^MAN-{DateTime.Today.Year}-\d{{4}}$");
        first.GetProperty("status").GetString().Should().Be("Open");
        first.GetProperty("assetStatus").GetString().Should().Be("Maintenance", "máquina parada: em manutenção na hora");
        var again = await CreatedAsync(await PostOrderAsync(supervisor, input, key));
        again.GetProperty("id").GetGuid().Should().Be(first.GetProperty("id").GetGuid(), "a mesma chave devolve a mesma OS");

        var second = await OrderAsync(supervisor, new { assetId = id, type = "Corrective", problem = "Ruído no motor" });
        await StartAsync(supervisor, second);
        (await AssetStatusAsync(supervisor, id)).Should().Be("Maintenance");

        var firstId = first.GetProperty("id").GetGuid();
        (await supervisor.PostAsJsonAsync($"{Orders}/{firstId}/cancel", new { reason = " " })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o motivo é obrigatório");
        var cancelled = await OkAsync(await supervisor.PostAsJsonAsync($"{Orders}/{firstId}/cancel", new { reason = "Aberta em duplicidade" }));
        cancelled.GetProperty("status").GetString().Should().Be("Cancelled");
        cancelled.GetProperty("assetStatus").GetString().Should().Be("Maintenance", "a outra OS em andamento ainda segura o bem");

        var secondId = second.GetProperty("id").GetGuid();
        var current = await supervisor.GetFromJsonAsync<JsonElement>($"{Orders}/{secondId}");
        (await OkAsync(await supervisor.PostAsJsonAsync($"{Orders}/{secondId}/cancel", new { reason = "Peça em falta", version = current.GetProperty("version").GetUInt32() })))
            .GetProperty("assetStatus").GetString().Should().Be("Active", "sem OS segurando, o bem volta a ativo");
        (await supervisor.PostAsJsonAsync($"{Orders}/{secondId}/start", new { })).StatusCode.Should().Be(HttpStatusCode.Conflict, "a cancelada não é iniciada");

        var viewer = await ActorAsync("assets:view");
        (await PostOrderAsync(viewer, input)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "abrir exige assets.create");
        var opener = await ActorAsync("assets:view,create");
        var opened = await OrderAsync(opener, new { assetId = id, type = "Corrective", problem = "Correia gasta" });
        (await opener.PostAsJsonAsync($"{Orders}/{opened.GetProperty("id").GetGuid()}/start", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "iniciar exige assets.edit");
    }

    /// <summary>
    /// CA 6, 8 e 14: concluir exige o checklist completo e o que foi feito; a preventiva do plano grava a última execução e recalcula da conclusão;
    /// versão antiga é 409 concurrency_conflict; duas conclusões juntas, só uma grava; o histórico guarda quem e o antes e depois.
    /// </summary>
    [Fact]
    public async Task Concluir_exige_o_checklist_recalcula_o_plano_da_conclusao_e_so_uma_conclusao_grava()
    {
        var supervisor = await SupervisorAsync();
        var id = (await CreateAsync(supervisor, Input(Unique("Fresadora")))).GetProperty("id").GetGuid();
        var plan = await PlanAsync(supervisor, id, new { description = "Preventiva mensal", frequencyType = "Days", frequencyValue = 30, lastDoneAt = Day(-40), checklist = new[] { "Óleo", "Correias" } });
        var planId = plan.GetProperty("id").GetGuid();
        plan.GetProperty("overdue").GetBoolean().Should().BeTrue("o vencimento passou: chip Vencida");

        var order = await OrderAsync(supervisor, new { assetId = id, type = "Preventive", problem = "Preventiva mensal", planId });
        order.GetProperty("checklist").GetArrayLength().Should().Be(2, "o checklist vem do plano");
        (await PostOrderAsync(supervisor, new { assetId = id, type = "Preventive", problem = "De novo", planId })).StatusCode.Should().Be(HttpStatusCode.Conflict, "o plano já tem OS aberta");
        var orderId = order.GetProperty("id").GetGuid();
        var openVersion = order.GetProperty("version").GetUInt32();
        var items = order.GetProperty("checklist").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        (await supervisor.PutAsJsonAsync($"{Orders}/{orderId}/checklist/{items[0]}", new { result = "Done" })).StatusCode.Should().Be(HttpStatusCode.Conflict, "marca só em andamento");

        var started = await StartAsync(supervisor, order);
        started.GetProperty("startedAt").ValueKind.Should().Be(JsonValueKind.String);
        var stale = await supervisor.PostAsJsonAsync($"{Orders}/{orderId}/cancel", new { reason = "Versão antiga", version = openVersion });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 8: a versão lida antes do início");
        Code(await JsonAsync(stale)).Should().Be("concurrency_conflict");

        var incomplete = await supervisor.PostAsJsonAsync($"{Orders}/{orderId}/complete", new { workDone = "Troquei o óleo" });
        incomplete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(incomplete)).GetProperty("detail").GetString().Should().Be("Marque os 2 itens que faltam.");
        await OkAsync(await supervisor.PutAsJsonAsync($"{Orders}/{orderId}/checklist/{items[0]}", new { result = "Done" }));
        var marked = await OkAsync(await supervisor.PutAsJsonAsync($"{Orders}/{orderId}/checklist/{items[1]}", new { result = "NotApplicable" }));
        marked.GetProperty("missing").GetInt32().Should().Be(0);
        var other = (await CreateAsync(supervisor, Input(Unique("Outra")))).GetProperty("id").GetGuid();
        var foreign = await OrderAsync(supervisor, new { assetId = other, type = "Corrective", problem = "Outro problema" });
        (await supervisor.PutAsJsonAsync($"{Orders}/{foreign.GetProperty("id").GetGuid()}/checklist/{items[0]}", new { result = "Done" })).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "o item é da outra OS");

        var version = marked.GetProperty("version").GetUInt32();
        var results = await Task.WhenAll(
            supervisor.PostAsJsonAsync($"{Orders}/{orderId}/complete", new { workDone = "Troquei o óleo e ajustei as correias", version }),
            (await SupervisorAsync()).PostAsJsonAsync($"{Orders}/{orderId}/complete", new { workDone = "Concluí junto", version }));
        results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict], "só uma conclusão grava (xmin)");
        var done = await JsonAsync(results.Single(r => r.StatusCode == HttpStatusCode.OK));
        done.GetProperty("status").GetString().Should().Be("Done");
        done.GetProperty("doneAt").ValueKind.Should().Be(JsonValueKind.String);
        done.GetProperty("assetStatus").GetString().Should().Be("Active");

        var after = await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/plans/{planId}");
        after.GetProperty("lastDoneAt").GetString().Should().Be(Day(0), "a última execução é a data da conclusão");
        after.GetProperty("nextDueAt").GetString().Should().Be(Day(30), "o próximo vencimento conta da conclusão");
        after.GetProperty("overdue").GetBoolean().Should().BeFalse();
        (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/card")).GetProperty("lastMaintenanceDate").GetString().Should().Be(Day(0));

        await using var scope = await ScopeAsync();
        var audit = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries.AsNoTracking()
            .Where(a => (a.Entity == "MaintenanceOrder" && a.EntityId == orderId.ToString()) || (a.Entity == "MaintenancePlan" && a.EntityId == planId.ToString())).ToListAsync();
        audit.Where(a => a.Entity == "MaintenanceOrder").Select(a => a.Action).Should().Contain(["Created", "Updated"], "CA 14: abertura, início e conclusão");
        audit.Where(a => a.Entity == "MaintenancePlan" && a.Action == "Updated").Should().NotBeEmpty("a conclusão muda o plano");
        audit.Should().AllSatisfy(a => a.UserId.Should().NotBeNull());
        (await (await ActorAsync("owner")).DeleteAsync($"{Base}/{id}")).StatusCode.Should().Be(HttpStatusCode.Conflict, "bem com manutenção tem histórico");

    }

    /// <summary>CA 6 e 13: a foto da OS sai sem EXIF/GPS; a rota genérica não grava; o anexo é lido só pela OS dele.</summary>
    [Fact]
    public async Task Foto_da_OS_sai_sem_EXIF_e_o_anexo_e_da_OS_da_rota()
    {
        var supervisor = await SupervisorAsync();
        var id = (await CreateAsync(supervisor, Input(Unique("Calandra")))).GetProperty("id").GetGuid();
        var order = (await OrderAsync(supervisor, new { assetId = id, type = "Corrective", problem = "Rolamento" })).GetProperty("id").GetGuid();
        var other = (await OrderAsync(supervisor, new { assetId = id, type = "Corrective", problem = "Outro" })).GetProperty("id").GetGuid();

        var photo = await CreatedAsync(await supervisor.PostAsync($"{Orders}/{order}/attachments", File("antes.jpg", JpegWithGps(), "image/jpeg")));
        var photoId = photo.GetProperty("id").GetGuid();
        var stored = await (await supervisor.GetAsync($"{Orders}/{order}/attachments/{photoId}/content")).Content.ReadAsByteArrayAsync();
        Contains(stored, GpsMarker).Should().BeFalse("a foto sai sem as coordenadas");
        await CreatedAsync(await supervisor.PostAsync($"{Orders}/{order}/attachments", File("relatorio.pdf", "%PDF-1.4\n%%EOF"u8.ToArray(), "application/pdf")));
        (await supervisor.GetFromJsonAsync<JsonElement>($"{Orders}/{order}/attachments")).GetProperty("files").GetArrayLength().Should().Be(2);

        (await supervisor.GetAsync($"{Orders}/{other}/attachments/{photoId}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR: o anexo é da outra OS");
        (await supervisor.PostAsync($"/api/v1/attachments/{MaintenanceService.AttachmentOwner}/{order}", File("antes.jpg", JpegWithGps(), "image/jpeg"))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "a rota genérica não grava anexo da OS");
        (await (await ActorAsync("assets:view,create")).PostAsync($"{Orders}/{order}/attachments", File("antes.jpg", JpegWithGps(), "image/jpeg"))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "anexar exige assets.edit");
    }

    /// <summary>CA 9, 10 e 11: aba do bem da mais recente; lista com filtros e preventivas vencidas e a vencer; a pauta com máquinas paradas e vencidas.</summary>
    [Fact]
    public async Task Lista_filtra_por_bem_e_tipo_e_a_pauta_mostra_paradas_e_vencidas()
    {
        var supervisor = await SupervisorAsync();
        var center = await CenterAsync("Pintura");
        var id = (await CreateAsync(supervisor, Input(Unique("Cabine de pintura"), center: center))).GetProperty("id").GetGuid();
        var overduePlan = (await PlanAsync(supervisor, id, new { description = "Filtros", frequencyType = "Days", frequencyValue = 10, lastDoneAt = Day(-15) })).GetProperty("id").GetGuid();
        var soonPlan = (await PlanAsync(supervisor, id, new { description = "Exaustor", frequencyType = "Days", frequencyValue = 30, lastDoneAt = Day(-10) })).GetProperty("id").GetGuid();
        var stopped = await OrderAsync(supervisor, new { assetId = id, type = "Corrective", problem = "Pistola entupida", machineStopped = true });
        var latest = await OrderAsync(supervisor, new { assetId = id, type = "Preventive", problem = "Avulsa" });

        var tab = await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/maintenance");
        tab.GetProperty("orders").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).Should().Equal(latest.GetProperty("id").GetGuid(), stopped.GetProperty("id").GetGuid());

        var list = await supervisor.GetFromJsonAsync<JsonElement>($"{Orders}?assetId={id}");
        list.GetProperty("orders").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).First().Should().Be(stopped.GetProperty("id").GetGuid(), "a de máquina parada primeiro");
        list.GetProperty("duePlans").EnumerateArray().Select(p => (p.GetProperty("id").GetGuid(), p.GetProperty("overdue").GetBoolean())).Should().Equal((overduePlan, true), (soonPlan, false));
        var corrective = await supervisor.GetFromJsonAsync<JsonElement>($"{Orders}?workCenterId={center}&type=Corrective");
        corrective.GetProperty("orders").EnumerateArray().Select(o => o.GetProperty("type").GetString()).Should().OnlyContain(t => t == "Corrective").And.HaveCount(1);
        corrective.GetProperty("duePlans").GetArrayLength().Should().Be(0, "a corretiva não lista preventivas");
        (await supervisor.GetFromJsonAsync<JsonElement>($"{Orders}?assetId={id}&status=Done")).GetProperty("orders").GetArrayLength().Should().Be(0);

        var agenda = await supervisor.GetFromJsonAsync<JsonElement>("/api/v1/me/agenda");
        var items = agenda.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("category").GetString()!);
        items[AssetsAgendaSource.MachinesStopped].GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("link").GetString())
            .Should().Contain(MaintenanceService.OrderLink + stopped.GetProperty("id").GetGuid(), "leva à OS");
        items[AssetsAgendaSource.MaintenanceOverdue].GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("link").GetString())
            .Should().Contain($"/app/patrimonio/bens/{id}?aba=manutencao&plano={overduePlan}", "leva ao plano");
        var reader = await ActorAsync("assets:view,create");
        (await reader.GetFromJsonAsync<JsonElement>("/api/v1/me/agenda")).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("category").GetString())
            .Should().NotContain([AssetsAgendaSource.MachinesStopped, AssetsAgendaSource.MaintenanceOverdue], "a pauta da manutenção é de quem tem assets.edit");
    }

    /// <summary>CA 12: com OS aberta, a baixa é 409 com o link; baixado, os planos ficam inativos e não voltam sozinhos na reativação.</summary>
    [Fact]
    public async Task Baixa_com_OS_aberta_e_recusada_e_baixado_os_planos_ficam_inativos()
    {
        var supervisor = await SupervisorAsync();
        var id = (await CreateAsync(supervisor, Input(Unique("Empilhadeira"), type: "Vehicle"))).GetProperty("id").GetGuid();
        var planId = (await PlanAsync(supervisor, id, new { description = "Revisão", frequencyType = "Days", frequencyValue = 90, lastDoneAt = Day(-1) })).GetProperty("id").GetGuid();
        var order = await OrderAsync(supervisor, new { assetId = id, type = "Corrective", problem = "Pneu furado" });

        var refused = await supervisor.PostAsJsonAsync($"{Base}/{id}/dispose", new { disposalReason = "Scrap" });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be(MaintenanceService.OpenOrdersCode);
        problem.GetProperty("detail").GetString().Should().Be("Conclua ou cancele as OS de manutenção abertas.");
        problem.GetRawText().Should().Contain(MaintenanceService.OrderLink + order.GetProperty("id").GetGuid(), "com o link para elas");

        await OkAsync(await supervisor.PostAsJsonAsync($"{Orders}/{order.GetProperty("id").GetGuid()}/cancel", new { reason = "Resolvido na rua" }));
        await OkAsync(await supervisor.PostAsJsonAsync($"{Base}/{id}/dispose", new { disposalReason = "Scrap" }));
        var plan = await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/plans/{planId}");
        plan.GetProperty("isActive").GetBoolean().Should().BeFalse("baixado o bem, o plano fica inativo");
        (await PostOrderAsync(supervisor, new { assetId = id, type = "Corrective", problem = "Depois da baixa" })).StatusCode.Should().Be(HttpStatusCode.Conflict, "bem baixado não recebe OS");
        var reactivate = await supervisor.PutAsJsonAsync($"{Base}/{id}/plans/{planId}", new { description = "Revisão", frequencyType = "Days", frequencyValue = 90, lastDoneAt = Day(-1), isActive = true });
        reactivate.StatusCode.Should().Be(HttpStatusCode.Conflict, "o plano do bem baixado não é reativado");

        await OkAsync(await supervisor.PostAsJsonAsync($"{Base}/{id}/reactivate", new { reason = "Baixa por engano" }));
        (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/plans/{planId}")).GetProperty("isActive").GetBoolean().Should().BeFalse("só volta se alguém reativar");
        var version = (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/plans/{planId}")).GetProperty("version").GetUInt32();
        (await OkAsync(await supervisor.PutAsJsonAsync($"{Base}/{id}/plans/{planId}", new { description = "Revisão", frequencyType = "Days", frequencyValue = 90, lastDoneAt = Day(-1), isActive = true, version })))
            .GetProperty("isActive").GetBoolean().Should().BeTrue();
    }
}
