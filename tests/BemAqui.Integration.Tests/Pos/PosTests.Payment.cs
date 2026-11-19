using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// PDV-03 CA 4: a forma de pagamento desce para o caixa com as bandeiras da tabela da adquirente (sem o "qualquer bandeira") e o teto de
/// parcelas, para o operador escolher a bandeira sem digitar nada do cartão. Taxa e prazo continuam fora da descida (são custo).
/// </summary>
public partial class PosTests
{
    [Fact]
    public async Task Forma_de_cartao_desce_com_as_bandeiras_da_tabela_e_o_teto_de_parcelas_sem_taxa()
    {
        await CommerceAsync(true);
        var (owner, _, device) = await RegisterAsync();
        using var registered = device;
        var (_, _, _, bank) = await SetupAsync();
        var suffix = Unique(string.Empty, 6);
        var acquirer = (await OkAsync(await owner.PostAsJsonAsync("/api/v1/parties",
            new { kind = "PJ", document = (string?)null, legalName = "Maquininha " + suffix, roles = new[] { "Acquirer" } }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        object Method(string name, string type, Guid? acq) => new { name, type, destinationAccountId = bank, acquirerPartyId = acq, nfePaymentCode = (string?)null };
        var parcelado = (await OkAsync(await owner.PostAsJsonAsync("/api/v1/finance/payment-methods", Method("Crédito parcelado " + suffix, "CreditInstallments", acquirer)), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var pix = (await OkAsync(await owner.PostAsJsonAsync("/api/v1/finance/payment-methods", Method("Pix " + suffix, "Pix", null)), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var validFrom = DateTime.UtcNow.Date.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var (brand, installments) in new[] { ("Mastercard", 2), ("Visa", 2), ("Visa", 7), ("Any", 2) })
        {
            var fee = await owner.PostAsJsonAsync($"/api/v1/finance/payment-methods/{parcelado}/fees", new { brand, installments, feePercent = 3.5m, settlementDays = 30, validFrom });
            fee.StatusCode.Should().Be(HttpStatusCode.OK, await fee.Content.ReadAsStringAsync());
        }

        await DrainOutboxAsync();
        var snapshot = await SnapshotAsync(device);
        var methods = snapshot.GetProperty("paymentMethods").EnumerateArray().ToList();
        var card = methods.Single(m => m.GetProperty("id").GetGuid() == parcelado);
        card.GetProperty("cardBrands").EnumerateArray().Select(b => b.GetString()).Should().Equal("Visa", "Mastercard");
        card.GetProperty("maxInstallments").GetInt32().Should().Be(12);
        var pixJson = methods.Single(m => m.GetProperty("id").GetGuid() == pix);
        pixJson.GetProperty("cardBrands").GetArrayLength().Should().Be(0);
        pixJson.GetProperty("maxInstallments").GetInt32().Should().Be(1);

        // Taxa e prazo da adquirente nunca descem para o caixa.
        PropertyNames(card).Where(n => n.Contains("fee", StringComparison.OrdinalIgnoreCase) || n.Contains("settlement", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
        card.TryGetProperty("feePercent", out JsonElement _).Should().BeFalse();
    }
}
