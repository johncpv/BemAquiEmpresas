using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Assets.Application;
using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Assets;

/// <summary>
/// PAT-02: movimentação com antes e depois, imutável e na ordem; lote atômico; filtro do responsável desligado; baixa (venda com o título a
/// receber só pré-preenchido); reativação; IDOR pela rota do bem; histórico com usuário, data e IP; arquivo do titular.
/// </summary>
public partial class AssetTests
{
    private static string Today => BemAqui.BuildingBlocks.Time.BusinessTime.Today(TimeProvider.System).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static async Task<JsonElement> MoveAsync(HttpClient client, Guid id, Guid? site, Guid? responsible, Guid? center, string reason = "Mudança de setor", string? date = null) =>
        await OkAsync(await client.PostAsJsonAsync($"{Base}/{id}/movements", new { siteId = site, responsibleEmployeeId = responsible, workCenterId = center, movedOn = date, reason }));

    private async Task<int> CountAsync(Func<IServiceProvider, Task<int>> count)
    {
        await using var scope = await ScopeAsync();
        return await count(scope.ServiceProvider);
    }

    /// <summary>CA 1, 2 e 10: o movimento nasce com antes e depois e o bem mostra os novos; nada mudou é 422; o histórico guarda usuário e data (o IP é do interceptor).</summary>
    [Fact]
    public async Task Movimentacao_grava_antes_e_depois_o_bem_mostra_os_novos_e_nada_mudou_e_422()
    {
        var supervisor = await SupervisorAsync();
        var (siteA, siteB) = (await SiteAsync(supervisor), await SiteAsync(supervisor));
        var (ana, bruno) = (await EmployeeAsync("Ana"), await EmployeeAsync("Bruno"));
        var center = await CenterAsync("Solda");
        var asset = await CreateAsync(supervisor, Input(Unique("Máquina de solda"), site: siteA, responsible: ana, center: center));
        var id = asset.GetProperty("id").GetGuid();

        var moved = await MoveAsync(supervisor, id, siteB, bruno, center);
        var movement = moved.GetProperty("movement");
        movement.GetProperty("kind").GetString().Should().Be("Transfer");
        movement.GetProperty("movedOn").GetString().Should().Be(Today, "a data padrão é hoje");
        movement.GetProperty("from").GetProperty("siteId").GetGuid().Should().Be(siteA);
        movement.GetProperty("from").GetProperty("responsibleName").GetString().Should().StartWith("Ana");
        movement.GetProperty("to").GetProperty("siteId").GetGuid().Should().Be(siteB);
        movement.GetProperty("to").GetProperty("responsibleEmployeeId").GetGuid().Should().Be(bruno);
        movement.GetProperty("reason").GetString().Should().Be("Mudança de setor");
        movement.GetProperty("createdBy").ValueKind.Should().Be(JsonValueKind.String);
        moved.GetProperty("asset").GetProperty("siteId").GetGuid().Should().Be(siteB, "o bem mostra o novo local na mesma transação");
        (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}")).GetProperty("responsibleEmployeeId").GetGuid().Should().Be(bruno);

        var same = await supervisor.PostAsJsonAsync($"{Base}/{id}/movements", new { siteId = siteB, responsibleEmployeeId = bruno, workCenterId = center, reason = "De novo" });
        same.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(same);
        Code(problem).Should().Be(AssetMovementService.NothingChangedCode);
        problem.GetProperty("detail").GetString().Should().Be("Informe pelo menos um local, responsável ou centro diferente do atual.");

        var future = await supervisor.PostAsJsonAsync($"{Base}/{id}/movements", new { siteId = siteA, movedOn = DateTime.Today.AddDays(3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), reason = "Volta" });
        future.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await supervisor.PostAsJsonAsync($"{Base}/{id}/movements", new { siteId = siteA, reason = new string('x', 201) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await supervisor.PostAsJsonAsync($"{Base}/{id}/movements", new { siteId = siteA, reason = " " })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Na aba Dados, local, responsável e centro só mudam pela movimentação (o histórico fica completo).
        var detail = await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}");
        var edit = await supervisor.PutAsJsonAsync($"{Base}/{id}", Input(detail.GetProperty("name").GetString()!, site: siteA, responsible: bruno, center: center, version: detail.GetProperty("version").GetUInt32()));
        edit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(edit)).Should().Be(AssetService.PlacementByMovementCode);

        var movementId = movement.GetProperty("id").GetGuid();
        await using var scope = await ScopeAsync();
        var audit = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries.AsNoTracking()
            .Where(a => (a.Entity == "AssetMovement" && a.EntityId == movementId.ToString()) || (a.Entity == "Asset" && a.EntityId == id.ToString() && a.Action == "Updated")).ToListAsync();
        audit.Select(a => a.Entity).Should().Contain(["AssetMovement", "Asset"], "o movimento e a troca no bem ficam no histórico");
        // O IP vem do interceptor de auditoria (AuditingTests); o TestServer não tem IP de socket, então o X-Forwarded-For não é confiado aqui.
        audit.Should().AllSatisfy(a => a.UserId.Should().NotBeNull("usuário e data (CA 10)"));
    }

