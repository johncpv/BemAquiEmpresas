using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using BemAqui.Fiscal.Contracts;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Assets;

public partial class AssetTests
{
    /// <summary>CA 2: nasce Active com a etiqueta PAT-00001 (sequência PAT, sem ano, 5 dígitos), uma a mais por bem; a edição não muda a etiqueta.</summary>
    [Fact]
    public async Task Bem_nasce_ativo_com_etiqueta_PAT_sem_ano_e_a_etiqueta_nao_muda()
    {
        var supervisor = await SupervisorAsync();
        var first = await CreateAsync(supervisor, Input(Unique("Torno CNC"), brand: "Romi", model: "GL 240", value: 185_000.50m, date: "2025-03-10"));
        var second = await CreateAsync(supervisor, Input(Unique("Furadeira de bancada"), type: "Tool"));

        var tag = first.GetProperty("tag").GetString()!;
        Regex.IsMatch(tag, "^PAT-[0-9]{5}$").Should().BeTrue(tag);
        int.Parse(second.GetProperty("tag").GetString()![4..], System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(int.Parse(tag[4..], System.Globalization.CultureInfo.InvariantCulture) + 1);
        first.GetProperty("status").GetString().Should().Be("Active");
        first.GetProperty("type").GetString().Should().Be("Machine");
        first.GetProperty("acquisitionValue").GetDecimal().Should().Be(185_000.50m);
        first.GetProperty("acquisitionDate").GetString().Should().Be("2025-03-10");

        var id = first.GetProperty("id").GetGuid();
        var updated = await supervisor.PutAsJsonAsync($"{Base}/{id}", Input("Torno CNC revisado", brand: "Romi", value: 190_000m, version: first.GetProperty("version").GetUInt32()));
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var body = await JsonAsync(updated);
        body.GetProperty("tag").GetString().Should().Be(tag);
        body.GetProperty("name").GetString().Should().Be("Torno CNC revisado");

        var stale = await supervisor.PutAsJsonAsync($"{Base}/{id}", Input("Outro nome", version: first.GetProperty("version").GetUInt32()));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(stale)).Should().Be("concurrency_conflict");

        var invalid = await supervisor.PostAsJsonAsync(Base, new { name = " ", type = (string?)null });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>CA 3: com o centro, o local vem do último bem do centro e o responsável é o mais comum entre os bens dele (só ativos).</summary>
    [Fact]
    public async Task Sugestao_do_centro_traz_o_local_do_ultimo_bem_e_o_responsavel_mais_comum()
    {
        var supervisor = await SupervisorAsync();
        var center = await CenterAsync("Usinagem");
        var (oldSite, newSite) = (await SiteAsync(supervisor), await SiteAsync(supervisor));
        var (ana, bruno) = (await EmployeeAsync("Ana"), await EmployeeAsync("Bruno"));
        await CreateAsync(supervisor, Input(Unique("Fresadora"), site: oldSite, responsible: ana, center: center));
        await CreateAsync(supervisor, Input(Unique("Torno"), site: oldSite, responsible: ana, center: center));
        await CreateAsync(supervisor, Input(Unique("Serra"), site: newSite, responsible: bruno, center: center));

        var suggestion = await supervisor.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Base}/suggestions?workCenterId={center}");
        suggestion.GetProperty("siteId").GetGuid().Should().Be(newSite, "o local do último bem do centro");
        suggestion.GetProperty("responsibleEmployeeId").GetGuid().Should().Be(ana, "o funcionário mais comum entre os bens do centro");

        await TerminateAsync(ana);
        (await supervisor.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Base}/suggestions?workCenterId={center}"))
            .GetProperty("responsibleEmployeeId").GetGuid().Should().Be(bruno, "desligado não é sugerido");
        (await supervisor.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Base}/suggestions?workCenterId={await CenterAsync("Vazio")}"))
            .GetProperty("siteId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    /// <summary>CA 4: o seletor traz só ativos (id, nome, matrícula e cargo); o desligado continua no bem com a marca "Desligado".</summary>
    [Fact]
    public async Task Responsavel_so_entre_ativos_e_o_desligado_continua_no_bem_marcado()
    {
        var supervisor = await SupervisorAsync();
        var (active, gone) = (await EmployeeAsync("Carla"), await EmployeeAsync("Davi", terminated: true));
        var choices = await supervisor.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/assets/choices/responsibles");
        var ids = choices.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        ids.Should().Contain(active).And.NotContain(gone);
        choices.EnumerateArray().First().EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["id", "name", "registration", "positionName"], "sem dado sensível");

        var refused = await supervisor.PostAsJsonAsync(Base, Input(Unique("Compressor"), responsible: gone));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(refused)).GetProperty("errors").GetProperty("responsibleEmployeeId")[0].GetString().Should().Be("Escolha um funcionário ativo da lista.");

        var asset = await CreateAsync(supervisor, Input(Unique("Compressor"), responsible: active));
        await TerminateAsync(active);
        var id = asset.GetProperty("id").GetGuid();
        var detail = await supervisor.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Base}/{id}");
        detail.GetProperty("responsibleTerminated").GetBoolean().Should().BeTrue();
        detail.GetProperty("responsibleName").GetString().Should().StartWith("Carla");
        var edit = await supervisor.PutAsJsonAsync($"{Base}/{id}", Input("Compressor de ar", responsible: active, version: detail.GetProperty("version").GetUInt32()));
        edit.StatusCode.Should().Be(HttpStatusCode.OK, "o responsável que já estava no bem continua aceito");
    }

    /// <summary>CA 5: locais do patrimônio com seed Fábrica e Escritório, criação rápida com assets.edit, nome único e local com bem só inativa.</summary>
    [Fact]
    public async Task Locais_tem_seed_criacao_rapida_nome_unico_e_com_bem_so_inativa()
    {
        var supervisor = await SupervisorAsync();
        var sites = await supervisor.GetFromJsonAsync<System.Text.Json.JsonElement>(Sites);
        sites.EnumerateArray().Where(s => s.GetProperty("isSeed").GetBoolean()).Select(s => s.GetProperty("name").GetString()).Should().Contain(["Fábrica", "Escritório"]);

        var name = Unique("Almoxarifado");
        var site = await SiteAsync(supervisor, name);
        var duplicate = await supervisor.PostAsJsonAsync(Sites, new { name = name.ToUpperInvariant() });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(duplicate)).Should().Be("asset_site_name_taken");
        (await (await ActorAsync("warehouse")).PostAsJsonAsync(Sites, new { name = Unique("Sem edição") })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await CreateAsync(supervisor, Input(Unique("Empilhadeira"), type: "Vehicle", site: site));
        var owner = await ActorAsync("assets:view,edit,delete");
        var inUse = await owner.DeleteAsync($"{Sites}/{site}");
        inUse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(inUse)).Should().Be("asset_site_in_use");
        (await supervisor.PutAsJsonAsync($"{Sites}/{site}", new { name, isActive = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        var refused = await supervisor.PostAsJsonAsync(Base, Input(Unique("Paleteira"), site: site));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "local inativo não entra em bem novo");

        var empty = await SiteAsync(supervisor);
        (await owner.DeleteAsync($"{Sites}/{empty}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>CA 6: mesma marca, modelo e série de outro bem não excluído: 409 com a etiqueta e o id para "Abrir bem".</summary>
    [Fact]
    public async Task Serie_repetida_na_mesma_marca_e_modelo_da_409_com_a_etiqueta()
    {
        var supervisor = await SupervisorAsync();
        var serial = "SN-" + Guid.NewGuid().ToString("N")[..8];
        var first = await CreateAsync(supervisor, Input(Unique("Solda MIG"), brand: "ESAB", model: "Smashweld 318", serial: serial));
        var repeated = await supervisor.PostAsJsonAsync(Base, Input(Unique("Solda MIG 2"), brand: " esab ", model: "SMASHWELD 318", serial: serial.ToLowerInvariant()));
        repeated.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(repeated);
        Code(problem).Should().Be("asset_serial_taken");
        problem.GetProperty("detail").GetString().Should().Be($"Já existe o bem {first.GetProperty("tag").GetString()} com esta série.");
        problem.GetProperty("assetId").GetGuid().Should().Be(first.GetProperty("id").GetGuid());

        await CreateAsync(supervisor, Input(Unique("Solda de outra marca"), brand: "Lincoln", model: "Smashweld 318", serial: serial));
    }

    /// <summary>CA 7: com a nota do Fiscal, fornecedor e data vêm dela; sem nota (bem antigo), referência em texto e fornecedor do IParties.</summary>
    [Fact]
    public async Task Nota_de_entrada_preenche_fornecedor_e_data_e_sem_nota_vale_o_texto_livre()
    {
        var supervisor = await SupervisorAsync();
        var supplier = await SupplierAsync("Máquinas Paraná");
        var invoice = new IncomingInvoiceSummary(Guid.CreateVersion7(), "4521", "1", supplier, "Máquinas Paraná Ltda", new DateOnly(2025, 8, 14));
        Invoices[invoice.Id] = invoice;
        (await supervisor.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/assets/choices/incoming-invoices?q=4521"))
            .EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(invoice.Id);

        var fromInvoice = await CreateAsync(supervisor, Input(Unique("Prensa"), invoice: invoice.Id, invoiceRef: "ignorado"));
        fromInvoice.GetProperty("supplierPartyId").GetGuid().Should().Be(supplier);
        fromInvoice.GetProperty("acquisitionDate").GetString().Should().Be("2025-08-14");
        fromInvoice.GetProperty("incomingInvoice").GetProperty("number").GetString().Should().Be("4521");
        fromInvoice.GetProperty("invoiceRef").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        var unknown = await supervisor.PostAsJsonAsync(Base, Input(Unique("Prensa"), invoice: Guid.CreateVersion7()));
        unknown.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var legacy = await CreateAsync(supervisor, Input(Unique("Guilhotina antiga"), invoiceRef: "NF 1203 série 2", supplier: supplier, date: "2012-05-02"));
        legacy.GetProperty("invoiceRef").GetString().Should().Be("NF 1203 série 2");
        legacy.GetProperty("supplierName").GetString().Should().StartWith("Máquinas Paraná");
        var customer = await SupplierAsync("Cliente", BemAqui.Parties.Contracts.PartyRoleKind.Customer);
        (await supervisor.PostAsJsonAsync(Base, Input(Unique("Prensa"), supplier: customer))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "fornecedor só com o papel Fornecedor");
    }
}
