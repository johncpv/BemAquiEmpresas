using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class PaymentOrderTests
{
    [Fact]
    public async Task Solicitante_nao_aprova_a_propria_OP_salvo_as_excecoes_da_D6()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ActorAsync(Group.OwnerSeedKey); // Proprietário com mais de um membro
        var supplier = await SupplierAsync(owner);
        var payable = (await PayableAsync(owner, supplier)).GetProperty("id").GetGuid();
        var order = await JsonAsync(await owner.SendAsync(Post(Base, Items((payable, 950m)))));
        var id = order.GetProperty("summary").GetProperty("id").GetGuid();
        order.GetProperty("canDecide").GetBoolean().Should().BeFalse();
        var denied = await owner.PostAsJsonAsync($"{Base}/{id}/approve", new { comment = (string?)null, version = Version(order) });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(denied)).Should().Be("own_payment_order");

        // CA 10: ligado o parâmetro, o Proprietário aprova a própria e a OP fica marcada.
        var limits = await JsonAsync(await owner.GetAsync("/api/v1/finance/parameters"));
        var update = await owner.PutAsJsonAsync("/api/v1/finance/parameters", new
        {
            discountApprovalPct = limits.GetProperty("discountApprovalPct").GetDecimal(), discountApprovalAmount = limits.GetProperty("discountApprovalAmount").GetDecimal(),
            cancelApprovalAmount = limits.GetProperty("cancelApprovalAmount").GetDecimal(), backdateDays = limits.GetProperty("backdateDays").GetInt32(), ownerCanApproveOwn = true, paymentTolerance = 0m,
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.Title.EndsWith("ligou a aprovação da própria ordem pelo Proprietário.")))).Should().BeGreaterThan(1, "todos os membros do Proprietário");
        var self = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/approve", new { comment = (string?)null, version = Version(order) }));
        self.GetProperty("summary").GetProperty("selfApproved").GetBoolean().Should().BeTrue();
        (await JsonAsync(await owner.GetAsync($"{Base}?selfApproved=true"))).GetProperty("items").EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task Reprovar_exige_motivo_e_decisoes_ao_mesmo_tempo_so_uma_vale()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, finance) = await ActorAsync("finance");
        var supplier = await SupplierAsync(owner);
        var noMethod = (await PayableAsync(finance, supplier, withMethod: false)).GetProperty("id").GetGuid();
        (await (await finance.SendAsync(Post(Base, Items((noMethod, 100m))))).Content.ReadAsStringAsync()).Should().Contain("Informe a forma de pagamento");

        var payable = (await PayableAsync(finance, supplier)).GetProperty("id").GetGuid();
        var order = await JsonAsync(await finance.SendAsync(Post(Base, Items((payable, 950m)))));
        var id = order.GetProperty("summary").GetProperty("id").GetGuid();
        (await owner.PostAsJsonAsync($"{Base}/{id}/reject", new { comment = " ", version = Version(order) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var results = await Task.WhenAll(
            owner.PostAsJsonAsync($"{Base}/{id}/approve", new { comment = (string?)null, version = Version(order) }),
            owner.PostAsJsonAsync($"{Base}/{id}/reject", new { comment = "Valor errado", version = Version(order) }));
        results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        var loser = results.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        (await loser.Content.ReadAsStringAsync()).Should().Contain("A ordem já foi decidida.");
        var status = await WithDbAsync(db => db.Payables.Where(p => p.Id == payable).Select(p => p.Status).SingleAsync());
        status.Should().BeOneOf(PayableStatus.Approved, PayableStatus.Open);
    }

    [Fact]
    public async Task Comprovante_so_em_OP_paga_e_o_grupo_Financeiro_ganha_pagar()
    {
        (await WithDbAsync(db => db.GroupPermissions.AnyAsync(p => p.ModuleCode == "finance" && p.Action == "pay" && db.Groups.Any(g => g.Id == p.GroupId && g.SeedKey == "finance")))).Should().BeTrue("seed patch finance-pay-financeiro (CA 1)");
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, finance) = await ActorAsync("finance");
        var supplier = await SupplierAsync(owner);
        var payable = (await PayableAsync(finance, supplier)).GetProperty("id").GetGuid();
        var order = await JsonAsync(await finance.SendAsync(Post(Base, Items((payable, 950m)))));
        var id = order.GetProperty("summary").GetProperty("id").GetGuid();
        var pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();
        MultipartFormDataContent File() => new() { { new ByteArrayContent(pdf) { Headers = { ContentType = new("application/pdf") } }, "file", "comprovante.pdf" } };
        var early = await finance.PostAsync($"/api/v1/attachments/PaymentOrder/{id}", File());
        early.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await early.Content.ReadAsStringAsync()).Should().Contain("Anexe o comprovante depois do pagamento.");

        var approved = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/approve", new { comment = (string?)null, version = Version(order) }));
        var bank = await BankAsync(owner);
        var paid = await finance.SendAsync(Post($"{Base}/{id}/pay", new { bankAccountId = bank, date = Iso(Today.AddDays(-10)), items = Array.Empty<object>(), backdateReason = (string?)null, version = Version(approved) }));
        (await paid.Content.ReadAsStringAsync()).Should().Contain("backdateReason", "data retroativa pede motivo (CA 18)");
        paid = await finance.SendAsync(Post($"{Base}/{id}/pay", new { bankAccountId = bank, date = Iso(Today), items = Array.Empty<object>(), backdateReason = (string?)null, version = Version(approved) }));
        paid.StatusCode.Should().Be(HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());
        (await WithDbAsync(db => db.Payables.Where(p => p.Id == payable).Select(p => p.Status).SingleAsync())).Should().Be(PayableStatus.Paid);
        (await JsonAsync(paid)).GetProperty("retentions").EnumerateArray().Single().GetProperty("amount").GetDecimal().Should().Be(50m, "sugere o recolhimento do ISS (CA 19)");
        var attached = await finance.PostAsync($"/api/v1/attachments/PaymentOrder/{id}", File());
        attached.StatusCode.Should().Be(HttpStatusCode.Created, await attached.Content.ReadAsStringAsync());
        var (_, viewer) = await ActorAsync(null);
        (await viewer.PostAsync($"/api/v1/attachments/PaymentOrder/{id}", File())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