    /// <summary>CA 3 e 9: do mais recente ao mais antigo; o movimento é lido pela rota do bem (o de outro bem é 404); sem assets.edit, 403.</summary>
    [Fact]
    public async Task Movimentos_do_mais_recente_ao_mais_antigo_e_o_de_outro_bem_e_404()
    {
        var supervisor = await SupervisorAsync();
        var (siteA, siteB) = (await SiteAsync(supervisor), await SiteAsync(supervisor));
        var id = (await CreateAsync(supervisor, Input(Unique("Paleteira"), type: "Tool"))).GetProperty("id").GetGuid();
        var other = (await CreateAsync(supervisor, Input(Unique("Carrinho"), type: "Tool"))).GetProperty("id").GetGuid();
        (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/movements")).GetArrayLength().Should().Be(0, "o vazio da aba");

        var first = (await MoveAsync(supervisor, id, siteA, null, null, "Chegou", DateTime.Today.AddDays(-5).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))).GetProperty("movement").GetProperty("id").GetGuid();
        var second = (await MoveAsync(supervisor, id, siteB, null, null, "Expedição")).GetProperty("movement").GetProperty("id").GetGuid();
        var before = await supervisor.PostAsJsonAsync($"{Base}/{id}/movements", new { siteId = siteA, movedOn = DateTime.Today.AddDays(-10).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), reason = "Retroativo" });
        before.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a data não volta para antes do último movimento");

