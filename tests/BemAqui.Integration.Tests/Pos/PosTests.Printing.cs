using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// IMP-01: modo "só cupom não fiscal" (só o Proprietário liga, com aceite auditado e versionado), impressoras do caixa que descem na sincronização,
/// impressões registradas (2ª via com quem e quando) e o relatório de vendas sem documento fiscal, com PostgreSQL real.
/// </summary>
public partial class PosTests
{
    /// <summary>Usuário fora do grupo Proprietário, num grupo próprio com as ações pedidas (ex.: sales.edit, "acesso às configurações").</summary>
    private async Task<(Guid Id, HttpClient Client)> MemberAsync(params string[] permissions)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "imp" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = Unique("Gerente ", 6), Description = "teste", CreatedAt = DateTimeOffset.UtcNow };
            db.Groups.Add(group);
            var grants = permissions.Select(p => p.Split('.')).ToList();
            foreach (var module in grants.Select(g => g[0]).Distinct())
            {
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
            }

            db.GroupPermissions.AddRange(grants.Select(g => new GroupGrant { GroupId = group.Id, ModuleCode = g[0], Action = g[1] }));
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    private static async Task<JsonElement> StoreOfAsync(HttpClient client, Guid storeId) =>
        (await OkAsync(await client.GetAsync("/api/v1/pos/stores"))).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == storeId);

    private static Task<HttpResponseMessage> NonFiscalAsync(HttpClient client, Guid storeId, bool enabled, bool accepted, int? termsVersion, uint version) =>
        client.PostAsJsonAsync($"/api/v1/pos/stores/{storeId}/non-fiscal-mode", new { enabled, accepted, termsVersion, version });

    [Fact]
    public async Task Modo_nao_fiscal_so_o_Proprietario_liga_com_aceite_auditado_versionado_e_a_loja_so_vende_com_ele()
    {
        await CommerceAsync(true);
        var (ownerId, owner) = await OwnerAsync(otp: true);
        var (owner2, store, device) = await RegisterAsync(owner);
        using var registered = device;

        // CA 10: loja nova com o modo desligado; sem o aceite, o caixa recebe "não vende".
        var fresh = await StoreOfAsync(owner2, store);
        fresh.GetProperty("fiscalMode").GetString().Should().Be("Nfce");
        fresh.GetProperty("receiptPrintMode").GetString().Should().Be("Ask");
        var before = (await SnapshotAsync(device)).GetProperty("store");
        before.GetProperty("canSell").GetBoolean().Should().BeFalse();
        before.GetProperty("companyName").GetString().Should().NotBeNullOrWhiteSpace();

        // O texto é versionado.
        var terms = await OkAsync(await owner.GetAsync("/api/v1/pos/stores/non-fiscal-terms"));
        terms.GetProperty("version").GetInt32().Should().Be(NonFiscalTerms.Version);
        terms.GetProperty("text").GetString().Should().Contain("contadora").And.Contain("não avalia");

        // CA 11: quem edita lojas mas não é do grupo Proprietário recebe 403 (ligar e desligar).
        var (_, manager) = await MemberAsync("sales.view", "sales.edit");
        var version = fresh.GetProperty("version").GetUInt32();
        var denied = await NonFiscalAsync(manager, store, true, true, NonFiscalTerms.Version, version);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, await denied.Content.ReadAsStringAsync());
        (await NonFiscalAsync(manager, store, false, false, null, version)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Sem marcar o aceite, ou com o texto de outra versão, não liga.
        (await NonFiscalAsync(owner, store, true, false, NonFiscalTerms.Version, version)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var stale = await NonFiscalAsync(owner, store, true, true, NonFiscalTerms.Version + 1, version);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(stale)).Should().Be("terms_changed");

        // CA 10: o Proprietário liga com o aceite; a loja guarda a versão, quem e quando; o caixa passa a vender.
        var on = await OkAsync(await NonFiscalAsync(owner, store, true, true, NonFiscalTerms.Version, version));
        on.GetProperty("fiscalMode").GetString().Should().Be("NonFiscal");
        on.GetProperty("nonFiscalTermsVersion").GetInt32().Should().Be(NonFiscalTerms.Version);
        on.GetProperty("nonFiscalAcceptedByName").GetString().Should().Be("Paula Lima");
        on.GetProperty("needsNewAcceptance").GetBoolean().Should().BeFalse();
        var after = (await SnapshotAsync(device)).GetProperty("store");
        after.GetProperty("canSell").GetBoolean().Should().BeTrue();
        after.GetProperty("fiscalMode").GetString().Should().Be("NonFiscal");

        // CA 13: o texto mudou de versão (simulado: o aceite guardado é de uma versão anterior); o sistema pede novo aceite e guarda o anterior.
        await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE pos.stores SET non_fiscal_terms_version = {NonFiscalTerms.Version - 1} WHERE id = {store}"));
        var old = await StoreOfAsync(owner, store);
        old.GetProperty("needsNewAcceptance").GetBoolean().Should().BeTrue();
        var stopped = (await SnapshotAsync(device)).GetProperty("store");
        stopped.GetProperty("canSell").GetBoolean().Should().BeFalse("o aceite de uma versão anterior do texto não libera a venda");
        stopped.GetProperty("fiscalMode").GetString().Should().Be("NonFiscal", "o caixa diz que falta o aceite do texto novo");
        var again = await OkAsync(await NonFiscalAsync(owner, store, true, true, NonFiscalTerms.Version, old.GetProperty("version").GetUInt32()));
        again.GetProperty("needsNewAcceptance").GetBoolean().Should().BeFalse();
        (await SnapshotAsync(device)).GetProperty("store").GetProperty("canSell").GetBoolean().Should().BeTrue();

        // Auditoria: cada aceite (e o desligar) fica no histórico da loja com quem, quando e a versão do texto.
        var off = await OkAsync(await NonFiscalAsync(owner, store, false, false, null, again.GetProperty("version").GetUInt32()));
        off.GetProperty("fiscalMode").GetString().Should().Be("Nfce");
        var audit = await WithDbAsync(db => db.AuditEntries.AsNoTracking()
            .Where(a => a.Entity == nameof(Store) && a.EntityId == store.ToString() && a.UserId == ownerId).OrderBy(a => a.At).ToListAsync());
        var fiscal = audit.Where(a => a.Changes.Contains("FiscalMode", StringComparison.Ordinal) || a.Changes.Contains("NonFiscalTermsVersion", StringComparison.Ordinal)).ToList();
        fiscal.Should().HaveCount(3, "ligar, aceitar a versão nova e desligar ficam registrados");
        fiscal[0].Changes.Should().Contain("NonFiscal").And.Contain("NonFiscalAcceptedBy");
        (await SnapshotAsync(device)).GetProperty("store").GetProperty("canSell").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Impressora_do_caixa_desce_na_sincronizacao_e_a_reimpressao_fica_registrada()
    {
        await CommerceAsync(true);
        var (owner, store, device) = await RegisterAsync();
        using var registered = device;
        var registerId = (await StoreOfAsync(owner, store)).GetProperty("registers")[0].GetProperty("id").GetGuid();

        // CA 3 e 6: 58 mm só com 32 colunas; conexão direta (USB, rede, Bluetooth) é da IMP-02 e da IMP-03.
        (await owner.PostAsJsonAsync("/api/v1/pos/printers", new { registerId, name = "Errada", connection = "Browser", paperWidthMm = 58, columns = 48, model = "generico-58" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.PostAsJsonAsync("/api/v1/pos/printers", new { registerId, name = "USB", connection = "Usb", paperWidthMm = 80, columns = 48, model = "generico-80" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var first = await OkAsync(await owner.PostAsJsonAsync("/api/v1/pos/printers",
            new { registerId, name = "Bobina 80", connection = "Browser", paperWidthMm = 80, columns = 48, model = "generico-80", isDefault = false, testConfirmed = true }), HttpStatusCode.Created);
        first.GetProperty("isDefault").GetBoolean().Should().BeTrue("a primeira impressora do caixa já nasce padrão");
        first.GetProperty("testConfirmedAt").ValueKind.Should().Be(JsonValueKind.String);
        var second = await OkAsync(await owner.PostAsJsonAsync("/api/v1/pos/printers",
            new { registerId, name = "Bobina 58", connection = "Browser", paperWidthMm = 58, columns = 32, model = "generico-58", isDefault = true }), HttpStatusCode.Created);
        second.GetProperty("isDefault").GetBoolean().Should().BeTrue();

        // CA 7: outro aparelho do mesmo caixa recebe a configuração na sincronização (a foto traz a lista do caixa, com colunas e a padrão).
        var printers = (await SnapshotAsync(device)).GetProperty("printers");
        printers.GetProperty("version").GetInt32().Should().BePositive();
        var items = printers.GetProperty("items").EnumerateArray().ToList();
        items.Select(p => p.GetProperty("name").GetString()).Should().Equal("Bobina 58", "Bobina 80");
        items[0].GetProperty("columns").GetInt32().Should().Be(32);
        items[0].GetProperty("isDefault").GetBoolean().Should().BeTrue();
        items[1].GetProperty("isDefault").GetBoolean().Should().BeFalse("uma padrão por caixa");

        // Desativar tira do caixa; a versão da coleção muda (o aparelho troca a lista inteira).
        var changed = await OkAsync(await owner.PutAsJsonAsync($"/api/v1/pos/printers/{first.GetProperty("id").GetGuid()}",
            new { registerId, name = "Bobina 80", connection = "Browser", paperWidthMm = 80, columns = 42, model = "generico-80", isDefault = false, isActive = false, version = first.GetProperty("version").GetUInt32() }));
        changed.GetProperty("isActive").GetBoolean().Should().BeFalse();
        var cursor = (await SnapshotAsync(device)).GetProperty("cursor").GetInt64();
        var delta = await OkAsync(await Anonymous().SendAsync(SignedGet(device, $"/api/v1/pos/sync/changes?cursor={cursor}")));
        delta.GetProperty("printers").GetProperty("items").EnumerateArray().Select(p => p.GetProperty("name").GetString()).Should().Equal("Bobina 58");
        delta.GetProperty("printers").GetProperty("version").GetInt32().Should().NotBe(printers.GetProperty("version").GetInt32());

        // CA 4: a impressão e a 2ª via chegam do aparelho (a subida é da OFF-02) e ficam gravadas com quem e quando, sem duplicar no reenvio.
        var saleId = Guid.CreateVersion7();
        var operatorId = Guid.CreateVersion7();
        await using var scope = await ScopeAsync();
        var service = scope.ServiceProvider.GetRequiredService<PrinterService>();
        var at = DateTimeOffset.UtcNow.AddMinutes(-5);
        // O caixa e o operador vêm do principal do aparelho e da sessão de PIN, não do corpo.
        var original = new PrintRecordInput(Guid.CreateVersion7(), second.GetProperty("id").GetGuid(), PrintRecordKind.Receipt, saleId, "LOJA-01.1-000001", 1, at);
        var reprint = original with { Id = Guid.CreateVersion7(), Kind = PrintRecordKind.Reprint, Copy = 2, PrintedAt = at.AddMinutes(2) };
        (await service.RecordPrintAsync(registerId, operatorId, original)).IsSuccess.Should().BeTrue();
        (await service.RecordPrintAsync(registerId, operatorId, reprint)).Value.Replayed.Should().BeFalse();
        (await service.RecordPrintAsync(registerId, operatorId, reprint)).Value.Replayed.Should().BeTrue();
        (await service.RecordPrintAsync(registerId, operatorId, reprint with { Id = Guid.CreateVersion7(), Copy = 1 })).IsFailure.Should().BeTrue("a 2ª via é a reimpressão");
        var jobs = await service.JobsOfSaleAsync(saleId);
        jobs.Select(j => (j.Kind, j.Copy, j.OperatorId)).Should().Equal((PrintRecordKind.Receipt, 1, operatorId), (PrintRecordKind.Reprint, 2, operatorId));

        // A venda que já está no servidor tem de ser deste caixa: o cupom de uma venda de outro caixa é recusado.
        var otherSale = Guid.CreateVersion7();
        var otherNumber = Unique("O", 8) + "-01.1-000001";
        await WithDbAsync(async db =>
        {
            db.Sales.Add(Sale.Create(otherSale, SaleChannel.Pos, otherNumber, "pos", null, store, Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, operatorId, true, 1,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, operatorId));
            return await db.SaveChangesAsync();
        });
        var foreign = await service.RecordPrintAsync(registerId, operatorId, original with { Id = Guid.CreateVersion7(), SaleId = otherSale, SaleNumber = otherNumber });
        foreign.Error!.Code.Should().Be("sale_other_register");

        // Caixa desativado: a impressora dele não muda mais.
        await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE pos.registers SET is_active = false WHERE id = {registerId}"));
        var inactive = await owner.PutAsJsonAsync($"/api/v1/pos/printers/{second.GetProperty("id").GetGuid()}",
            new { registerId, name = "Bobina 58", connection = "Browser", paperWidthMm = 58, columns = 32, model = "generico-58", isDefault = true, isActive = true, version = second.GetProperty("version").GetUInt32() });
        inactive.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await inactive.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Relatorio_de_vendas_sem_documento_fiscal_por_periodo_loja_e_operador_com_exportacao()
    {
        await CommerceAsync(true);
        var (sellerId, owner) = await OwnerAsync(name: "Rita Caixa");
        var (store, register, _) = await StoreAsync(owner);
        var day = new DateOnly(2026, 3, 10);

        // Duas vendas sem documento, uma com nota (fica fora), uma cancelada (fora) e uma fora do período.
        await WithDbAsync(async db =>
        {
            Sale NewSale(decimal total, DateOnly date, Action<Sale>? change = null)
            {
                var id = Guid.CreateVersion7();
                var sale = Sale.Create(id, SaleChannel.Pos, Unique("T", 8) + "-" + id.ToString("N")[..6], "pos", null, store, register, Guid.CreateVersion7(), null, null, sellerId, true, 1,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, sellerId);
                sale.ApplyTotals([SaleLine.Create(Guid.CreateVersion7(), id, 1, Guid.CreateVersion7(), null, null, "Item", "UN", 1, total, 0, null, null)], 0);
                sale.Complete(DateTimeOffset.UtcNow, date);
                change?.Invoke(sale);
                db.Sales.Add(sale);
                return sale;
            }

            NewSale(10.50m, day);
            NewSale(4.25m, day.AddDays(1));
            NewSale(99m, day, s => s.InvoiceId = Guid.CreateVersion7());
            NewSale(77m, day, s => s.Cancel("teste", null, DateTimeOffset.UtcNow));
            NewSale(55m, day.AddDays(40));
            return await db.SaveChangesAsync();
        });

        var report = await OkAsync(await owner.GetAsync($"/api/v1/sales/reports/non-fiscal?from=2026-03-01&to=2026-03-31&storeId={store}"));
        report.GetProperty("count").GetInt32().Should().Be(2);
        report.GetProperty("total").GetDecimal().Should().Be(14.75m);
        var row = report.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("operatorName").GetString().Should().Be("Rita Caixa");
        row.GetProperty("storeName").GetString().Should().StartWith("Loja ");

        (await OkAsync(await owner.GetAsync($"/api/v1/sales/reports/non-fiscal?from=2026-03-01&to=2026-03-31&storeId={store}&operatorId={Guid.CreateVersion7()}")))
            .GetProperty("count").GetInt32().Should().Be(0);
        (await owner.GetAsync($"/api/v1/sales/reports/non-fiscal?from=2026-03-31&to=2026-03-01")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var export = await owner.GetAsync($"/api/v1/sales/reports/non-fiscal/export?from=2026-03-01&to=2026-03-31&storeId={store}");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var csv = await export.Content.ReadAsStringAsync();
        csv.Should().Contain("Loja;Operador;Vendas;Valor (R$)").And.Contain("Rita Caixa;2;14,75").And.Contain("Total;;2;14,75");
    }
}
