using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Assets.Application;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Fiscal.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Attachments;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Assets;

/// <summary>
/// PAT-01a: cadastro de bens (etiqueta PAT, sugestões, responsável, locais, série, NF de entrada, foto sem EXIF/GPS, lista, exclusão,
/// seed patch, contratos e histórico). Os testes da classe compartilham o tenant A: contagens relativas ao início de cada teste.
/// </summary>
public partial class AssetTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/assets/assets";
    private const string Sites = "/api/v1/assets/sites";

    /// <summary>Bens "com histórico" para o CA 12 (os provedores reais vêm na PAT-02 e na PAT-03a).</summary>
    private static readonly ConcurrentDictionary<Guid, string> InUse = new();

    /// <summary>Notas de entrada importadas (a FIS-04a ainda não chegou): o seletor do CA 7 lê daqui.</summary>
    private static readonly ConcurrentDictionary<Guid, IncomingInvoiceSummary> Invoices = new();

    private sealed class FakeUsage : IAssetUsageProvider
    {
        public Task<string?> UsageAsync(Guid assetId, CancellationToken cancellationToken = default) => Task.FromResult(InUse.TryGetValue(assetId, out var usage) ? usage : null);
    }

    private sealed class FakeInvoices : IIncomingInvoices
    {
        public Task<IReadOnlyList<IncomingInvoiceSummary>> SearchAsync(string? term, int limit = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IncomingInvoiceSummary>>(Invoices.Values.Where(i => term is null || i.Number.Contains(term, StringComparison.Ordinal)).Take(limit).ToList());

        public Task<IReadOnlyList<IncomingInvoiceSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IncomingInvoiceSummary>>(ids.Where(Invoices.ContainsKey).Select(id => Invoices[id]).ToList());
    }

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.AddScoped<IAssetUsageProvider, FakeUsage>();
        s.AddScoped<IIncomingInvoices, FakeInvoices>();
        s.AddSingleton<IVirusScanner>(new FakeScanner());
    }));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static string Unique(string name) => name + " " + Guid.NewGuid().ToString("N")[..6];

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>
    /// Usuário do tenant A num grupo do seed (pela <c>seed_key</c>), num grupo próprio do módulo <c>assets</c> com as ações dadas
    /// ("assets:view,create") ou sem grupo.
    /// </summary>
    private async Task<HttpClient> ActorAsync(string? group = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "p" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (group is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (group.StartsWith("assets:", StringComparison.Ordinal))
            {
                var custom = new Group { Name = "Patrimônio " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(custom);
                db.GroupModules.Add(new GroupModule { GroupId = custom.Id, ModuleCode = "assets", Enabled = true });
                db.GroupPermissions.AddRange(group[7..].Split(',').Select(a => new GroupGrant { GroupId = custom.Id, ModuleCode = "assets", Action = a }));
                groupId = custom.Id;
            }
            else
            {
                groupId = await db.Groups.Where(g => g.SeedKey == group).Select(g => g.Id).SingleAsync();
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    private Task<HttpClient> SupervisorAsync() => ActorAsync("production_supervision");

    private async Task<Guid> CenterAsync(string name, bool active = true)
    {
        await using var scope = await ScopeAsync();
        var calendar = await scope.ServiceProvider.GetRequiredService<CalendarService>().DefaultEntityAsync();
        var center = new WorkCenter { Name = Unique(name), CalendarId = calendar.Id, IsActive = active, CreatedAt = DateTimeOffset.UtcNow };
        center.SetShifts([new Shift("07:00", "16:00")]);
        var db = scope.ServiceProvider.GetRequiredService<IProductionDb>();
        db.WorkCenters.Add(center);
        await db.SaveChangesAsync();
        return center.Id;
    }

    private async Task<Guid> EmployeeAsync(string name, bool terminated = false)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.HR.Application.IHrDb>();
        var position = new JobPosition { Name = "Mecânico " + Guid.NewGuid().ToString("N")[..6], MonthlyHours = 220m, CreatedAt = DateTimeOffset.UtcNow };
        db.JobPositions.Add(position);
        var employee = new Employee
        {
            Name = Unique(name), PositionId = position.Id, AdmissionDate = new DateOnly(2024, 1, 2), BirthDate = new DateOnly(1990, 1, 1), BaseSalary = 3000m, CreatedAt = DateTimeOffset.UtcNow,
            Cpf = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Status = terminated ? EmployeeStatus.Terminated : EmployeeStatus.Active, TerminationDate = terminated ? new DateOnly(2026, 1, 5) : null,
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private async Task TerminateAsync(Guid employeeId)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.HR.Application.IHrDb>();
        var employee = await db.Employees.SingleAsync(e => e.Id == employeeId);
        employee.Status = EmployeeStatus.Terminated;
        employee.TerminationDate = new DateOnly(2026, 10, 1);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SupplierAsync(string name, PartyRoleKind role = PartyRoleKind.Supplier)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IPartiesDb>();
        var party = new Party { Kind = PartyKind.PJ, LegalName = Unique(name), CreatedAt = DateTimeOffset.UtcNow };
        party.Roles.Add(new PartyRole { PartyId = party.Id, Role = role });
        db.Parties.Add(party);
        await db.SaveChangesAsync();
        return party.Id;
    }

    private static async Task<Guid> SiteAsync(HttpClient client, string? name = null) =>
        (await CreatedAsync(await client.PostAsJsonAsync(Sites, new { name = name ?? Unique("Setor") }))).GetProperty("id").GetGuid();

    private static async Task<JsonElement> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static object Input(
        string name, string type = "Machine", string? serial = null, string? brand = null, string? model = null, decimal? value = null, Guid? site = null, Guid? responsible = null,
        Guid? center = null, Guid? supplier = null, Guid? invoice = null, string? invoiceRef = null, string? date = null, uint? version = null) =>
        new
        {
            name, type, brand, model, serialNumber = serial, acquisitionDate = date, acquisitionValue = value, incomingInvoiceId = invoice, invoiceRef, supplierPartyId = supplier,
            siteId = site, responsibleEmployeeId = responsible, workCenterId = center, notes = (string?)null, version,
        };

    private static async Task<JsonElement> CreateAsync(HttpClient client, object input) => await CreatedAsync(await client.PostAsJsonAsync(Base, input));

    private static MultipartFormDataContent File(string name, byte[] bytes, string type) => new() { { new ByteArrayContent(bytes) { Headers = { ContentType = new(type) } }, "file", name } };
}
