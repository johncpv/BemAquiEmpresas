using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Endpoints;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Finance;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Platform.Application.Modules;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.AppApi;

/// <summary>
/// INT-03, integração (Testcontainers + fake do Inter): a API de integração v1 com o token de aplicativo de verdade (gravado pelo hash, como a troca do
/// código grava). Uma prova por camada de acesso, o mapa escopo × rota, produtos sem custo, clientes com máscara e nome abreviado, limite das buscas
/// com o 409 <c>party_exists</c> contando, <c>PATCH</c> restrito, pedido idempotente por conexão, cobrança, cancelamento fora do estado, IDOR,
/// campos proibidos do projeto e auditoria com a origem do aplicativo.
/// </summary>
public sealed class IntegrationApiTests(BillingSlipFixture fixture) : IClassFixture<BillingSlipFixture>
{
    private const string Api = "/api/v1/integration";

    private static readonly string[] AllScopes = ConnectedAppScopes.Conecta;

    private sealed record Setup(HttpClient Owner, Guid OwnerId, Guid Customer, Guid Location, Guid Cash);

    private Setup? _setup;

    /// <summary>Perfil Comércio, módulo <c>conecta</c> por exceção, Proprietário (o autorizador), Inter, cliente cobrável, local padrão e as formas de pagamento.</summary>
    private async Task<Setup> SetupAsync()
    {
        if (_setup is not null)
        {
            return _setup;
        }

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var modules = scope.ServiceProvider.GetRequiredService<TenantModuleService>();
            (await modules.ApplyProfileAsync(fixture.TenantA.Id, BusinessProfileCatalog.Retail, null, null)).IsSuccess.Should().BeTrue();
            (await modules.SaveAsync(fixture.TenantA.Id, [new ModuleReleaseChange("conecta", true), new ModuleReleaseChange("projects", true), new ModuleReleaseChange("fiscal", true)], null, null)).IsSuccess.Should().BeTrue();
        }

