using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BemAqui.SharedKernel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Lookups;

/// <summary>Endereço público de um CEP (CAD-01b CA 10), já no formato do BemAqui.</summary>
public sealed record AddressData(string ZipCode, string? Street, string? District, string City, string State, string? IbgeCode);

/// <summary>Consulta de CEP (CAD-01b): reaproveitada pela CAD-06 e pela CAD-04b. Trocar o provedor não muda a tela (CA 15).</summary>
public interface ICepLookup
{
    Task<Result<AddressData>> LookupAsync(string cep, CancellationToken cancellationToken = default);
}

/// <summary>
/// CEP pela BrasilAPI (<c>/api/cep/v2</c>), com o código IBGE pelo município (<see cref="IIbgeLookup"/>). Dado público,
/// sem dado pessoal: cache em memória por 30 dias.
/// </summary>
public sealed partial class BrasilApiCepLookup(PublicDataHttp http, IMemoryCache cache, IOptions<LookupOptions> options, IIbgeLookup ibge) : ICepLookup
{
    public const string Integration = "cep:brasilapi";

    [GeneratedRegex("^[0-9]{8}$")]
    private static partial Regex CepPattern();

    public async Task<Result<AddressData>> LookupAsync(string cep, CancellationToken cancellationToken = default)
    {
        var digits = new string((cep ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (!CepPattern().IsMatch(digits))
        {
            return Error.Validation("validation", "CEP com 8 dígitos.", new Dictionary<string, string[]> { ["cep"] = ["CEP com 8 dígitos."] });
        }

        if (cache.TryGetValue("cep:" + digits, out AddressData? cached) && cached is not null)
        {
            return cached;
        }

        var (status, json) = await http.GetAsync("api/cep/v2/" + digits, Integration, cancellationToken);
        using (json)
        {
            if (status == LookupStatus.NotFound || (status == LookupStatus.Found && json is not null && Text(json.RootElement, "city") is null))
            {
                return Error.NotFound("cep_not_found", "CEP não encontrado. Preencha o endereço.");
            }

            if (status != LookupStatus.Found || json is null)
            {
                return Error.Unavailable("lookup_unavailable", "Não foi possível consultar o CEP agora.");
            }

            var root = json.RootElement;
            var state = (Text(root, "state") ?? string.Empty).ToUpperInvariant();
            var city = Text(root, "city")!;
            var address = new AddressData(digits, Text(root, "street"), Text(root, "neighborhood"), city, state.Length == 2 ? state : string.Empty, await ibge.FindAsync(state, city, cancellationToken));
            cache.Set("cep:" + digits, address, TimeSpan.FromDays(options.Value.CacheDays));
            return address;
        }
    }

    internal static string? Text(JsonElement element, string property) => PublicJson.Text(element, property);
}

/// <summary>Leitura defensiva da resposta pública (CAD-01b CA 13): só os campos esperados, como texto curto.</summary>
public static class PublicJson
{
    /// <summary>Só texto, até 200 caracteres; número também vira texto. Qualquer outra coisa é descartada.</summary>
    public static string? Text(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > 200 ? text[..200] : text;
    }

    /// <summary>Maiúsculas sem acento, para comparar nomes de município.</summary>
    public static string Fold(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var decomposed = name.ToUpperInvariant().Normalize(NormalizationForm.FormD);
        return new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Trim();
    }
}
