using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Sales.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Sales;

/// <summary>
/// PDV-07: Loja → Vendas (lista, filtros, totais do filtro, exportação, detalhe com a trilha e o CPF mascarado) e os cards da loja no Início, com
/// PostgreSQL real. Cada teste usa um dia de negócio próprio, no passado.
/// </summary>
public partial class SalesTests
{
    private const string ValidCpf = "52998224725";

    [Fact]
    public async Task Pdv07_lista_filtra_soma_o_filtro_mascara_o_CPF_e_esconde_o_custo_sem_view_cost()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 20, 4m);
        var day = Today.AddDays(-Random.Shared.Next(300, 360));
        var a = await IngestAsync(Online(day, location, [Line(product, 2, 10m)], [Paid(methods.Pix, 20m)]));
        var b = await IngestAsync(Online(day, location, [Line(product, 1, 10m)], [Paid(methods.Cash, 10m)]));
        var c = await IngestAsync(Online(day.AddDays(1), location, [Line(product, 1, 10m)], [Paid(methods.Pix, 10m)]));
        a.IsSuccess.Should().BeTrue();
        b.IsSuccess.Should().BeTrue();
        c.IsSuccess.Should().BeTrue();
        (await CancelAsync(b.Value.SaleId)).IsSuccess.Should().BeTrue();
        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<ISaleCustomerDocuments>().AttachAsync(new SaleCustomerDocumentInput(a.Value.SaleId, "cpf", ValidCpf))).IsSuccess.Should().BeTrue();
        }

        await DrainAsync();
        var d = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        // CA 2: o dia tem 2 vendas (uma cancelada); os totais são do filtro: feitas R$ 20, canceladas 1 (R$ 10); custo 2 × 4 = 8, margem 12.
        var list = await OkJsonAsync(owner, $"/api/v1/sales/sales?from={d}&to={d}");
        list.GetProperty("total").GetInt32().Should().Be(2);
        var totals = list.GetProperty("totals");
        totals.GetProperty("total").GetDecimal().Should().Be(20m);
        totals.GetProperty("cancelledCount").GetInt32().Should().Be(1);
        totals.GetProperty("cancelledTotal").GetDecimal().Should().Be(10m);
        totals.GetProperty("cost").GetDecimal().Should().Be(8m);
        totals.GetProperty("margin").GetDecimal().Should().Be(12m);
        var rowA = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == a.Value.SaleId);
        rowA.GetProperty("paymentMethods").EnumerateArray().Single().GetString().Should().StartWith("Pix ");
        rowA.GetProperty("cost").GetDecimal().Should().Be(8m);

        // CA 6: o CPF vem mascarado na lista.
        rowA.GetProperty("customerDocument").GetString().Should().Be("***.982.247-**");
        list.GetRawText().Should().NotContain(ValidCpf);

        // Filtros: forma de pagamento, estado e página pequena (os totais continuam do filtro, não da página).
        (await OkJsonAsync(owner, $"/api/v1/sales/sales?from={d}&to={d}&paymentMethodId={methods.Cash}")).GetProperty("total").GetInt32().Should().Be(1);
        var cancelled = await OkJsonAsync(owner, $"/api/v1/sales/sales?from={d}&to={d}&state=Cancelled");
        cancelled.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(b.Value.SaleId);
        var paged = await OkJsonAsync(owner, $"/api/v1/sales/sales?from={d}&to={day.AddDays(1):yyyy-MM-dd}&pageSize=1");
        paged.GetProperty("items").GetArrayLength().Should().Be(1);
        paged.GetProperty("total").GetInt32().Should().Be(3);
        paged.GetProperty("totals").GetProperty("total").GetDecimal().Should().Be(30m);

        // CA 5: sem sales.view_cost, custo e margem não vêm (ausentes, não zerados) na lista, no detalhe nem no arquivo.
        var seller = await SalesActorAsync("sales.view");
        var plain = await OkJsonAsync(seller, $"/api/v1/sales/sales?from={d}&to={d}");
        plain.GetProperty("totals").TryGetProperty("cost", out _).Should().BeFalse();
        plain.GetProperty("totals").TryGetProperty("margin", out _).Should().BeFalse();
        plain.GetProperty("items").EnumerateArray().All(i => !i.TryGetProperty("cost", out _) && !i.TryGetProperty("margin", out _)).Should().BeTrue();
        var plainDetail = await OkJsonAsync(seller, $"/api/v1/sales/sales/{a.Value.SaleId}");
        plainDetail.TryGetProperty("cost", out _).Should().BeFalse();
        plainDetail.GetProperty("lines").EnumerateArray().All(l => !l.TryGetProperty("unitCost", out _)).Should().BeTrue();
        var file = await seller.GetAsync($"/api/v1/sales/sales/export?format=xlsx&from={d}&to={d}");
        file.StatusCode.Should().Be(HttpStatusCode.OK, await file.Content.ReadAsStringAsync());

        // Período inválido: 422 com a mensagem.
        (await owner.GetAsync($"/api/v1/sales/sales?from={d}&to={day.AddDays(-1):yyyy-MM-dd}")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Pdv07_detalhe_traz_itens_pagamentos_trilha_e_o_CPF_inteiro_so_pela_leitura_auditada()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 1, 5m);
        var day = Today.AddDays(-Random.Shared.Next(200, 290));
        var sale = await IngestAsync(Online(day, location, [new SaleLineInput(Guid.CreateVersion7(), product, 3, 9m, 0, ListUnitPrice: 10m, PriceReason: "Cliente fiel")],
            [Paid(methods.Credit, 27m, brand: "Visa")]));
        sale.IsSuccess.Should().BeTrue();
        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<ISaleCustomerDocuments>().AttachAsync(new SaleCustomerDocumentInput(sale.Value.SaleId, "cpf", ValidCpf))).IsSuccess.Should().BeTrue();
        }

        await DrainAsync();

        // CA 4: itens com o preço da tabela e o praticado, pagamento com a forma e a trilha (estoque com falta de saldo: 1 em saldo, 3 vendidos).
        var detail = await OkJsonAsync(owner, $"/api/v1/sales/sales/{sale.Value.SaleId}");
        var line = detail.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("listUnitPrice").GetDecimal().Should().Be(10m);
        line.GetProperty("unitPrice").GetDecimal().Should().Be(9m);
        line.GetProperty("priceReason").GetString().Should().Be("Cliente fiel");
        detail.GetProperty("payments").EnumerateArray().Single().GetProperty("methodName").GetString().Should().StartWith("Crédito ");
        var trail = detail.GetProperty("trail").EnumerateArray().ToList();
        trail.Should().Contain(t => t.GetProperty("kind").GetString() == "stock" && t.GetProperty("title").GetString() == "Estoque: falta de saldo");
        trail.Should().Contain(t => t.GetProperty("kind").GetString() == "finance");
        detail.GetProperty("stockStatus").GetString().Should().Be("Shortfall");

        // CA 6: o detalhe traz a máscara; o inteiro só pela rota, com a leitura na auditoria.
        detail.GetProperty("customerDocument").GetString().Should().Be("***.982.247-**");
        detail.GetRawText().Should().NotContain(ValidCpf);
        var full = await OkJsonAsync(owner, $"/api/v1/sales/sales/{sale.Value.SaleId}/customer-document");
        full.GetProperty("document").GetString().Should().Be("529.982.247-25");
        await using (var scope = await ScopeAsync())
        {
            var reads = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries.AsNoTracking()
                .Where(e => e.Entity == "SaleCustomerDocument" && e.EntityId == sale.Value.SaleId.ToString() && e.Action == AuditActions.SensitiveRead).CountAsync();
            reads.Should().Be(1);
        }

        // Venda de outro id: 404, sem vazar nada.
        (await owner.GetAsync($"/api/v1/sales/sales/{Guid.CreateVersion7()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/sales/sales/{Guid.CreateVersion7()}/customer-document")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pdv07_cards_da_loja_aparecem_com_o_perfil_Comercio_e_somem_sem_ele()
    {
        var (_, owner) = await OwnerAsync();
        var methods = await MethodsAsync(owner);
        var location = await LocationAsync();
        var product = await ProductAsync(location, 5, 1m);
        var today = Today;
        var request = Online(today, location, [Line(product, 1, 12.34m)], [Paid(methods.Pix, 12.34m)]) with { CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        (await IngestAsync(request)).IsSuccess.Should().BeTrue();
        try
        {
            // CA 8: vendas de hoje, ticket médio e caixas abertos pelos contratos (o tenant é compartilhado: pelo menos esta venda).
            var card = await OkJsonAsync(owner, "/api/v1/dashboard/cards/store-sales");
            card.GetProperty("today").GetString().Should().Be(today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            card.GetProperty("todayCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
            card.GetProperty("todayAmount").GetDecimal().Should().BeGreaterThanOrEqualTo(12.34m);
            card.GetProperty("ticketToday").ValueKind.Should().Be(JsonValueKind.Number);
            card.GetProperty("openRegisters").ValueKind.Should().Be(JsonValueKind.Array);

            // CA 11: sem o perfil Comércio, o módulo sales não é liberado e o card não existe (403 module_not_released).
            await CommerceAsync(false);
            var denied = await owner.GetAsync("/api/v1/dashboard/cards/store-sales");
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await denied.Content.ReadAsStringAsync()).Should().Contain("module_not_released");
        }
        finally
        {
            await CommerceAsync(true);
        }
    }

    private static async Task<JsonElement> OkJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Usuário num grupo novo só com as ações pedidas (ex.: sales.view sem sales.view_cost).</summary>
    private async Task<HttpClient> SalesActorAsync(params string[] permissions)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "s" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = "Vendas " + Guid.NewGuid().ToString("N")[..8], Description = "Teste da retaguarda" };
            db.Groups.Add(group);
            foreach (var module in permissions.Select(p => p.Split('.')[0]).Distinct())
            {
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
            }

            foreach (var permission in permissions)
            {
                db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = permission.Split('.')[0], Action = permission.Split('.')[1] });
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
            await db.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }
}
