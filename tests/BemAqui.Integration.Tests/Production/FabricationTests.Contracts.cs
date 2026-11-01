using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BemAqui.Commercial.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

public abstract partial class FabricationTestBase
{
    protected const string Installations = "/api/v1/production/installations";

    protected static byte[] Pad(byte[] head, int size)
    {
        var bytes = new byte[size];
        head.CopyTo(bytes, 0);
        for (var i = head.Length; i < size; i++)
        {
            bytes[i] = (byte)'a';
        }

        return bytes;
    }

    protected static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid orderId, byte[] content, string name)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", name);
        return await client.PostAsync($"/api/v1/attachments/ProductionOrder/{orderId}", form);
    }

    /// <summary>OS de instalação da COM-06 no mesmo projeto (instalador, valor e datas).</summary>
    protected static async Task<Guid> InstallationAsync(HttpClient owner, Guid projectId)
    {
        var party = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Instalador " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Installer" } });
        var installer = (await JsonAsync(party)).GetProperty("id").GetGuid();
        var created = await owner.SendAsync(Post(Installations, new { projectId, projectItemIds = (Guid[]?)null, installerId = installer, contractValue = 5_000m, plannedStart = Iso(Today), plannedEnd = Iso(Today.AddDays(5)) }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }
}

public partial class FabricationOrdersTests
{
    [Fact]
    public async Task Contratos_separam_os_tipos_e_a_instalacao_continua_lendo_a_mesma_tabela()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var center = await CenterAsync("Montagem final");
        var product = await ProductAsync();
        await RoutingAsync(owner, center, product);
        var itemRouting = await RoutingAsync(owner, center, name: Unique("Roteiro do item"), steps: [Step("Projeto", "P", center, 4m)]);
        var (projectId, _, items) = await ApprovedAsync(owner, [("Equipment", product, 1m)], routingOnItems: itemRouting);
        var fabrication = (await OrderOfAsync(items[0]))!;
        var installation = await InstallationAsync(owner, projectId);

        // CA 4 e 22: o roteiro do item do orçamento passa ao item do projeto e vence o do produto.
        fabrication.RoutingTemplateId.Should().Be(itemRouting);
        await using (var scope = await ScopeAsync())
        {
            var projects = scope.ServiceProvider.GetRequiredService<IProjects>();
            var summary = (await projects.FindAsync(projectId))!;
            summary.PromisedDate.Should().Be(Today.AddDays(-1).AddDays(90));
            var info = (await projects.ItemsAsync(projectId)).Single();
            info.Should().BeEquivalentTo(new { Kind = ProjectItemKind.Equipment, ItemId = (Guid?)product, IsOnboarding = false, RoutingTemplateId = (Guid?)itemRouting, ProjectId = projectId });

            // CA 19 e 21: cada contrato só vê o seu tipo.
            var installs = scope.ServiceProvider.GetRequiredService<IInstallationOrders>();
            (await installs.ByProjectAsync(projectId)).Select(o => o.Id).Should().Equal(installation);
            (await installs.StatusAsync(fabrication.Id)).Should().BeNull();
            var orders = scope.ServiceProvider.GetRequiredService<IProductionOrders>();
            var found = (await orders.ByProjectAsync(projectId)).Single();
            found.Should().BeEquivalentTo(new { fabrication.Id, Type = ProductionOrderKind.Fabrication, ProjectItemId = items[0], Quantity = 1m, Status = ProductionOrderState.Open, CurrentStepName = "Projeto", CurrentStepCode = "P" });
            (await orders.StatusAsync(installation)).Should().BeNull();
            (await orders.FindAsync(installation)).Should().BeNull();
            (await orders.FindManyAsync([fabrication.Id, installation])).Should().ContainSingle();
        }

        // CA 19 e 20: com situações novas na tabela, a instalação continua listando e abrindo, sem exceção.
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Paused' WHERE id = {fabrication.Id}"));
        var list = await JsonAsync(await owner.GetAsync($"{Installations}?projectId={projectId}"));
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(installation);
        (await owner.GetAsync($"{Installations}/{installation}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync($"{Installations}/{fabrication.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"{Base}/{installation}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"{Installations}/defaults?projectId={projectId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Quality' WHERE id = {installation}"));
        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<IInstallationOrders>().StatusAsync(installation)).Should().Be(InstallationState.InProgress);
        }

        // CA 19: o CHECK por tipo recusa instalação sem instalador e fabricação com valor combinado.
        var noInstaller = () => WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET installer_id = NULL WHERE id = {installation}"));
        await noInstaller.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.SqlState == Npgsql.PostgresErrorCodes.CheckViolation);
        var withValue = () => WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET contract_value = 10 WHERE id = {fabrication.Id}"));
        await withValue.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.SqlState == Npgsql.PostgresErrorCodes.CheckViolation);

