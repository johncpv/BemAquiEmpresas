using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>PAG-01: formas de pagamento, adquirente como terceiro, taxas por vigência e o contrato <see cref="IPaymentMethods"/>.</summary>
public partial class FinanceTests
{
    private const string Methods = "/api/v1/finance/payment-methods";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static object Method(string name, string type, Guid account, Guid? acquirer = null) =>
        new { name, type, destinationAccountId = account, acquirerPartyId = acquirer, nfePaymentCode = (string?)null };

    private static object Fee(string brand, int installments, decimal feePercent, int settlementDays, DateOnly validFrom) =>
        new { brand, installments, feePercent, settlementDays, validFrom = validFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) };

    /// <summary>Conta bancária, caixa e adquirente (terceiro com o papel Adquirente) para os cenários.</summary>
    private static async Task<(Guid Bank, Guid Cash, Guid Acquirer)> PaymentSetupAsync(HttpClient owner)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var bank = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Banco " + suffix)));
        var cash = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new { name = "Caixa " + suffix, kind = "Cash" }));
        var acquirer = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Maquininha " + suffix, roles = new[] { "Acquirer" } }));
        return (bank, cash, acquirer);
    }

    [Fact]
    public async Task Forma_de_pagamento_do_cadastro_ao_contrato_com_sugeridas_a_confirmar_e_desativar_em_vez_de_excluir()
    {
        var (_, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var (bank, cash, acquirer) = await PaymentSetupAsync(owner);

        // CA 7: Dinheiro só cai em conta Caixa; CA 1: cartão exige a adquirente.
        (await owner.PostAsJsonAsync(Methods, Method("Dinheiro na conta", "Cash", bank))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.PostAsJsonAsync(Methods, Method("Débito sem adquirente", "Debit", bank))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var money = await CreatedAsync(await owner.PostAsJsonAsync(Methods, Method("Dinheiro da loja", "Cash", cash)));
        var credit = await CreatedAsync(await owner.PostAsJsonAsync(Methods, Method("Crédito parcelado", "CreditInstallments", bank, acquirer)));
        var detail = await JsonAsync(await owner.GetAsync($"{Methods}/{credit}"));
        detail.GetProperty("nfePaymentCode").GetString().Should().Be("03");
        detail.GetProperty("confirmed").GetBoolean().Should().BeTrue("quem digita a forma já conferiu");
        (await owner.PostAsJsonAsync($"{Methods}/{credit}/fees", Fee("Visa", 2, 3.49m, 30, Today))).StatusCode.Should().Be(HttpStatusCode.OK);

        // CA 5: o contrato devolve as ativas com troco só no Dinheiro, a adquirente e a taxa vigente.
        await using (var scope = await ScopeAsync())
        {
            var contract = scope.ServiceProvider.GetRequiredService<IPaymentMethods>();
            var active = await contract.ActiveAsync(Today);
            active.Single(m => m.Id == money).AllowsChange.Should().BeTrue();
            var card = active.Single(m => m.Id == credit);
            card.AllowsChange.Should().BeFalse();
            card.AcquirerPartyId.Should().Be(acquirer);
            card.Fees.Should().ContainSingle(f => f.Brand == CardBrand.Visa && f.FeePercent == 3.49m && f.SettlementDays == 30);
            (await contract.FeeAsync(credit, CardBrand.Visa, 6, Today))!.FeePercent.Should().Be(3.49m, "a faixa \"a partir de 2x\" vale para 6x");
            (await contract.FeeAsync(credit, CardBrand.Elo, 2, Today)).Should().BeNull("Elo sem taxa própria e sem taxa de qualquer bandeira");
        }

        // CA 4: as sugeridas nascem "A confirmar"; cartão sem adquirente não é conferido; o Pix é, com quem e quando.
        (await owner.PostAsync($"{Methods}/suggested", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var seeded = await QueryAsync(db => db.PaymentMethods.Where(m => m.SeedKey != null).ToDictionaryAsync(m => m.SeedKey!, m => (m.Id, m.ConfirmedAt)));
        seeded.Keys.Should().BeEquivalentTo(["dinheiro", "pix", "debito", "credito", "credito-parcelado"]);
        seeded.Values.Should().OnlyContain(v => v.ConfirmedAt == null);
        (await owner.PostAsync($"{Methods}/{seeded["debito"].Id}/confirm", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var pix = seeded["pix"].Id;
        (await JsonAsync(await owner.PostAsync($"{Methods}/{pix}/confirm", null))).GetProperty("confirmed").GetBoolean().Should().BeTrue();
        (await QueryAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == "PaymentMethod" && a.EntityId == pix.ToString()).Select(a => a.Changes).ToListAsync()))
            .Should().Contain(c => c.Contains("confirmedAt", StringComparison.Ordinal), "a conferência é auditada");
        (await owner.PostAsync($"{Methods}/suggested", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await QueryAsync(db => db.PaymentMethods.CountAsync(m => m.SeedKey == "pix"))).Should().Be(1, "idempotente");

        // CA 6: usada em venda, só desativa.
        SoldWith[money] = true;
        var refused = await owner.DeleteAsync($"{Methods}/{money}");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(refused)).Should().Be("payment_method_in_use");
        (await JsonAsync(await owner.PostAsync($"{Methods}/{money}/inactivate", null))).GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Taxa_nova_encerra_a_anterior_sem_editar_o_passado_e_o_historico_mostra_quem_e_quando()
    {
        var (ownerId, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var (bank, _, acquirer) = await PaymentSetupAsync(owner);
        var debit = await CreatedAsync(await owner.PostAsJsonAsync(Methods, Method("Débito " + Guid.NewGuid().ToString("N")[..4], "Debit", bank, acquirer)));

        (await owner.PostAsJsonAsync($"{Methods}/{debit}/fees", Fee("Any", 1, 1.99m, 1, Today.AddDays(-30)))).StatusCode.Should().Be(HttpStatusCode.OK, "a primeira taxa pode começar em qualquer data");
        (await owner.PostAsJsonAsync($"{Methods}/{debit}/fees", Fee("Any", 2, 1.99m, 1, Today))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "débito é à vista");
        var past = await owner.PostAsJsonAsync($"{Methods}/{debit}/fees", Fee("Any", 1, 1.50m, 1, Today.AddDays(-1)));
        past.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "taxa passada não se altera (CA 3)");
        (await past.Content.ReadAsStringAsync()).Should().Contain("taxa passada não se altera");
        (await owner.PostAsJsonAsync($"{Methods}/{debit}/fees", Fee("Any", 1, 1.79m, 2, Today.AddDays(5)))).StatusCode.Should().Be(HttpStatusCode.OK);

        var history = (await JsonAsync(await owner.GetAsync($"{Methods}/{debit}/fees"))).EnumerateArray().ToList();
        history.Should().HaveCount(2);
        var old = history.Single(f => f.GetProperty("feePercent").GetDecimal() == 1.99m);
        old.GetProperty("validTo").GetString().Should().Be(Today.AddDays(5).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        old.GetProperty("inForce").GetBoolean().Should().BeTrue("vale até a véspera da nova");
        history.Should().OnlyContain(f => f.GetProperty("createdBy").GetGuid() == ownerId && f.GetProperty("createdAt").ValueKind == JsonValueKind.String);

        await using var scope = await ScopeAsync();
        var contract = scope.ServiceProvider.GetRequiredService<IPaymentMethods>();
        (await contract.FeeAsync(debit, CardBrand.Mastercard, 1, Today))!.FeePercent.Should().Be(1.99m, "sem taxa da bandeira, vale a de qualquer bandeira");
        (await contract.FeeAsync(debit, CardBrand.Mastercard, 1, Today.AddDays(5)))!.SettlementDays.Should().Be(2);
    }

    [Fact]
    public async Task Quem_so_ve_as_formas_nao_cadastra_taxa_nem_confere()
    {
        var (_, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var (_, viewer) = await ActorAsync("view");
        var (bank, _, _) = await PaymentSetupAsync(owner);
        var pix = await CreatedAsync(await owner.PostAsJsonAsync(Methods, Method("Pix " + Guid.NewGuid().ToString("N")[..4], "Pix", bank)));

        (await viewer.GetAsync($"{Methods}/{pix}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync($"{Methods}/{pix}/fees", Fee("Any", 1, 0.99m, 0, Today))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsync($"{Methods}/{pix}/confirm", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
