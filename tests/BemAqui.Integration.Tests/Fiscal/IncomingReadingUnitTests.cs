using BemAqui.Fiscal.Application.Incoming;
using BemAqui.Fiscal.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-05 (unidade): chave de 44 dígitos com DV e as partes (CA 4); emitente pessoa física; a resposta do worker conferida como dado de
/// fora (chave com DV errado descartada, CNPJ do texto contra a chave, chave desconhecida, tamanho); a conferência campo a campo, o mês da
/// chave, o destinatário e a soma dos itens (CA 7 a 9); o que foi corrigido (CA 13).
/// </summary>
public class IncomingReadingUnitTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    /// <summary>Chave de outubro de 2026 em SP: UF, AAMM, CNPJ, modelo, série, número, forma de emissão, código e o DV.</summary>
    private static string Key(string issuer, string model = "55", int series = 1, long number = 1234) =>
        Redigit(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"352610{issuer}{model}{series:000}{number:000000000}112345678"));

    /// <summary>Recalcula o DV de 43 dígitos.</summary>
    private static string Redigit(string body)
    {
        var sum = 0;
        var weight = 2;
        for (var i = body.Length - 1; i >= 0; i--)
        {
            sum += (body[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var rest = sum % 11;
        return body + (rest < 2 ? 0 : 11 - rest).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static CheckReadingRequest Request(string key, params ReadingItemInput[] items)
    {
        var list = items.Length == 0 ? [new ReadingItemInput("CH-2MM", "CHAPA DE ACO 2MM", "72085100", "UN", 10m, 12.5m, 125m)] : items.ToList();
        var products = list.Sum(i => i.Amount ?? 0m);
        return new CheckReadingRequest(key, new DateOnly(2026, 10, 15), "FORNECEDOR TESTE LTDA", NfeSamples.TenantCnpj,
            new ReadingTotalsInput(products, 0m, 0m, 0m, 0m, 0m, products, 0m, 0m, 0m, products), list, IncomingReadingRules.RequiredKeys(list.Count));
    }

    [Fact]
    public void Chave_com_DV_e_as_partes_e_emitente_pessoa_fisica()
    {
        var cnpj = NfeSamples.NewCnpj();
        var key = Key(cnpj, number: 98765, series: 7);
        var parts = IncomingReadingRules.Parse(key)!;
        parts.Should().BeEquivalentTo(new { StateCode = "35", Year = 2026, Month = 10, IssuerDocument = cnpj, Model = "55", Series = 7, Number = 98765L });
        IncomingReadingRules.Parse(key[..43] + ((key[43] - '0' + 1) % 10)).Should().BeNull("dígito verificador errado");
        IncomingReadingRules.Parse(key[..40]).Should().BeNull();
        IncomingReadingRules.IsPersonIssuer(parts).Should().BeFalse();
        var person = IncomingReadingRules.Parse(Key("000" + NfeSamples.CarrierCpf))!;
        IncomingReadingRules.IsPersonIssuer(person).Should().BeTrue("CPF com três zeros na frente");
    }

    [Fact]
    public void Resposta_do_worker_e_conferida_como_dado_de_fora()
    {
        var cnpj = NfeSamples.NewCnpj();
        var key = Key(cnpj);
        var wrongDigit = key[..43] + ((key[43] - '0' + 1) % 10);
        ReadingField F(string k, string? v, ReadingFieldState s = ReadingFieldState.Read) => new(k, v, s);
        var reply = new PdfReaderReply(PdfReadingOutcome.Read, 1, 120,
        [
            F("accessKey", wrongDigit), F("issuerDocument", NfeSamples.NewCnpj()), F("recipientDocument", NfeSamples.NewCnpj()), F("issuedAt", "2026-10-15"),
            F("totals.products", "125.00"), F("totals.total", "nao-e-numero"), F("issuerName", new string('A', 500)), F("comando", "rm -rf /"),
            F("items.0.code", "CH-2MM"), F("items.0.quantity", "10.0000"), F("items.0.ncm", "7208.51.00"), F("items.0.description", "CHAPA\u0007 DE  ACO"),
            F("items.5.code", "pulou"),
        ]);
        var (fields, read, warnings) = IncomingReadingSanitizer.Sanitize(reply, NfeSamples.TenantCnpj);
        var byKey = fields.ToDictionary(f => f.Key);

        read.Should().BeNull();
        byKey["accessKey"].Should().Be(F("accessKey", null, ReadingFieldState.NotFound));
        warnings.Should().ContainSingle().Which.Should().Be(IncomingReadingRules.KeyDiscardedWarning);
        byKey["recipientDocument"].State.Should().Be(ReadingFieldState.Uncertain, "o destinatário não é o tenant");
        byKey["totals.products"].Value.Should().Be("125");
        byKey["totals.total"].Should().Be(F("totals.total", null, ReadingFieldState.NotFound));
        byKey["totals.freight"].State.Should().Be(ReadingFieldState.NotFound, "campo que não veio aparece como não encontrado");
        byKey["issuerName"].Value.Should().HaveLength(200);
        byKey["items.0.quantity"].Value.Should().Be("10");
        byKey["items.0.ncm"].Value.Should().Be("72085100");
        byKey["items.0.description"].Value.Should().Be("CHAPA DE ACO");
        byKey["items.0.unit"].State.Should().Be(ReadingFieldState.NotFound);
        byKey.Keys.Should().NotContain(["comando", "issuerDocument", "items.5.code"], "só as chaves do formulário, os itens em sequência");

        var good = IncomingReadingSanitizer.Sanitize(reply with { Fields = [F("accessKey", key), F("issuerDocument", NfeSamples.NewCnpj())] }, NfeSamples.TenantCnpj);
        good.Key.Should().Be(key);
        good.Warnings.Should().ContainSingle().Which.Should().Be(IncomingReadingRules.IssuerMismatchWarning, "o CNPJ do texto que contradiz a chave é ignorado");
        IncomingReadingSanitizer.Sanitize(PdfReaderReply.Of(PdfReadingOutcome.NoText), null).Fields.Should().BeEmpty();
    }

    [Fact]
    public void Conferencia_campo_a_campo_chave_mes_destinatario_e_soma_dos_itens()
    {
        var cnpj = NfeSamples.NewCnpj();
        var key = Key(cnpj);
        var request = Request(key);
        var ok = IncomingReadingForm.Build(request, NfeSamples.TenantCnpj, Today);
        ok.IsSuccess.Should().BeTrue(ok.Error?.Message);
        ok.Value.Key.IssuerDocument.Should().Be(cnpj);
        ok.Value.Totals.Total.Should().Be(125m);

        string Message(CheckReadingRequest r) => IncomingReadingForm.Build(r, NfeSamples.TenantCnpj, Today).Error!.Message;
        Message(request with { Confirmed = request.Confirmed!.Skip(1).ToList() }).Should().Be("Falta conferir 1 campo. Marque cada campo como conferido ou corrija o valor.");
        Message(request with { Confirmed = [] }).Should().StartWith("Faltam conferir 22 campos");
        Message(request with { AccessKey = key[..43] + ((key[43] - '0' + 1) % 10) }).Should().Contain("dígito verificador");
        Message(request with { AccessKey = Key(cnpj, model: "65") }).Should().Be("A chave não é de NF-e (modelo 55).");
        Message(request with { IssuedOn = new DateOnly(2026, 9, 30) }).Should().Be("A data de emissão não bate com o mês da chave (10/2026).");
        Message(request with { IssuedOn = Today.AddDays(1) }).Should().Contain("futuro");
        Message(request with { RecipientDocument = NfeSamples.NewCnpj() }).Should().Be(IncomingInvoiceRules.NotRecipientMessage);
        Message(request with { IssuerName = " " }).Should().Contain("razão social");
        Message(Request(key, new ReadingItemInput("A", "ITEM", "123", "UN", 1m, 1m, 1m))).Should().Be("Item 1: o NCM tem 8 dígitos.");
        Message(Request(key, new ReadingItemInput("A", "ITEM", null, "UN", 0m, 1m, 1m))).Should().Contain("quantidade maior que zero");
        Message(request with { Items = [] }).Should().Be("Informe ao menos um item da nota.");

        var sum = request with { Totals = request.Totals! with { Products = 100m } };
        var refused = IncomingReadingForm.Build(sum, NfeSamples.TenantCnpj, Today).Error!;
        refused.Code.Should().Be("incoming_reading_sum");
        refused.Message.Should().Be("A soma dos itens (R$ 125,00) não bate com o total da nota (R$ 100,00).");
    }

    [Fact]
    public void Corrigido_e_o_que_mudou_do_lido_e_o_digitado()
    {
        var key = Key(NfeSamples.NewCnpj());
        var form = IncomingReadingForm.Build(Request(key), NfeSamples.TenantCnpj, Today).Value;
        ReadingField[] read =
        [
            new("accessKey", key, ReadingFieldState.Read), new("issuedAt", "2026-10-15", ReadingFieldState.Read), new("issuerName", "FORNECEDOR  TESTE LTDA", ReadingFieldState.Uncertain),
            new("recipientDocument", NfeSamples.TenantCnpj, ReadingFieldState.Read), new("totals.products", "125.00", ReadingFieldState.Read), new("totals.total", "120", ReadingFieldState.Uncertain),
            new("items.0.quantity", "10.0000", ReadingFieldState.Read),
        ];
        var corrected = IncomingReadingForm.Corrected(read, form.Values);
        corrected.Should().Contain("totals.total").And.Contain("totals.freight", "não lido = digitado").And.Contain("items.0.code");
        corrected.Should().NotContain(["accessKey", "issuedAt", "issuerName", "recipientDocument", "totals.products", "items.0.quantity"]);
    }
}