        var list = await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/movements");
        list.EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).Should().Equal(second, first);
        (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/movements/{first}")).GetProperty("reason").GetString().Should().Be("Chegou");
        (await supervisor.GetAsync($"{Base}/{other}/movements/{first}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR: o movimento é do outro bem");

        var viewer = await ActorAsync("assets:view");
        (await viewer.GetAsync($"{Base}/{id}/movements")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync($"{Base}/{id}/movements", new { siteId = siteA, reason = "Sem permissão" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"{Base}/{id}/dispose", new { disposalReason = "Scrap" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var delete = await (await ActorAsync("owner")).DeleteAsync($"{Base}/{id}");
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict, "bem com movimentação tem histórico");
        Code(await JsonAsync(delete)).Should().Be(AssetService.HasHistoryCode);
    }

    /// <summary>CA 4: um movimento por bem na mesma transação; um bem baixado na seleção faz o lote falhar inteiro, com a lista do que impede.</summary>
    [Fact]
    public async Task Transferencia_em_lote_e_atomica_e_o_baixado_impede_tudo()
    {
        var supervisor = await SupervisorAsync();
        var (origin, target) = (await SiteAsync(supervisor), await SiteAsync(supervisor));
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await CreateAsync(supervisor, Input(Unique("Cadeira"), type: "Furniture", site: origin))).GetProperty("id").GetGuid());
        }

        await OkAsync(await supervisor.PostAsJsonAsync($"{Base}/{ids[2]}/dispose", new { disposalReason = "Scrap", reason = "Quebrada" }));
        var startCount = await CountAsync(sp => sp.GetRequiredService<IAssetsDb>().AssetMovements.CountAsync(m => ids.Contains(m.AssetId)));

        var preview = await OkAsync(await supervisor.PostAsJsonAsync($"{Base}/transfer/preview", new { assetIds = ids, siteId = target, reason = "Reforma" }));
        preview.GetProperty("items").GetArrayLength().Should().Be(2);
        preview.GetProperty("items")[0].GetProperty("from").GetProperty("siteId").GetGuid().Should().Be(origin);
        preview.GetProperty("items")[0].GetProperty("to").GetProperty("siteId").GetGuid().Should().Be(target);
        preview.GetProperty("refused").EnumerateArray().Single().GetProperty("assetId").GetGuid().Should().Be(ids[2]);

        var refused = await supervisor.PostAsJsonAsync($"{Base}/transfer", new { assetIds = ids, siteId = target, reason = "Reforma" });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be(AssetMovementService.TransferRefusedCode);
        problem.GetRawText().Should().Contain(ids[2].ToString(), "a lista do que impede");
        (await CountAsync(sp => sp.GetRequiredService<IAssetsDb>().AssetMovements.CountAsync(m => ids.Contains(m.AssetId)))).Should().Be(startCount, "o lote não grava nada");

        var done = await OkAsync(await supervisor.PostAsJsonAsync($"{Base}/transfer", new { assetIds = ids.Take(2), siteId = target, reason = "Reforma" }));
        done.GetProperty("moved").GetInt32().Should().Be(2);
        (await CountAsync(sp => sp.GetRequiredService<IAssetsDb>().AssetMovements.CountAsync(m => ids.Contains(m.AssetId)))).Should().Be(startCount + 2, "um movimento por bem");
        foreach (var id in ids.Take(2))
        {
            (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}")).GetProperty("siteId").GetGuid().Should().Be(target);
        }

        (await supervisor.PostAsJsonAsync($"{Base}/transfer", new { assetIds = ids.Take(2), reason = "Sem destino" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>CA 5: o filtro "Responsável desligado" traz só os bens do funcionário desligado.</summary>
    [Fact]
    public async Task Filtro_responsavel_desligado_traz_os_bens_do_desligado()
    {
        var supervisor = await SupervisorAsync();
        var (leaving, staying) = (await EmployeeAsync("Saindo"), await EmployeeAsync("Ficando"));
        var mine = (await CreateAsync(supervisor, Input(Unique("Notebook"), type: "It", responsible: leaving))).GetProperty("id").GetGuid();
        var kept = (await CreateAsync(supervisor, Input(Unique("Monitor"), type: "It", responsible: staying))).GetProperty("id").GetGuid();
        await TerminateAsync(leaving);

        var list = await supervisor.GetFromJsonAsync<JsonElement>($"{Base}?responsibleTerminated=true&pageSize=100");
        var found = list.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ToList();
        found.Should().Contain(mine).And.NotContain(kept);
        list.GetProperty("items").EnumerateArray().Should().OnlyContain(a => a.GetProperty("responsibleTerminated").GetBoolean());
    }

    /// <summary>
    /// CA 6 e 7: a baixa por venda deixa o bem Disposed, fora da lista padrão e da capacidade, com o evento só de ids; o título a receber vem
    /// pré-preenchido só para quem tem finance.create e nada é gravado no Financeiro.
    /// </summary>
    [Fact]
    public async Task Baixa_por_venda_tira_o_bem_das_listas_e_so_preenche_o_titulo_para_quem_lanca()
    {
        var supervisor = await SupervisorAsync();
        var owner = await ActorAsync("owner");
        var center = await CenterAsync("Prensas");
        var buyer = await SupplierAsync("Comprador", PartyRoleKind.Customer);
        var name = Unique("Prensa hidráulica");
        var asset = await CreateAsync(supervisor, Input(name, center: center));
        var id = asset.GetProperty("id").GetGuid();
        var tag = asset.GetProperty("tag").GetString()!;
        var receivables = await CountAsync(sp => sp.GetRequiredService<BemAqui.Finance.Application.IFinanceDb>().Receivables.CountAsync());

        (await supervisor.PostAsJsonAsync($"{Base}/{id}/dispose", new { disposalReason = "Sale" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a venda pede o valor");
        (await supervisor.PostAsJsonAsync($"{Base}/{id}/dispose", new { disposalReason = "Other" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "outro pede a descrição");
        (await supervisor.PostAsJsonAsync($"{Base}/{id}/dispose", new { disposalReason = "Sale", value = 10m, buyerPartyId = await SupplierAsync("Fornecedor") })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "o comprador é um cliente");

        var sold = await OkAsync(await owner.PostAsJsonAsync($"{Base}/{id}/dispose", new { disposalReason = "Sale", value = 48_500.75m, buyerPartyId = buyer, reason = "Troca por nova" }));
        sold.GetProperty("asset").GetProperty("status").GetString().Should().Be("Disposed");
        sold.GetProperty("movement").GetProperty("kind").GetString().Should().Be("Disposal");
        sold.GetProperty("movement").GetProperty("disposalValue").GetDecimal().Should().Be(48_500.75m);
        var draft = sold.GetProperty("receivable");
        draft.GetProperty("partyId").GetGuid().Should().Be(buyer);
        draft.GetProperty("amount").GetDecimal().Should().Be(48_500.75m);
        draft.GetProperty("issueDate").GetString().Should().Be(Today);
        draft.GetProperty("description").GetString().Should().Be($"Venda do bem {tag} — {name}");
        (await CountAsync(sp => sp.GetRequiredService<BemAqui.Finance.Application.IFinanceDb>().Receivables.CountAsync())).Should().Be(receivables, "nada é gravado no Financeiro");

        var list = await supervisor.GetFromJsonAsync<JsonElement>($"{Base}?q={tag}");
        list.GetProperty("items").GetArrayLength().Should().Be(0, "o baixado sai da lista padrão");
        (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}?q={tag}&status=Disposed")).GetProperty("items").GetArrayLength().Should().Be(1, "filtro Baixados");
        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<IAssets>().ByWorkCenterAsync(center)).Should().NotContain(a => a.Id == id, "a máquina baixada sai da capacidade");
            var outbox = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().OutboxMessages.AsNoTracking()
                .Where(m => m.Type.EndsWith(nameof(AssetDisposed))).ToListAsync();
            outbox = outbox.Where(m => m.Payload.Contains(id.ToString(), StringComparison.Ordinal)).ToList();
            outbox.Should().ContainSingle();
            JsonDocument.Parse(outbox[0].Payload).RootElement.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).Should().BeEquivalentTo(["assetid", "disposedon"]);
        }

        (await supervisor.PostAsJsonAsync($"{Base}/{id}/dispose", new { disposalReason = "Scrap" })).StatusCode.Should().Be(HttpStatusCode.Conflict, "já baixado");
        (await supervisor.PostAsJsonAsync($"{Base}/{id}/movements", new { siteId = await SiteAsync(supervisor), reason = "Mover" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var detail = await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}");
        (await supervisor.PutAsJsonAsync($"{Base}/{id}", Input(name, center: center, version: detail.GetProperty("version").GetUInt32()))).StatusCode.Should().Be(HttpStatusCode.Conflict, "baixado não é editado");

        // Sem finance.create, o passo não aparece.
        var other = (await CreateAsync(supervisor, Input(Unique("Compressor")))).GetProperty("id").GetGuid();
        var noFinance = await OkAsync(await supervisor.PostAsJsonAsync($"{Base}/{other}/dispose", new { disposalReason = "Sale", value = 1_000m }));
        noFinance.GetProperty("receivable").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>CA 8: o bem baixado por engano volta a Active com um movimento de reativação; nada é apagado; desfazer a venda avisa do título.</summary>
    [Fact]
    public async Task Reativacao_volta_o_bem_a_ativo_sem_apagar_nada_e_avisa_da_venda()
    {
        var supervisor = await SupervisorAsync();
        var id = (await CreateAsync(supervisor, Input(Unique("Empilhadeira"), type: "Vehicle"))).GetProperty("id").GetGuid();
        (await supervisor.PostAsJsonAsync($"{Base}/{id}/reactivate", new { reason = "Engano" })).StatusCode.Should().Be(HttpStatusCode.Conflict, "só o baixado é reativado");
        await OkAsync(await supervisor.PostAsJsonAsync($"{Base}/{id}/dispose", new { disposalReason = "Sale", value = 5_000m }));

        (await supervisor.PostAsJsonAsync($"{Base}/{id}/reactivate", new { reason = "" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o motivo é obrigatório");
        var back = await OkAsync(await supervisor.PostAsJsonAsync($"{Base}/{id}/reactivate", new { reason = "Venda desfeita" }));
        back.GetProperty("asset").GetProperty("status").GetString().Should().Be("Active");
        back.GetProperty("saleReverted").GetBoolean().Should().BeTrue();
        back.GetProperty("movement").GetProperty("kind").GetString().Should().Be("Reactivation");

        var kinds = (await supervisor.GetFromJsonAsync<JsonElement>($"{Base}/{id}/movements")).EnumerateArray().Select(m => m.GetProperty("kind").GetString()).ToList();
        kinds.Should().BeEquivalentTo(["Reactivation", "Disposal"], "nada é apagado");
    }

    /// <summary>LGPD (ADR 0037): o funcionário vê no arquivo dele os bens e os movimentos em que entregou ou recebeu um bem, com o motivo.</summary>
    [Fact]
    public async Task Arquivo_do_titular_traz_os_movimentos_do_funcionario()
    {
        var supervisor = await SupervisorAsync();
        var (from, to) = (await EmployeeAsync("Entrega"), await EmployeeAsync("Recebe"));
        var id = (await CreateAsync(supervisor, Input(Unique("Furadeira"), type: "Tool", responsible: from))).GetProperty("id").GetGuid();
        await MoveAsync(supervisor, id, null, to, null, "Troca de turno");

        await using var scope = await ScopeAsync();
        var provider = scope.ServiceProvider.GetServices<IPersonalDataProvider>().OfType<AssetsPersonalDataProvider>().Single();
        var section = await provider.ExportAsync(new PersonalDataExportContext(new PersonalDataSubject(PersonalDataSubjectKind.Employee, from, "Entrega", [], EmployeeId: from), "LGPD-T", new HashSet<string>()));
        var movements = section!.Blocks.Single(b => b.Source == "assets.asset_movements" && b.Title == "Movimentações de bens").Records;
        movements.Should().ContainSingle();
        movements[0].Fields.Single(f => f.Name == "Papel").Value.Should().Be("Entregou o bem");
        movements[0].Fields.Single(f => f.Name == "Motivo").Value.Should().Be("Troca de turno");
    }
}
