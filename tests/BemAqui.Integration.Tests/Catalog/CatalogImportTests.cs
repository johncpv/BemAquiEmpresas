using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Catalog.Application;
using BemAqui.Catalog.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Contracts;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Integration.Tests.Catalog;

/// <summary>
/// CAT-02: importação de produtos por planilha. O tenant A recebe o perfil Comércio (o catálogo é oculto para a fábrica). O host da classe roda
/// no pedido até 2 linhas válidas (acima, vai para o job, capturado aqui). Códigos únicos por teste: os testes compartilham o tenant.
/// </summary>
public class CatalogImportTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Import = "/api/v1/catalog/import";

    private bool _retail;

    private Messaging.CapturingJobClient Jobs => tenancy.State<Messaging.CapturingJobClient>();

    private WebApplicationFactory<Program> Factory => tenancy.Shared(nameof(CatalogImportTests), b => b.UseSetting("Catalog:ImportSyncMaxRows", "2").ConfigureTestServices(s =>
    {
        s.RemoveAll<Hangfire.IBackgroundJobClient>();
        s.AddSingleton<Hangfire.IBackgroundJobClient>(Jobs);
    }));

    [Fact]
    public async Task Previa_com_erros_por_linha_so_as_validas_entram_e_reimportar_atualiza_sem_duplicar()
    {
        var (_, client) = await ActorAsync("owner");
        var tag = Tag();
        var gtin = NewGtin();
        var sheet = Sheet(
            [$"{tag}-1", "Café " + tag, "UN", "09012100", gtin, "19,90", null, null, "Mercearia " + tag],
            [$"{tag}-2", "Açúcar " + tag, null, null, null, "abc", null, null, null],
            [$"{tag}-3", "Arroz " + tag, null, null, "7891000315508", null, null, null, null],
            [$"{tag}-1", "Repetido " + tag, null, null, null, null, null, null, null],
            [$"{tag}-4", "Feijão " + tag, "XX", null, null, null, null, null, null]);

        // CA 2: total, cria, atualiza e erros por linha, com o motivo em português.
        var preview = await PreviewAsync(client, sheet);
        preview.GetProperty("totalRows").GetInt32().Should().Be(5);
        preview.GetProperty("toCreate").GetInt32().Should().Be(1);
        preview.GetProperty("toUpdate").GetInt32().Should().Be(0);
        preview.GetProperty("errorCount").GetInt32().Should().Be(4);
        var errors = preview.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        errors.Should().Contain("Linha 3: preço de venda inválido. Use um valor maior que zero, com até 2 casas.");
        errors.Should().Contain("Linha 4: código de barras inválido.");
        errors.Should().Contain("Linha 5: código interno repetido na planilha (já está na linha 2).");
        errors.Should().Contain(e => e!.StartsWith("Linha 6: unidade \"XX\" não existe.", StringComparison.Ordinal));
        preview.GetProperty("newCategories").EnumerateArray().Select(c => c.GetString()).Should().Equal("Mercearia " + tag);

        // CA 3 e 8: só a válida entra; o relatório diz criados, atualizados e ignorados; as com erro descem numa planilha.
        var id = preview.GetProperty("id").GetGuid();
        var done = await ConfirmAsync(client, id, new { launchInitialStock = false, locationId = (Guid?)null });
        done.GetProperty("status").GetString().Should().Be("Completed");
        done.GetProperty("created").GetInt32().Should().Be(1);
        done.GetProperty("skipped").GetInt32().Should().Be(4);
        var errorsFile = await client.GetAsync($"{Import}/{id}/errors");
        errorsFile.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var workbook = new XLWorkbook(new MemoryStream(await errorsFile.Content.ReadAsByteArrayAsync())))
        {
            var rows = workbook.Worksheet("Produtos");
            rows.Cell(2, 1).GetValue<int>().Should().Be(3);
            rows.Cell(2, 3).GetString().Should().Be($"{tag}-2", "a linha volta como veio, para corrigir");
            rows.LastRowUsed()!.RowNumber().Should().Be(5);
        }

        (await client.PostAsJsonAsync($"{Import}/{id}/confirm", new { launchInitialStock = false })).StatusCode.Should().Be(HttpStatusCode.Conflict, "confirma uma vez só");

        var product = await QueryAsync(db => db.Products.AsNoTracking().SingleAsync(p => p.SaleName == "Café " + tag));
        (await QueryAsync(db => db.ProductCategories.AsNoTracking().SingleAsync(c => c.Id == product.CategoryId))).Name.Should().Be("Mercearia " + tag, "a categoria nova nasce");
        (await PriceAsync(product.Id)).Should().Be(19.90m);

        // CA 4: o mesmo código interno, e o código de barras sem código interno, atualizam; nada duplica.
        var again = await PreviewAsync(client, Sheet(
            [$"{tag}-1", "Café torrado " + tag, null, null, gtin, "21,50", null, null, null],
            [null, null, null, null, gtin, "22,00", null, null, null]));
        again.GetProperty("errorCount").GetInt32().Should().Be(1, "o mesmo GTIN duas vezes na planilha");
        again.GetProperty("toUpdate").GetInt32().Should().Be(1);
        again.GetProperty("toCreate").GetInt32().Should().Be(0);
        var updated = await ConfirmAsync(client, again.GetProperty("id").GetGuid(), new { launchInitialStock = false });
        updated.GetProperty("updated").GetInt32().Should().Be(1);
        (await QueryAsync(db => db.Products.AsNoTracking().CountAsync(p => p.ItemId == product.ItemId))).Should().Be(1);
        (await QueryAsync(db => db.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id))).SaleName.Should().Be("Café torrado " + tag);
        (await PriceAsync(product.Id)).Should().Be(21.50m);

        var byGtin = await ConfirmAsync(client, (await PreviewAsync(client, Sheet([null, null, null, null, gtin, "23,00", null, null, null]))).GetProperty("id").GetGuid(), new { launchInitialStock = false });
        byGtin.GetProperty("updated").GetInt32().Should().Be(1, "sem código interno, a chave é o código de barras");
        (await PriceAsync(product.Id)).Should().Be(23.00m);
        (await QueryAsync(db => db.ProductBarcodes.AsNoTracking().CountAsync(b => b.Gtin == gtin))).Should().Be(1);
    }

    [Fact]
    public async Task Sem_alterar_preco_a_previa_avisa_e_a_importacao_segue_sem_o_preco()
    {
        var (_, client) = await ActorAsync("view,create");
        var tag = Tag();
        var preview = await PreviewAsync(client, Sheet([$"{tag}-1", "Sabão " + tag, null, null, null, "abc", null, null, null]));
        preview.GetProperty("pricesIgnored").GetBoolean().Should().BeTrue();
        preview.GetProperty("errorCount").GetInt32().Should().Be(0, "a coluna de preço nem é lida");
        var done = await ConfirmAsync(client, preview.GetProperty("id").GetGuid(), new { launchInitialStock = false });
        done.GetProperty("created").GetInt32().Should().Be(1);
        var product = await QueryAsync(db => db.Products.AsNoTracking().SingleAsync(p => p.SaleName == "Sabão " + tag));
        (await PriceAsync(product.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Saldo_inicial_so_com_a_confirmacao_e_uma_vez_so()
    {
        var (_, owner) = await ActorAsync("owner");
        var tag = Tag();
        var sheet = Sheet([$"{tag}-1", "Vela " + tag, null, null, null, null, "2,50", "5", null]);
        var preview = await PreviewAsync(owner, sheet);
        preview.GetProperty("stockRows").GetInt32().Should().Be(1);
        preview.GetProperty("canLaunchStock").GetBoolean().Should().BeTrue();
        var location = preview.GetProperty("suggestedLocationId").GetGuid();
        var id = preview.GetProperty("id").GetGuid();
        (await owner.PostAsJsonAsync($"{Import}/{id}/confirm", new { launchInitialStock = true, locationId = Guid.NewGuid() })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "local que não existe");

        var (_, noAdjust) = await ActorAsync("view,create,edit_price");
        var other = await PreviewAsync(noAdjust, Sheet([$"{tag}-9", "Pavio " + tag, null, null, null, null, "1", "3", null]));
        other.GetProperty("canLaunchStock").GetBoolean().Should().BeFalse();
        (await noAdjust.PostAsJsonAsync($"{Import}/{other.GetProperty("id").GetGuid()}/confirm", new { launchInitialStock = true, locationId = location })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "saldo inicial exige inventory.adjust");

        var done = await ConfirmAsync(owner, id, new { launchInitialStock = true, locationId = location });
        done.GetProperty("stockPosted").GetInt32().Should().Be(1);
        var product = await QueryAsync(db => db.Products.AsNoTracking().SingleAsync(p => p.SaleName == "Vela " + tag));
        (await BalanceAsync(product.ItemId, location)).Should().Be(5m);

        // Reimportar a mesma planilha não lança o saldo de novo.
        var again = await ConfirmAsync(owner, (await PreviewAsync(owner, sheet)).GetProperty("id").GetGuid(), new { launchInitialStock = true, locationId = location });
        again.GetProperty("updated").GetInt32().Should().Be(1);
        again.GetProperty("stockPosted").GetInt32().Should().Be(0);
        again.GetProperty("stockAlreadyPosted").GetInt32().Should().Be(1);
        (await BalanceAsync(product.ItemId, location)).Should().Be(5m);
    }

    [Fact]
    public async Task Importacao_grande_vai_para_o_job_com_o_tenant_e_avisa_no_sino()
    {
        var (userId, client) = await ActorAsync("owner");
        var tag = Tag();
        var preview = await PreviewAsync(client, Sheet(
            [$"{tag}-1", "Lápis " + tag, null, null, null, null, null, null, null],
            [$"{tag}-2", "Caneta " + tag, null, null, null, null, null, null, null],
            [$"{tag}-3", "Borracha " + tag, null, null, null, null, null, null, null]));
        var before = Jobs.Created.Count;
        var queued = await ConfirmAsync(client, preview.GetProperty("id").GetGuid(), new { launchInitialStock = false });
        queued.GetProperty("inBackground").GetBoolean().Should().BeTrue();
        queued.GetProperty("status").GetString().Should().Be("Running");
        var job = Jobs.Created.Skip(before).Single(j => j.Job.Type == typeof(ProductImportJob)).Job;
        job.Args[0].Should().Be(tenancy.TenantA.Id, "job com tenantId");
        var args = (ProductImportJobArgs)job.Args[1];
        args.RequestedBy.Should().Be(userId);

        await ActivatorUtilities.CreateInstance<ProductImportJob>(Factory.Services).ExecuteAsync(tenancy.TenantA.Id, args, CancellationToken.None);
        var done = await client.GetFromJsonAsync<JsonElement>($"{Import}/{args.RunId}");
        done.GetProperty("status").GetString().Should().Be("Completed");
        done.GetProperty("created").GetInt32().Should().Be(3);
        done.GetProperty("processed").GetInt32().Should().Be(3);
        await using var scope = await ScopeAsync();
        var notification = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.Type == ProductImportService.NotificationType).SingleAsync();
        notification.Title.Should().Contain("3 criados");
        notification.Link.Should().Be($"{ProductImportService.Route}?id={args.RunId}");
    }

    [Fact]
    public async Task Modelo_para_baixar_e_arquivo_fora_da_allowlist()
    {
        var (_, client) = await ActorAsync("view,create");
        var template = await client.GetAsync($"{Import}/template");
        template.StatusCode.Should().Be(HttpStatusCode.OK);
        template.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        using var form = new MultipartFormDataContent { { new ByteArrayContent("codigo;nome"u8.ToArray()), "file", "produtos.csv" } };
        var refused = await client.PostAsync($"{Import}/preview", form);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString().Should().Contain(".xlsx");

        var (_, viewer) = await ActorAsync("view");
        (await viewer.GetAsync($"{Import}/template")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "importar é catalog.create");
    }

    private static string Tag() => "I" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

    private static string NewGtin()
    {
        var body = string.Concat(Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(0, 10).ToString(CultureInfo.InvariantCulture)));
        return body + GtinRules.CheckDigit(body).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Planilha no modelo: a aba "Produtos" com o cabeçalho e as linhas em texto.</summary>
    private static byte[] Sheet(params string?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Produtos");
        for (var c = 0; c < ProductImportColumns.Headers.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = ProductImportColumns.Headers[c];
        }

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c] is { } text)
                {
                    sheet.Cell(r + 2, c + 1).SetValue(text);
                }
            }
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static async Task<JsonElement> PreviewAsync(HttpClient client, byte[] sheet)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(sheet), "file", "produtos.xlsx" } };
        var response = await client.PostAsync($"{Import}/preview", form);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> ConfirmAsync(HttpClient client, Guid id, object input)
    {
        var response = await client.PostAsJsonAsync($"{Import}/{id}/confirm", input);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<decimal?> PriceAsync(Guid productId)
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<PriceService>().CurrentAsync(productId);
    }

    private async Task<decimal> BalanceAsync(Guid itemId, Guid locationId)
    {
        await using var scope = await ScopeAsync();
        return (await scope.ServiceProvider.GetRequiredService<IStockBalances>().GetAsync([itemId], locationId)).Sum(b => b.OnHand);
    }

    private async Task<T> QueryAsync<T>(Func<BemAqui.Host.Persistence.TenantDbContext, Task<T>> query)
    {
        await using var scope = await ScopeAsync();
        return await query(scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>());
    }

    /// <summary>Perfil Comércio no tenant A (uma vez por classe): o catálogo passa a ser liberado.</summary>
    private async Task RetailAsync()
    {
        if (_retail)
        {
            return;
        }

        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<BemAqui.Platform.Application.Modules.TenantModuleService>();
        (await service.ApplyProfileAsync(tenancy.TenantA.Id, BusinessProfileCatalog.Retail, "counter", null)).IsSuccess.Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<ITenantResolver>().Invalidate();
        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        _retail = true;
    }

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Usuário no Proprietário (<c>owner</c>, com <c>inventory.adjust</c>) ou num grupo só do catálogo com as ações dadas.</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string actions)
    {
        await RetailAsync();
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "i" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (actions == "owner")
            {
                groupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            }
            else
            {
                var group = new Group { Name = "Importação " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(group);
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "catalog", Enabled = true });
                db.GroupPermissions.AddRange(actions.Split(',').Select(a => new GroupGrant { GroupId = group.Id, ModuleCode = "catalog", Action = a }));
                groupId = group.Id;
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }
}
