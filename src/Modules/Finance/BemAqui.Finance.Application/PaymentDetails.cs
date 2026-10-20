using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application;

/// <summary>Forma de pagamento do título (FIN-02 CA 6, D9). Os campos que valem dependem de <see cref="Kind"/>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PaymentDetailsInput(
    PaymentMethodKind Kind,
    PixKeyType? PixKeyType = null,
    string? PixKey = null,
    string? HolderName = null,
    string? HolderDocument = null,
    string? Barcode = null,
    string? BankCode = null,
    string? Agency = null,
    string? Account = null,
    string? AccountType = null,
    string? Description = null);

/// <summary>Forma gravada (mascarada) e os avisos que não bloqueiam (titular diferente do fornecedor, boleto diferente do título).</summary>
public sealed record PaymentDetailsView(PaymentDetailsInput Details, string? HolderWarning, string? BoletoWarning);

/// <summary>Validação e leitura da forma de pagamento (CA 6 e 7).</summary>
public static partial class PaymentDetails
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static string Serialize(PaymentDetailsInput input) => JsonSerializer.Serialize(input, Json);

    public static PaymentDetailsInput? Deserialize(string? json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PaymentDetailsInput>(json, Json);

    private static Error Bad(string field, string message) => ReceivableService.Invalid("paymentDetails." + field, message);

    /// <summary>Normaliza (só dígitos onde cabe) e valida.</summary>
    public static (PaymentDetailsInput? Value, Error? Error) Validate(PaymentDetailsInput input, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        var holderDoc = BrazilianDocuments.Normalize(input.HolderDocument);
        return input.Kind switch
        {
            PaymentMethodKind.Pix => Pix(input, holderDoc),
            PaymentMethodKind.Boleto => BoletoLine.Parse(input.Barcode, today) is null
                ? (null, Bad("barcode", "Linha digitável inválida. Confira os 47 ou 48 números."))
                : (new PaymentDetailsInput(PaymentMethodKind.Boleto, Barcode: BoletoLine.Digits(input.Barcode)), null),
            PaymentMethodKind.Ted => Ted(input, holderDoc),
            _ => string.IsNullOrWhiteSpace(input.Description) || input.Description.Trim().Length > 200
                ? (null, Bad("description", "Descreva a forma de pagamento (ex.: débito em conta)."))
                : (new PaymentDetailsInput(PaymentMethodKind.Other, Description: input.Description.Trim()), null),
        };
    }

    private static (PaymentDetailsInput?, Error?) Pix(PaymentDetailsInput input, string holderDoc)
    {
        if (input.PixKeyType is not { } type || string.IsNullOrWhiteSpace(input.PixKey))
        {
            return (null, Bad("pixKey", "Informe o tipo e a chave Pix."));
        }

        var key = NormalizePixKey(type, input.PixKey);
        if (key is null)
        {
            return (null, Bad("pixKey", "Chave Pix inválida para o tipo escolhido."));
        }

        return Holder(input.HolderName, holderDoc) is { } error ? (null, error) : (new PaymentDetailsInput(PaymentMethodKind.Pix, type, key, input.HolderName!.Trim(), holderDoc), null);
    }

    private static (PaymentDetailsInput?, Error?) Ted(PaymentDetailsInput input, string holderDoc)
    {
        var bank = new string((input.BankCode ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (!Banks.All.Any(b => b.Code == bank))
        {
            return (null, Bad("bankCode", "Escolha o banco da lista."));
        }

        if (string.IsNullOrWhiteSpace(input.Agency) || string.IsNullOrWhiteSpace(input.Account))
        {
            return (null, Bad(string.IsNullOrWhiteSpace(input.Agency) ? "agency" : "account", "Informe a agência e a conta."));
        }

        if (input.AccountType is not ("Checking" or "Savings"))
        {
            return (null, Bad("accountType", "Escolha conta corrente ou poupança."));
        }

        return Holder(input.HolderName, holderDoc) is { } error
            ? (null, error)
            : (new PaymentDetailsInput(PaymentMethodKind.Ted, BankCode: bank, Agency: input.Agency.Trim(), Account: input.Account.Trim(), AccountType: input.AccountType, HolderName: input.HolderName!.Trim(), HolderDocument: holderDoc), null);
    }
}
