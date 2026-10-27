using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BemAqui.Fiscal.Application.Emitter;

/// <summary>
/// Forma canônica do pedido de NF-e (FIS-01 CA 4 e 5): valores com 2 casas, quantidades com 4, preço unitário e alíquotas com até 4
/// (o <c>vUnCom</c> aceita até 10; com 2, quantidade × unitário deixaria de bater com o total). O adaptador envia exatamente esta forma,
/// e o <see cref="Hash"/> (SHA-256 em hex) é o <c>payload_hash</c> gravado na nota ao entrar em <c>Processing</c>.
/// </summary>
public static class NfePayload
{
    public const int MoneyDecimals = 2;
    public const int QuantityDecimals = 4;
    public const int UnitPriceDecimals = 4;
    public const int RateDecimals = 4;

    private static readonly JsonSerializerOptions Canonical = new()
    {
        Converters = { new CanonicalDecimalConverter(), new JsonStringEnumConverter(allowIntegerValues: false) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    public static decimal Money(decimal value) => Math.Round(value, MoneyDecimals, MidpointRounding.AwayFromZero);

    public static decimal? Money(decimal? value) => value is { } v ? Money(v) : null;

    public static decimal Quantity(decimal value) => Math.Round(value, QuantityDecimals, MidpointRounding.AwayFromZero);

    public static decimal UnitPrice(decimal value) => Math.Round(value, UnitPriceDecimals, MidpointRounding.AwayFromZero);

    public static decimal Rate(decimal value) => Math.Round(value, RateDecimals, MidpointRounding.AwayFromZero);

    public static decimal? Rate(decimal? value) => value is { } v ? Rate(v) : null;

    /// <summary>O pedido com o arredondamento aplicado (o que vai para o Emissor).</summary>
    public static NfeIssueRequest Normalize(NfeIssueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request with
        {
            Items = request.Items.Select(i => i with
            {
                Quantity = Quantity(i.Quantity),
                UnitPrice = UnitPrice(i.UnitPrice),
                Total = Money(i.Total),
                Discount = Money(i.Discount),
                Freight = Money(i.Freight),
                Insurance = Money(i.Insurance),
                OtherCosts = Money(i.OtherCosts),
                Taxes = i.Taxes with
                {
                    Icms = i.Taxes.Icms with
                    {
                        Base = Money(i.Taxes.Icms.Base), Rate = Rate(i.Taxes.Icms.Rate), Amount = Money(i.Taxes.Icms.Amount), BaseReduction = Rate(i.Taxes.Icms.BaseReduction),
                        StBase = Money(i.Taxes.Icms.StBase), StRate = Rate(i.Taxes.Icms.StRate), StAmount = Money(i.Taxes.Icms.StAmount), StMargin = Rate(i.Taxes.Icms.StMargin),
                        FcpBase = Money(i.Taxes.Icms.FcpBase), FcpRate = Rate(i.Taxes.Icms.FcpRate), FcpAmount = Money(i.Taxes.Icms.FcpAmount),
                    },
                    Pis = PisCofins(i.Taxes.Pis),
                    Cofins = PisCofins(i.Taxes.Cofins),
                    Ipi = i.Taxes.Ipi is { } ipi ? ipi with { Base = Money(ipi.Base), Rate = Rate(ipi.Rate), Amount = Money(ipi.Amount) } : null,
                    IbsCbs = i.Taxes.IbsCbs is { } ibs ? ibs with { Base = Money(ibs.Base), StateIbs = TaxRate(ibs.StateIbs), CityIbs = TaxRate(ibs.CityIbs), Cbs = TaxRate(ibs.Cbs) } : null,
                },
            }).ToList(),
            Payments = request.Payments.Select(p => p with { Amount = Money(p.Amount) }).ToList(),
            ReferencedAccessKeys = request.ReferencedAccessKeys ?? [],
        };
    }

    /// <summary>SHA-256 (hex minúsculo) da forma canônica; o mesmo pedido dá sempre o mesmo hash, com 1 ou 1,00.</summary>
    public static string Hash(NfeIssueRequest request)
    {
        var json = JsonSerializer.Serialize(Normalize(request), Canonical);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static NfePisCofins PisCofins(NfePisCofins value) => value with { Base = Money(value.Base), Rate = Rate(value.Rate), Amount = Money(value.Amount) };

    private static NfeTaxRate? TaxRate(NfeTaxRate? value) => value is { } v ? new NfeTaxRate(Rate(v.Rate), Money(v.Amount)) : null;

    /// <summary>Decimal sem zeros à direita (1,00 e 1 são o mesmo valor no hash).</summary>
    private sealed class CanonicalDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue((value / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture));
    }
}
