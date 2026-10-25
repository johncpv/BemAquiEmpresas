using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// Base dos testes da cotação (CMP-02) com PostgreSQL real, sobre a base do pedido (fornecedor, item, SC, atores e alçadas). Cada teste
/// cria os próprios fornecedores, itens, SC e cotações e filtra pelos ids dele.
/// </summary>
public abstract class RfqTestBase(TenancyFixture tenancy) : PurchaseOrderTestBase(tenancy)
{
    protected const string Rfqs = "/api/v1/purchasing/rfqs";
    protected const string Buyer = "purchasing:view,create,edit";

    protected sealed record Quote(Guid LineId, decimal UnitPrice, int LeadDays = 5, decimal? Available = null, bool InSupplierUnit = false);

    protected sealed record Pick(Guid LineId, Guid SupplierId, string? Reason = null, string? Note = null);

    protected static async Task<JsonElement> CreateRfqAsync(HttpClient buyer, Guid[] requestLineIds, Guid[] suppliers, Guid? key = null, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var response = await buyer.SendAsync(Post(Rfqs, new { requestLineIds, lines = Array.Empty<object>(), supplierIds = suppliers, notes = (string?)null }, key));
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    protected static async Task<JsonElement> CreateTypedRfqAsync(HttpClient buyer, (Guid Item, decimal Quantity)[] lines, Guid[] suppliers)
    {
        var response = await buyer.SendAsync(Post(Rfqs, new
        {
            requestLineIds = Array.Empty<Guid>(), lines = lines.Select(l => new { itemId = l.Item, quantity = l.Quantity, unitId = (Guid?)null }), supplierIds = suppliers, notes = (string?)null,
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    protected static async Task<JsonElement> GetRfqAsync(HttpClient client, JsonElement rfq)
    {
        var response = await client.GetAsync($"{Rfqs}/{Id(rfq)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    protected static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    /// <summary>O fornecedor da cotação (<c>rfq_suppliers.id</c>) de um terceiro.</summary>
    protected static Guid SupplierRow(JsonElement rfq, Guid partyId) =>
        rfq.GetProperty("suppliers").EnumerateArray().Single(s => s.GetProperty("partyId").GetGuid() == partyId).GetProperty("id").GetGuid();

    protected static JsonElement SupplierOf(JsonElement rfq, Guid partyId) =>
        rfq.GetProperty("suppliers").EnumerateArray().Single(s => s.GetProperty("partyId").GetGuid() == partyId);

    protected static Guid LineOf(JsonElement rfq, Guid itemId) =>
        rfq.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("itemId").GetGuid() == itemId).GetProperty("id").GetGuid();

    protected static JsonElement Line(JsonElement rfq, Guid lineId) => rfq.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == lineId);

    protected static Task<HttpResponseMessage> RespondRawAsync(HttpClient buyer, JsonElement rfq, Guid supplierRow, Quote[] quotes, decimal freight = 0m, decimal ipiPct = 0m, uint? version = null) =>
        buyer.PutAsJsonAsync($"{Rfqs}/{Id(rfq)}/suppliers/{supplierRow}/response", new
        {
            freight, ipiPct, paymentTermId = (Guid?)null, validUntil = Iso(Today.AddDays(30)), notes = (string?)null,
            quotes = quotes.Select(q => new { lineId = q.LineId, unitPrice = q.UnitPrice, inSupplierUnit = q.InSupplierUnit, availableQuantity = q.Available, leadDays = q.LeadDays }),
            version = version ?? Version(rfq),
        });

    protected static async Task<JsonElement> RespondAsync(HttpClient buyer, JsonElement rfq, Guid supplierRow, Quote[] quotes, decimal freight = 0m, decimal ipiPct = 0m)
    {
        var response = await RespondRawAsync(buyer, rfq, supplierRow, quotes, freight, ipiPct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    protected static Task<HttpResponseMessage> CloseRawAsync(HttpClient buyer, JsonElement rfq, params Pick[] picks) =>
        buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/close", new
        {
            selections = picks.Select(p => new { lineId = p.LineId, supplierId = p.SupplierId, reason = p.Reason, note = p.Note }), version = Version(rfq),
        });

    protected static async Task<JsonElement> CloseAsync(HttpClient buyer, JsonElement rfq, params Pick[] picks)
    {
        var response = await CloseRawAsync(buyer, rfq, picks);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    protected static async Task<JsonElement> GenerateAsync(HttpClient buyer, JsonElement rfq, Guid? key = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await buyer.SendAsync(Post($"{Rfqs}/{Id(rfq)}/purchase-orders", new { version = Version(rfq) }, key));
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    /// <summary>Anexo da proposta do fornecedor, direto no banco (o upload e o antivírus são cobertos pelos testes de anexos).</summary>
    protected Task<Guid> AttachAsync(Guid supplierRow, ScanStatus status = ScanStatus.Clean) => WithDbAsync(async db =>
    {
        var attachment = new Attachment
        {
            OwnerType = "Rfq", OwnerId = supplierRow, FileName = "proposta.pdf", ContentType = "application/pdf", Size = 10, Sha256 = Guid.NewGuid().ToString("N"),
            StoragePath = "teste/" + Guid.NewGuid().ToString("N"), ScanStatus = status, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync();
        return attachment.Id;
    });

    /// <summary>O antivírus recusou o anexo depois (o arquivo some da lista): a conclusão confere de novo (CA 6).</summary>
    protected Task<int> InfectAsync(Guid attachmentId) =>
        WithDbAsync(db => db.Attachments.Where(a => a.Id == attachmentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.ScanStatus, ScanStatus.Infected)));

    /// <summary>Linhas do histórico de uma entidade da cotação (busca pelo tipo e filtra na memória pelo id).</summary>
    protected Task<List<BemAqui.BuildingBlocks.Persistence.Auditing.AuditEntry>> AuditOfAsync(string entity, Guid id) => WithDbAsync(async db =>
        (await db.AuditEntries.AsNoTracking().Where(a => a.Entity == entity).ToListAsync()).Where(a => a.EntityId == id.ToString()).ToList());
}