        fixture.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        var (ownerId, owner) = await fixture.ActorAsync(owner: true);
        var (account, _) = await fixture.IntegrationAsync(owner);
        var customer = await BillingSlipFixture.CustomerAsync(owner);
        var location = await DefaultLocationAsync();
        var cashAccount = await Created(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new { name = "Caixa API " + Guid.NewGuid().ToString("N")[..4], kind = "Cash" }));
        var cash = await Created(await owner.PostAsJsonAsync("/api/v1/finance/payment-methods",
            new { name = "Dinheiro API", type = "Cash", destinationAccountId = cashAccount, acquirerPartyId = (Guid?)null, nfePaymentCode = (string?)null }));
        var slip = await Created(await owner.PostAsJsonAsync("/api/v1/finance/payment-methods",
            new { name = "Boleto API", type = "BankSlip", destinationAccountId = account, acquirerPartyId = (Guid?)null, nfePaymentCode = (string?)null }));
        var saved = await owner.PutAsJsonAsync("/api/v1/sales/orders/parameters", new
        {
            discountLimitPct = 10m, appDiscountLimitPct = 0m, holdHours = 48, appHoldHours = 2, chargeDays = 1, appChargeDays = 3, appMaxLineQty = 50m, appMaxOrderQty = 200m,
            appUnpaidAlertCount = 10, bankSlipPaymentMethodId = slip,
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        _setup = new Setup(owner, ownerId, customer, location, cash);
        return _setup;
    }

    private Task<Guid> DefaultLocationAsync() => fixture.QueryAsync(async db =>
    {
        var current = await db.Locations.Where(l => l.IsDefault).Select(l => (Guid?)l.Id).FirstOrDefaultAsync();
        if (current is { } id)
        {
            return id;
        }

        var l = new Location { Code = "API-" + Guid.NewGuid().ToString("N")[..5], Name = "Loja API", IsDefault = true, CreatedAt = DateTimeOffset.UtcNow };
        db.Locations.Add(l);
        await db.SaveChangesAsync();
        return l.Id;
    });

    /// <summary>
    /// Conexão ativa com os escopos dados e um token de acesso <c>bqa_</c> (só o hash no banco). Sem <paramref name="clientId"/>, um aplicativo novo
    /// (a conexão é uma por aplicativo); com <c>conecta</c>, a conexão aberta anterior é revogada.
    /// </summary>
    private async Task<HttpClient> AppAsync(Guid grantor, IEnumerable<string> scopes, string? clientId = null, string host = BillingSlipFixture.HostA)
    {
        var token = AppClaims.AccessPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        await using (var scope = await fixture.ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var code = clientId ?? "teste-" + Guid.NewGuid().ToString("N")[..8];
            var app = await db.ConnectedApps.FirstOrDefaultAsync(a => a.ClientId == code);
            if (app is null)
            {
                app = new ConnectedApp { ClientId = code, Name = code, RequiredModule = "conecta", CreatedAt = now };
                db.ConnectedApps.Add(app);
            }
            else
            {
                foreach (var open in await db.AppConnections.Where(c => c.AppId == app.Id && c.Status != AppConnectionStatus.Revoked).ToListAsync())
                {
                    open.Revoke(AppConnectionReason.Replaced, "teste", null, now);
                }
            }

            await db.SaveChangesAsync();
            var connection = AppConnection.Create(app.Id, grantor, scopes, "Loja de teste", now);
            db.AppConnections.Add(connection);
            db.AppTokens.Add(new AppToken
            {
                TokenHash = OpaqueToken.Hash(token), Kind = AppTokenKind.Access, ConnectionId = connection.Id, FamilyId = Guid.NewGuid(), CreatedAt = now,
                ExpiresAt = now + AppToken.AccessLifetime,
            });
            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> Created(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static HttpRequestMessage Post(string url, object body, Guid? key = null, bool withKey = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (withKey)
        {
            request.Headers.Add(IdempotencyFilter.Header, (key ?? Guid.NewGuid()).ToString());
        }

        return request;
    }

    private static string NewCpf()
    {
        var d = Enumerable.Range(0, 9).Select(_ => RandomNumberGenerator.GetInt32(10)).ToList();
        d[0] = d.Distinct().Count() == 1 ? (d[0] + 1) % 10 : d[0];
        int Dv(int length)
        {
            var sum = 0;
            for (var i = 0; i < length; i++)
            {
                sum += d[i] * (length + 1 - i);
            }

            return sum % 11 < 2 ? 0 : 11 - (sum % 11);
        }

        d.Add(Dv(9));
        d.Add(Dv(10));
        return string.Concat(d);
    }

    private static string NewPhone() => "+55119" + string.Concat(Enumerable.Range(0, 8).Select(_ => RandomNumberGenerator.GetInt32(10)));

    /// <summary>Produto com preço na tabela padrão e saldo no local padrão; devolve o id do produto.</summary>
    private async Task<Guid> ProductAsync(Guid location, decimal stock, decimal price = 100m)
    {
        await using var scope = await fixture.ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var item = new Item { Type = ItemType.Finished, Code = "API-" + Guid.NewGuid().ToString("N")[..6], Name = "Peça API " + Guid.NewGuid().ToString("N")[..4], UnitId = unit, Ncm = "84313110", CreatedAt = DateTimeOffset.UtcNow };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        var defaults = scope.ServiceProvider.GetRequiredService<BemAqui.Catalog.Application.CatalogDefaults>();
        var product = new BemAqui.Catalog.Domain.Product { ItemId = item.Id, CategoryId = (await defaults.GeneralCategoryAsync()).Id, SaleName = item.Name, CreatedAt = DateTimeOffset.UtcNow };
        db.Products.Add(product);
        db.PriceListItems.Add(new BemAqui.Catalog.Domain.PriceListItem { PriceListId = (await defaults.DefaultPriceListAsync()).Id, ProductId = product.Id, Price = price, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        (await scope.ServiceProvider.GetRequiredService<IStockMovements>()
            .PostAsync(new StockPosting(StockMovementType.Receipt, item.Id, location, null, stock, 40m, StockSources.GoodsReceipt, Guid.NewGuid()))).IsSuccess.Should().BeTrue();
        await transaction.CommitAsync();
        return product.Id;
    }

    // ---------- autenticação, três camadas e o mapa de escopos ----------

    [Fact]
    public async Task Uma_prova_por_camada_token_modulo_e_escopo()
    {
        var s = await SetupAsync();
        using var anonymous = fixture.Factory.CreateClient();
        anonymous.DefaultRequestHeaders.Host = BillingSlipFixture.HostA;
        var none = await anonymous.GetAsync(new Uri(Api + "/products", UriKind.Relative));
        Code(await Json(none, HttpStatusCode.Unauthorized)).Should().Be("invalid_token");
        Code(await Json(await s.Owner.GetAsync(new Uri(Api + "/products", UriKind.Relative)), HttpStatusCode.Unauthorized)).Should().Be("invalid_token", "sessão de pessoa não vale na API do aplicativo");

        var catalogOnly = await AppAsync(s.OwnerId, ["catalog.view"]);
        (await catalogOnly.GetAsync(new Uri(Api + "/products", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
        var denied = await Json(await catalogOnly.GetAsync(new Uri(Api + "/customers?phone=%2B5511999990000", UriKind.Relative)), HttpStatusCode.Forbidden);
        Code(denied).Should().Be("insufficient_scope");
        denied.GetProperty("scope").GetString().Should().Be("parties.view");

        // Token do tenant A no host do tenant B: 401 invalid_token, sem dizer se o recurso existe.
        var otherHost = await AppAsync(s.OwnerId, ["catalog.view"], host: "b.localhost");
        Code(await Json(await otherHost.GetAsync(new Uri(Api + "/products", UriKind.Relative)), HttpStatusCode.Unauthorized)).Should().Be("invalid_token");

        await fixture.SetModuleAsync("catalog", false);
        try
        {
            Code(await Json(await catalogOnly.GetAsync(new Uri(Api + "/products", UriKind.Relative)), HttpStatusCode.Forbidden)).Should().Be("module_not_released");
        }
        finally
        {
            await fixture.SetModuleAsync("catalog", true);
        }
    }

    /// <summary>CA 3: a tabela da story, rota por rota, contra o <c>RequirePermission</c> do endpoint.</summary>
    [Fact]
    public void Mapa_de_escopo_por_rota_segue_a_tabela_da_story()
    {
        var expected = new (string Method, string Route, string Scope)[]
        {
            ("GET", "/products", "catalog.view"), ("GET", "/products/{id:guid}", "catalog.view"),
            ("GET", "/customers", "parties.view"), ("GET", "/customers/{id:guid}", "parties.view"),
            ("POST", "/customers", "parties.create"), ("PATCH", "/customers/{id:guid}", "parties.edit"),
            ("POST", "/orders", "sales.create"), ("GET", "/orders/{id:guid}", "sales.view"), ("GET", "/customers/{id:guid}/orders", "sales.view"),
            ("POST", "/orders/{id:guid}/confirm", "sales.create"), ("POST", "/orders/{id:guid}/charge", "sales.edit"), ("POST", "/orders/{id:guid}/cancel", "sales.edit"),
            ("GET", "/customers/{id:guid}/charges", "finance.view"), ("GET", "/charges/{id:guid}", "finance.view"), ("GET", "/charges/{id:guid}/pdf", "finance.view"),
            ("GET", "/orders/{id:guid}/invoice", "fiscal.view"), ("GET", "/invoices/{id:guid}/danfe", "fiscal.view"),
            ("GET", "/customers/{id:guid}/projects", "projects.view"),
        };
        var endpoints = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith(Api + "/", StringComparison.Ordinal) && e.Metadata.GetMetadata<ConnectedAppEndpointAttribute>() is { Self: false })
            .Select(e => (Method: e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single(), Route: e.RoutePattern.RawText![Api.Length..].TrimEnd('/'),
                Scope: string.Join('|', e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>()
                    .Where(p => p.StartsWith(ModuleEndpoints.PermissionPolicyPrefix, StringComparison.Ordinal)).Select(p => p[ModuleEndpoints.PermissionPolicyPrefix.Length..]))))
            .ToList();

        endpoints.Should().BeEquivalentTo(expected, "toda rota de negócio da API de integração tem exatamente o escopo da tabela (CA 3)");
        expected.Select(e => e.Scope).Distinct().Should().OnlyContain(scope => AllScopes.Contains(scope), "só escopos da ADR 0053");
    }

    // ---------- catálogo ----------

    [Fact]
    public async Task Produtos_sem_custo_e_disponivel_so_com_inventory_view()
    {
        var s = await SetupAsync();
        var product = await ProductAsync(s.Location, 7m, 123.45m);
        var catalog = await AppAsync(s.OwnerId, ["catalog.view"]);
        var withStock = await AppAsync(s.OwnerId, ["catalog.view", "inventory.view"]);

        var text = await (await catalog.GetAsync(new Uri($"{Api}/products/{product}", UriKind.Relative))).Content.ReadAsStringAsync();
        var view = JsonDocument.Parse(text).RootElement;
        view.GetProperty("price").GetDecimal().Should().Be(123.45m);
        view.GetProperty("code").GetString().Should().StartWith("API-");
        view.TryGetProperty("available", out _).Should().BeFalse("sem inventory.view o campo não vem");
        text.Should().NotContainAny(["cost", "Cost", "margin", "Margin"]);

        var stock = await Json(await withStock.GetAsync(new Uri($"{Api}/products/{product}", UriKind.Relative)), HttpStatusCode.OK);
        stock.GetProperty("available").GetDecimal().Should().Be(7m);

        var page = await Json(await catalog.GetAsync(new Uri($"{Api}/products?q=Peça API&page=1&pageSize=500", UriKind.Relative)), HttpStatusCode.OK);
        page.GetProperty("page").GetInt32().Should().Be(1);
        page.GetProperty("pageSize").GetInt32().Should().Be(100, "pageSize até 100");
        page.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(product);
        (await catalog.GetAsync(new Uri($"{Api}/products/{Guid.NewGuid()}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- clientes ----------

    [Fact]
    public async Task Cliente_pelo_telefone_mascarado_pelo_documento_abreviado_e_auditado()
    {
        var s = await SetupAsync();
        var app = await AppAsync(s.OwnerId, ["parties.view", "parties.create"]);
        var (cpf, phone) = (NewCpf(), NewPhone());
        var created = await Json(await app.SendAsync(Post(Api + "/customers", new
        {
            kind = "PF", name = "Maria da Silva Oliveira", document = cpf, phone, email = "maria@cliente.com.br",
            address = new { zipCode = "01310100", street = "Avenida Paulista", number = "1000", complement = (string?)null, district = "Bela Vista", city = "São Paulo", state = "SP" },
        })), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("canBill").GetBoolean().Should().BeTrue();

        var byPhone = (await Json(await app.GetAsync(new Uri($"{Api}/customers?phone={Uri.EscapeDataString(phone)}", UriKind.Relative)), HttpStatusCode.OK)).EnumerateArray().Single();
        byPhone.GetProperty("id").GetGuid().Should().Be(id);
        byPhone.GetProperty("name").GetString().Should().Be("Maria da Silva Oliveira");
        byPhone.GetProperty("document").GetString().Should().Be($"***.{cpf[3..6]}.{cpf[6..9]}-**");
        byPhone.GetProperty("city").GetString().Should().Be("São Paulo");
        byPhone.TryGetProperty("email", out _).Should().BeFalse("nunca outros contatos");
        byPhone.TryGetProperty("street", out _).Should().BeFalse("nunca o endereço completo");

        var byDocument = (await Json(await app.GetAsync(new Uri($"{Api}/customers?document={cpf}", UriKind.Relative)), HttpStatusCode.OK)).EnumerateArray().Single();
        byDocument.GetProperty("name").GetString().Should().Be("Maria S. O.");
        byDocument.TryGetProperty("document", out _).Should().BeFalse();
        byDocument.TryGetProperty("city", out _).Should().BeFalse();
        byDocument.GetProperty("canInvoice").GetBoolean().Should().BeTrue();

        var national = phone[3..];
        var audit = await fixture.QueryAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == IntegrationLookupService.AuditEntity || (a.Entity == "Party" && a.EntityId == id.ToString()))
            .ToListAsync());
        audit.Should().Contain(a => a.Entity == "Party" && a.Action == "Created" && a.Origin!.StartsWith("Integração teste-", StringComparison.Ordinal) && a.UserId == s.OwnerId,
            "a escrita mostra Integração {aplicativo} (autorizada por {pessoa}) e o autorizador");
        audit.Where(a => a.Entity == IntegrationLookupService.AuditEntity).Should().Contain(a => a.Changes.Contains("document:", StringComparison.Ordinal))
            .And.OnlyContain(a => !a.Changes.Contains(cpf, StringComparison.Ordinal) && !a.Changes.Contains(national, StringComparison.Ordinal), "o termo vai em hash");
    }

    [Fact]
    public async Task Busca_passa_do_limite_e_o_party_exists_conta_no_mesmo_limite()
    {
        var s = await SetupAsync();
        var app = await AppAsync(s.OwnerId, ["parties.view", "parties.create"]);
        var cpf = NewCpf();
        await Json(await app.SendAsync(Post(Api + "/customers", new { kind = "PF", name = "Cliente Limite", document = cpf })), HttpStatusCode.Created);

        for (var i = 0; i < 29; i++)
        {
            (await app.GetAsync(new Uri($"{Api}/customers?phone=%2B551199999{i:0000}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var exists = await Json(await app.SendAsync(Post(Api + "/customers", new { kind = "PF", name = "Outra pessoa", document = cpf })), HttpStatusCode.Conflict);
        Code(exists).Should().Be("party_exists");
        exists.TryGetProperty("existingId", out _).Should().BeTrue();
        exists.TryGetProperty("existingName", out _).Should().BeFalse("o 409 não revela o nome");

        var limited = await app.GetAsync(new Uri($"{Api}/customers?document={cpf}", UriKind.Relative));
        Code(await Json(limited, HttpStatusCode.TooManyRequests)).Should().Be("rate_limited");
        limited.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Patch_so_preenche_vazio_e_documento_nunca_muda()
    {
        var s = await SetupAsync();
        var app = await AppAsync(s.OwnerId, ["parties.view", "parties.create", "parties.edit"]);
        var id = (await Json(await app.SendAsync(Post(Api + "/customers", new { kind = "PF", name = "Cliente Patch", document = NewCpf() })), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        (await app.PatchAsJsonAsync($"{Api}/customers/{id}", new { email = "primeiro@cliente.com.br" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.PatchAsJsonAsync($"{Api}/customers/{id}", new { email = "primeiro@cliente.com.br" })).StatusCode.Should().Be(HttpStatusCode.OK, "o mesmo valor não muda nada");
        var change = await Json(await app.PatchAsJsonAsync($"{Api}/customers/{id}", new { email = "outro@cliente.com.br" }), HttpStatusCode.UnprocessableEntity);
        Code(change).Should().Be("field_change_requires_backoffice");
        Code(await Json(await app.PatchAsJsonAsync($"{Api}/customers/{id}", new { document = NewCpf() }), HttpStatusCode.UnprocessableEntity))
            .Should().Be("field_change_requires_backoffice");
        (await fixture.QueryAsync(db => db.Parties.Where(p => p.Id == id).Select(p => p.Email).SingleAsync())).Should().Be("primeiro@cliente.com.br");
        (await app.PatchAsJsonAsync($"{Api}/customers/{Guid.NewGuid()}", new { email = "x@y.com" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- pedidos, cobrança e cancelamento ----------

    [Fact]
    public async Task Pedido_idempotente_por_conexao_e_corpo_diferente_responde_409()
    {
        var s = await SetupAsync();
        var product = await ProductAsync(s.Location, 20m);
        var app = await AppAsync(s.OwnerId, AllScopes, "conecta");
        var external = "conv-" + Guid.NewGuid().ToString("N")[..8];
        object Body(decimal quantity) => new { customerId = s.Customer, externalId = external, lines = new[] { new { productId = product, quantity, unitPrice = 1m } } };

        Code(await Json(await app.SendAsync(Post(Api + "/orders", Body(1), withKey: false)), HttpStatusCode.BadRequest)).Should().Be("idempotency_key_required");

        var key = Guid.NewGuid();
        var first = await Json(await app.SendAsync(Post(Api + "/orders", Body(1), key)), HttpStatusCode.Created);
        first.GetProperty("status").GetString().Should().Be("Draft");
        first.GetProperty("origin").GetString().Should().Be("Conecta", "a origem vem do token");
        first.GetProperty("message").GetString().Should().Be("Pedido do Conecta: confirme com o cliente.");
        first.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal().Should().Be(100m, "o preço enviado é ignorado");

        var again = await Json(await app.SendAsync(Post(Api + "/orders", Body(1), key)), HttpStatusCode.Created);
        again.GetProperty("id").GetGuid().Should().Be(first.GetProperty("id").GetGuid());
        Code(await Json(await app.SendAsync(Post(Api + "/orders", Body(2), key)), HttpStatusCode.Conflict)).Should().Be("idempotency_key_reused");
        Code(await Json(await app.SendAsync(Post(Api + "/orders", Body(3))), HttpStatusCode.Conflict)).Should().Be("external_id_conflict");
        (await fixture.QueryAsync(db => db.SalesOrders.CountAsync(o => o.ExternalId == external))).Should().Be(1);
        (await app.SendAsync(Post(Api + "/orders", new { customerId = s.Customer, externalId = "x", origin = "Backoffice", lines = Array.Empty<object>() })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "origem no corpo é recusada");
    }

    [Fact]
    public async Task Pedido_confirmado_cobrado_e_cancelado_e_o_pago_so_cancela_no_erp()
    {
        var s = await SetupAsync();
        var product = await ProductAsync(s.Location, 20m);
        var app = await AppAsync(s.OwnerId, AllScopes, "conecta");

        var order = await Json(await app.SendAsync(Post(Api + "/orders", new { customerId = s.Customer, externalId = "c-" + Guid.NewGuid().ToString("N")[..8], lines = new[] { new { productId = product, quantity = 2m } } })),
            HttpStatusCode.Created);
        var id = order.GetProperty("id").GetGuid();
        var confirmed = await Json(await app.PostAsJsonAsync($"{Api}/orders/{id}/confirm", new { }), HttpStatusCode.OK);
        confirmed.GetProperty("status").GetString().Should().Be("Open");
        confirmed.GetProperty("dates").GetProperty("confirmedAt").ValueKind.Should().Be(JsonValueKind.String);

        var charged = await Json(await app.SendAsync(Post($"{Api}/orders/{id}/charge", new { sendEmail = false })), HttpStatusCode.Accepted);
        charged.GetProperty("status").GetString().Should().Be("AwaitingPayment");
        var receivable = charged.GetProperty("charge").GetProperty("id").GetGuid();
        charged.GetProperty("charge").GetProperty("slipStatus").GetString().Should().Be("Requested");

        var slipId = await fixture.QueryAsync(db => db.BillingSlips.Where(b => b.ReceivableId == receivable).Select(b => b.Id).SingleAsync());
        await fixture.IssueJobAsync(slipId);
        var detail = await app.GetAsync(new Uri($"{Api}/charges/{receivable}", UriKind.Relative));
        detail.Headers.CacheControl!.NoStore.Should().BeTrue();
        var text = await detail.Content.ReadAsStringAsync();
        detail.StatusCode.Should().Be(HttpStatusCode.OK, text);
        text.Should().NotContainAny(["provider", "Provider", "payload"]);
        JsonDocument.Parse(text).RootElement.GetProperty("digitableLine").GetString().Should().NotBeNullOrEmpty();
        (await app.GetAsync(new Uri($"{Api}/charges/{receivable}?customerId={Guid.NewGuid()}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound, "de outro cliente: 404");
        (await Json(await app.GetAsync(new Uri($"{Api}/customers/{s.Customer}/charges", UriKind.Relative)), HttpStatusCode.OK)).EnumerateArray()
            .Should().Contain(c => c.GetProperty("id").GetGuid() == receivable);

        (await app.GetAsync(new Uri($"{Api}/orders/{id}/invoice", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound, "a nota do pedido chega com a VND-02b");
        (await Json(await app.GetAsync(new Uri($"{Api}/customers/{s.Customer}/orders", UriKind.Relative)), HttpStatusCode.OK)).EnumerateArray()
            .Should().Contain(o => o.GetProperty("id").GetGuid() == id);

        var cancelled = await Json(await app.PostAsJsonAsync($"{Api}/orders/{id}/cancel", new { reason = "Cliente desistiu" }), HttpStatusCode.OK);
        cancelled.GetProperty("status").GetString().Should().Be("Cancelled");
        Code(await Json(await app.PostAsJsonAsync($"{Api}/orders/{id}/cancel", new { reason = "De novo" }), HttpStatusCode.Conflict)).Should().Be("invalid_order_transition");

        // Pago (registrado por pessoa no ERP): o aplicativo não cancela.
        var paidOrder = (await Json(await app.SendAsync(Post(Api + "/orders", new { customerId = s.Customer, externalId = "p-" + Guid.NewGuid().ToString("N")[..8], lines = new[] { new { productId = product, quantity = 1m } } })),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var open = await Json(await app.PostAsJsonAsync($"{Api}/orders/{paidOrder}/confirm", new { }), HttpStatusCode.OK);
        await Json(await s.Owner.SendAsync(Post($"/api/v1/sales/orders/{paidOrder}/payments", new { paymentMethodId = s.Cash, version = open.GetProperty("version").GetUInt32() })),
            HttpStatusCode.OK);
        var refused = await Json(await app.PostAsJsonAsync($"{Api}/orders/{paidOrder}/cancel", new { reason = "Quero cancelar" }), HttpStatusCode.Conflict);
        Code(refused).Should().Be("order_cancel_requires_backoffice");
    }

    [Fact]
    public async Task Ids_de_outro_cliente_inexistentes_ou_de_quem_nao_e_cliente_dao_404_igual()
    {
        var s = await SetupAsync();
        var app = await AppAsync(s.OwnerId, AllScopes);
        var supplier = await Created(await s.Owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = BillingSlipFixture.NewCnpj(), legalName = "Fornecedor API", tradeName = (string?)null, roles = new[] { "Supplier" } }));

        foreach (var url in new[]
                 {
                     $"{Api}/customers/{supplier}", $"{Api}/customers/{supplier}/orders", $"{Api}/customers/{supplier}/charges", $"{Api}/customers/{supplier}/projects",
                     $"{Api}/orders/{Guid.NewGuid()}", $"{Api}/charges/{Guid.NewGuid()}", $"{Api}/invoices/{Guid.NewGuid()}/danfe", $"{Api}/customers/{Guid.NewGuid()}",
                 })
        {
            var problem = await Json(await app.GetAsync(new Uri(url, UriKind.Relative)), HttpStatusCode.NotFound);
            Code(problem).Should().Be("not_found", url);
            problem.GetProperty("detail").GetString().Should().Be(IntegrationApi.NotFoundMessage, url);
        }
    }

    // ---------- projeto ----------

    [Fact]
    public async Task Projeto_do_cliente_so_com_o_dto_minimo()
    {
        var s = await SetupAsync();
        var app = await AppAsync(s.OwnerId, AllScopes);
        (await Json(await app.GetAsync(new Uri($"{Api}/customers/{s.Customer}/projects", UriKind.Relative)), HttpStatusCode.OK)).GetArrayLength().Should().Be(0);

        var forbidden = new[] { "cost", "budget", "margin", "value", "amount", "price", "total", "revenue", "orcado", "custo", "valor" };
        typeof(IntegrationProjectStatus).GetProperties().Select(p => p.Name.ToLowerInvariant()).Should()
            .NotContain(name => forbidden.Any(f => name.Contains(f, StringComparison.Ordinal)), "o DTO do projeto nunca leva custo, orçado, margem ou valor (CA 19)");
        var view = IntegrationProjectEndpoints.Map(
            new BemAqui.Commercial.Contracts.ProjectSummary(Guid.NewGuid(), "PRJ-2026-0001", "Elevador", s.Customer, Guid.NewGuid(), "Fabricated", null, PromisedDate: new DateOnly(2020, 1, 1)),
            new DateOnly(2026, 11, 27));
        view.Should().Be(new IntegrationProjectStatus("PRJ-2026-0001", "Elevador", "F", "Fabricado", "Atrasado", new DateOnly(2020, 1, 1)));
    }
}
