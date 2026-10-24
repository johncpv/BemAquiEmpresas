using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.Integration.Tests.Attachments;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// Base dos testes do envio do pedido ao fornecedor (CMP-03b) com PostgreSQL real: antivírus e transporte de e-mail falsos, storage numa
/// pasta temporária. O pedido é aprovado pela regra padrão "Proprietário aprova tudo": pede o comprador, aprova o Proprietário.
/// </summary>
public abstract class PurchaseOrderDispatchTestBase(TenancyFixture tenancy) : PurchaseOrderTestBase(tenancy), IDisposable
{
    protected const string Buyer = "purchasing:view,create,edit";

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "bqe-cmp03b-" + Guid.NewGuid().ToString("N"));

    protected FakeScanner Scanner { get; } = new();

    protected CapturingTransport Transport { get; } = new();

    protected override void Configure(IServiceCollection services)
    {
        base.Configure(services);
        services.AddSingleton<IVirusScanner>(Scanner);
        services.AddSingleton<IEmailTransport>(Transport);
        services.Configure<FileStorageOptions>(o => o.Root = _storage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Fornecedor com o contato de cobrança <paramref name="email"/>.</summary>
    protected async Task<Guid> SupplierWithContactAsync(HttpClient owner, string email = "compras@gama.com.br")
    {
        var id = await SupplierAsync(owner);
        var contact = await owner.PostAsJsonAsync($"/api/v1/parties/{id}/contacts", new { name = "Compras Gama", email, receivesBilling = true });
        contact.IsSuccessStatusCode.Should().BeTrue(await contact.Content.ReadAsStringAsync());
        return id;
    }

    /// <summary>Pedido aprovado com uma linha (100 × R$ 12,00), frete R$ 80,00 e IPI de 5%.</summary>
    protected async Task<JsonElement> ApprovedAsync(HttpClient owner, HttpClient buyer, Guid supplier, Guid? project = null)
    {
        var item = await ItemAsync();
        var body = new
        {
            partyId = supplier, projectId = project, paymentTermId = await TermAsync("30/60/90 dias"), expectedDate = Iso(Today.AddDays(20)), freight = 80m, ipiPct = 5m,
            notes = "Entregar no portão 2.", lines = new[] { new { itemId = item, quantity = 100m, unitId = (Guid?)null, unitPrice = 12m, requestLineIds = Array.Empty<Guid>() } },
        };
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, body));
        var approved = await DecideAsync(owner, order, true);
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        return await JsonAsync(approved);
    }

    protected static object Dispatch(JsonElement order, string to, string[]? cc = null, bool confirm = false, string? message = null) =>
        new { to, cc = cc ?? [], message, confirmRecipient = confirm, version = Version(order) };

    protected static Task<HttpResponseMessage> SendAsync(HttpClient client, JsonElement order, object body, Guid? key = null) =>
        client.SendAsync(Post($"{Base}/{order.GetProperty("id").GetGuid()}/dispatches", body, key));

    protected static Guid Id(JsonElement order) => order.GetProperty("id").GetGuid();

    /// <summary>Roda o <see cref="SendEmailJob"/> do e-mail, como o Hangfire faria.</summary>
    protected async Task RunJobAsync(Guid emailLogId)
    {
        var job = ActivatorUtilities.CreateInstance<SendEmailJob>(Factory.Services);
        await job.ExecuteAsync(Tenancy.TenantA.Id, new SendEmailArgs(emailLogId), CancellationToken.None);
    }
}
