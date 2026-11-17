using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Sales;

/// <summary>
/// VND-01: núcleo de vendas sem caixa, com PostgreSQL real (movimenta estoque e financeiro). O tenant A da classe ganha o perfil Comércio; cada
/// teste usa um dia de negócio próprio (o recebível agrupa por dia e canal) e os próprios itens, locais e formas de pagamento.
/// </summary>
public partial class SalesTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    /// <summary>Meio-dia (São Paulo) de um dia passado: o dia de negócio da venda do teste.</summary>
    private static DateTimeOffset NoonOf(DateOnly day) => BusinessTime.StartOfDayUtc(day).AddHours(12);

    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Liga ou desliga o perfil Comércio do tenant A (ele nasce só com <c>factory</c>, como a Konebras).</summary>
    private async Task CommerceAsync(bool on)
    {
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<BemAqui.Platform.Application.Modules.TenantModuleService>();
        var result = on
            ? await service.ApplyProfileAsync(tenancy.TenantA.Id, BusinessProfileCatalog.Retail, null, null)
            : await service.RemoveProfileAsync(tenancy.TenantA.Id, BusinessProfileCatalog.Retail, null, null);
        result.IsSuccess.Should().BeTrue();
        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>Usuário Proprietário do tenant A.</summary>
    private async Task<(Guid Id, HttpClient Client)> OwnerAsync()
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "v" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var groupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    /// <summary>Item de revenda em UN (o adaptador provisório usa o id do item como id do produto) e, com estoque, a entrada no local.</summary>
    private async Task<Guid> ItemAsync(Guid? location = null, decimal quantity = 0, decimal cost = 0, ItemType type = ItemType.Finished, bool tracksLot = false)
    {
        Guid id;
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
            var item = new Item { Type = type, Code = Unique("PRD"), Name = "Produto " + Guid.NewGuid().ToString("N")[..4], UnitId = unit, Ncm = "84313110", TracksLot = tracksLot, CreatedAt = DateTimeOffset.UtcNow };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            id = item.Id;
        }

        if (location is { } loc && quantity > 0)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var posted = await scope.ServiceProvider.GetRequiredService<IStockMovements>()
                .PostAsync(new StockPosting(StockMovementType.Receipt, id, loc, null, quantity, cost, StockSources.GoodsReceipt, Guid.NewGuid()));
            posted.IsSuccess.Should().BeTrue();
            await transaction.CommitAsync();
        }

        return id;
    }

    private async Task<Guid> LocationAsync()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var location = new Location { Code = Unique("LJ"), Name = "Loja " + Guid.NewGuid().ToString("N")[..4], CreatedAt = DateTimeOffset.UtcNow };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    /// <summary>Formas do teste: Pix (sem adquirente), Dinheiro (troco) e Crédito com taxa de 3% em D+30 numa adquirente.</summary>
    private sealed record Methods(Guid Pix, Guid Cash, Guid Credit, Guid Acquirer);

    private async Task<Methods> MethodsAsync(HttpClient owner)
    {
        await CommerceAsync(true);
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var bank = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts",
            new { name = "Banco " + suffix, kind = "Bank", bankCode = "077", agency = "1234-5", account = "1" + suffix[..5] + "-9", isDefault = false }));
        var cash = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new { name = "Caixa " + suffix, kind = "Cash" }));
        var acquirer = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Maquininha " + suffix, roles = new[] { "Acquirer" } }));
        object Method(string name, string type, Guid account, Guid? acq = null) => new { name, type, destinationAccountId = account, acquirerPartyId = acq, nfePaymentCode = (string?)null };
        var pix = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/payment-methods", Method("Pix " + suffix, "Pix", bank)));
        var money = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/payment-methods", Method("Dinheiro " + suffix, "Cash", cash)));
        var credit = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/payment-methods", Method("Crédito " + suffix, "Credit", bank, acquirer)));
        var fee = await owner.PostAsJsonAsync($"/api/v1/finance/payment-methods/{credit}/fees",
            new { brand = "Any", installments = 1, feePercent = 3m, settlementDays = 30, validFrom = Today.AddDays(-400).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) });
        fee.StatusCode.Should().Be(HttpStatusCode.OK, await fee.Content.ReadAsStringAsync());
        return new Methods(pix, money, credit, acquirer);
    }

    private static SaleIngestionRequest Online(DateOnly day, Guid location, IReadOnlyList<SaleLineInput> lines, IReadOnlyList<SalePaymentInput> payments, Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), SaleChannel.Online, Unique("PED"), "OnlineOrder", Guid.NewGuid(), NoonOf(day), lines, payments, StockLocationId: location);

    private static SaleLineInput Line(Guid product, decimal quantity, decimal price, decimal discount = 0) => new(Guid.CreateVersion7(), product, quantity, price, discount);

    private static SalePaymentInput Paid(Guid method, decimal amount, decimal change = 0, string? brand = null) =>
        new(Guid.CreateVersion7(), method, amount, SalePaymentStatus.Captured, PaymentIntegration.Manual, change, CardBrand: brand);

    /// <summary>Ingestão como um módulo chamaria: numa transação própria, com commit só no sucesso.</summary>
    private async Task<Result<SaleIngestionResult>> IngestAsync(SaleIngestionRequest request)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await scope.ServiceProvider.GetRequiredService<ISaleIngestion>().IngestAsync(request);
        if (result.IsSuccess)
        {
            await transaction.CommitAsync();
        }

        return result;
    }

    private async Task<Result<SaleView>> CancelAsync(Guid saleId, bool refunded = true)
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<ISaleIngestion>().CancelAsync(new SaleCancellationRequest(saleId, "Cliente desistiu", refunded));
    }

    /// <summary>Processa o outbox do tenant até não sobrar mensagem pendente; nenhum consumidor pode falhar.</summary>
    private async Task DrainAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        for (var round = 0; round < 60; round++)
        {
            await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
            {
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(info);
            }

            var pending = await WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= DateTimeOffset.UtcNow.AddSeconds(1)));
            if (pending == 0)
            {
                var errors = await WithDbAsync(db => db.OutboxMessages.Where(m => m.ProcessedAt == null && m.DeadLetteredAt == null).Select(m => m.Type + ": " + m.Error).ToListAsync());
                errors.Should().BeEmpty("nenhum consumidor pode falhar");
                return;
            }

            await Task.Delay(250);
        }
    }

    private Task<decimal> OnHandAsync(Guid item, Guid location) =>
        WithDbAsync(db => db.StockPositions.AsNoTracking().Where(p => p.ItemId == item && p.LocationId == location).Select(p => p.QuantityOnHand).FirstOrDefaultAsync());
}