        // ENG-01 CA 10: o roteiro em uso mostra as OS e os itens do Commercial.
        var usage = await JsonAsync(await owner.GetAsync($"{Routings}/{itemRouting}"));
        usage.GetProperty("usage").GetString().Should().Contain("1 OS").And.Contain("itens de orçamento ou projeto");
    }

    [Fact]
    public async Task Anexos_aceitam_desenho_de_25_MB_na_fabricacao_e_a_instalacao_segue_com_10_MB()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (projectId, _, items) = await ApprovedAsync(owner, [("Equipment", null, 1m)]);
        var fabrication = (await OrderOfAsync(items[0]))!.Id;
        var installation = await InstallationAsync(owner, projectId);
        var dwg = Pad("AC1032"u8.ToArray(), 2048);
        var dxf = Encoding.ASCII.GetBytes("0\nSECTION\n2\nHEADER\n0\nENDSEC\n0\nEOF\n");
        var big = Pad("%PDF-1.7 "u8.ToArray(), 25 * 1024 * 1024);

        // CA 12: dwg, dxf e pdf de 25 MB na fabricação, com quem anexou.
        (await UploadAsync(owner, fabrication, dwg, "planta.dwg")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await UploadAsync(owner, fabrication, dxf, "corte.dxf")).StatusCode.Should().Be(HttpStatusCode.Created);
        var large = await UploadAsync(owner, fabrication, big, "memorial.pdf");
        large.StatusCode.Should().Be(HttpStatusCode.Created, await large.Content.ReadAsStringAsync());
        var (_, viewer) = await ActorAsync("production:view");
        var listed = await JsonAsync(await viewer.GetAsync($"/api/v1/attachments/ProductionOrder/{fabrication}"));
        listed.GetArrayLength().Should().Be(3);
        listed[0].GetProperty("uploadedByName").ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Undefined);
        (await UploadAsync(viewer, fabrication, dxf, "x.dxf")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // CA 12: a instalação continua com png, jpg e pdf de até 10 MB.
        (await UploadAsync(owner, installation, dwg, "planta.dwg")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await UploadAsync(owner, installation, Pad("%PDF-1.7 "u8.ToArray(), 11 * 1024 * 1024), "termo.pdf")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await UploadAsync(owner, installation, Pad("%PDF-1.7 "u8.ToArray(), 1024), "termo.pdf")).StatusCode.Should().Be(HttpStatusCode.Created);

        // CA 13: 30 envios por hora por usuário; o 31º recebe 429 e nada é gravado.
        var (uploaderId, uploader) = await ActorAsync("production:view,edit");
        for (var i = 0; i < 30; i++)
        {
            (await UploadAsync(uploader, fabrication, dxf, $"corte-{i}.dxf")).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var limited = await UploadAsync(uploader, fabrication, dxf, "corte-31.dxf");
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await JsonAsync(limited)).GetProperty("detail").GetString().Should().Be("Muitos envios em pouco tempo. Tente de novo em alguns minutos.");
        (await WithDbAsync(db => db.Attachments.CountAsync(a => a.UploadedBy == uploaderId))).Should().Be(30);
    }

    [Fact]
    public async Task Lista_filtra_por_projeto_cliente_linha_situacao_e_atraso_e_aguenta_5_mil_OS()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var line = Unique("Linha A");
        var center = await CenterAsync("Corte linha", line);
        var product = await ProductAsync();
        await RoutingAsync(owner, center, product);
        var (projectId, _, items) = await ApprovedAsync(owner, [("Equipment", product, 1m), ("Equipment", null, 1m)]);
        var withLine = (await OrderOfAsync(items[0]))!;
        var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"));
        var party = project.GetProperty("partyId").GetGuid();

        var byProject = await JsonAsync(await owner.GetAsync($"{Base}?projectId={projectId}"));
        byProject.GetProperty("total").GetInt32().Should().Be(2);
        var row = byProject.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == withLine.Id);
        row.GetProperty("codes").EnumerateArray().Select(c => c.GetString()).Should().Equal("C", "M", "Q");
        row.GetProperty("currentStepIndex").GetInt32().Should().Be(0);
        row.GetProperty("partyId").GetGuid().Should().Be(party);
        (await JsonAsync(await owner.GetAsync($"{Base}?partyId={party}"))).GetProperty("total").GetInt32().Should().Be(2);
        // O item sem produto pode receber o mesmo roteiro pela regra "único ativo" (depende dos roteiros que os outros testes deixaram):
        // a lista pela linha traz a OS do roteiro da linha e só OS deste projeto.
        var byLine = (await JsonAsync(await owner.GetAsync($"{Base}?line={Uri.EscapeDataString(line)}"))).GetProperty("items").EnumerateArray().ToList();
        byLine.Select(r => r.GetProperty("id").GetGuid()).Should().Contain(withLine.Id);
        byLine.Should().OnlyContain(r => r.GetProperty("projectId").GetGuid() == projectId);
        (await JsonAsync(await owner.GetAsync($"{Base}?projectId={projectId}&situation=cancelada"))).GetProperty("total").GetInt32().Should().Be(0);

        // Atraso (padrão 2): prometido no passado e OS não concluída.
        (await JsonAsync(await owner.GetAsync($"{Base}?projectId={projectId}&late=true"))).GetProperty("total").GetInt32().Should().Be(0);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET promised_date = {Today.AddDays(-3)} WHERE id = {projectId}"));
        var late = await JsonAsync(await owner.GetAsync($"{Base}?projectId={projectId}&late=true"));
        late.GetProperty("total").GetInt32().Should().Be(2);
        late.GetProperty("items")[0].GetProperty("delayDays").GetInt32().Should().Be(3);

        // CA 15: 5 mil OS em menos de 1 s. As 5 mil saem no fim: o banco é da classe e os outros testes não devem varrê-las.
        await WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"INSERT INTO production.production_orders (id, number, type, project_id, contract_id, project_item_id, quantity, status, priority, created_at) SELECT gen_random_uuid(), 'OS-P-' || g, 'Fabrication', {projectId}, c.contract_id, gen_random_uuid(), 1, 'Open', 0, now() FROM generate_series(1, 5000) g, (SELECT contract_id FROM production.production_orders WHERE id = {withLine.Id}) c"));
        try
        {
            await owner.GetAsync($"{Base}?late=true");
            var watch = Stopwatch.StartNew();
            var all = await owner.GetAsync($"{Base}?late=true&pageSize=50");
            watch.Stop();
            all.StatusCode.Should().Be(HttpStatusCode.OK);
            (await JsonAsync(all)).GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(5000);
            watch.ElapsedMilliseconds.Should().BeLessThan(1000);
        }
        finally
        {
            await WithDbAsync(db => db.Database.ExecuteSqlAsync($"DELETE FROM production.production_orders WHERE project_id = {projectId} AND number LIKE 'OS-P-%'"));
        }
    }

    [Fact]
    public async Task Tres_camadas_de_acesso_nas_OS_de_fabricacao()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, _, items) = await ApprovedAsync(owner, [("Product", null, 1m), ("Product", null, 1m)]);
        var (_, none) = await ActorAsync(null);
        var (_, viewer) = await ActorAsync("production:view");
        var (_, creator) = await ActorAsync("production:view,create");

        // CA 23: sem grupo, 403 forbidden; só ver, não gera nem edita; criar, gera.
        var forbidden = await none.GetAsync(Base);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(forbidden)).Should().Be("forbidden");
        (await viewer.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.SendAsync(Post(Base, new { projectItemId = items[0] }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var created = await creator.SendAsync(Post(Base, new { projectItemId = items[0] }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var order = await JsonAsync(created);
        var id = order.GetProperty("id").GetGuid();
        (await creator.PutAsJsonAsync($"{Base}/{id}/steps", StepsBody(order, []))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await creator.PostAsJsonAsync($"{Base}/{id}/cancel", new { reason = "x", version = Version(order) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.SendAsync(Post(Base, new { projectItemId = items[1] }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Sem Idempotency-Key, o comando que cria documento é recusado.
        (await creator.PostAsJsonAsync(Base, new { projectItemId = items[1] })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}