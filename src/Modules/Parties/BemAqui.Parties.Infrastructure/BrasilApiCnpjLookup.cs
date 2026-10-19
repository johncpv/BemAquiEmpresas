using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Lookups;
using BemAqui.Parties.Application;
using BemAqui.SharedKernel;

namespace BemAqui.Parties.Infrastructure;

/// <summary>
/// CNPJ pela BrasilAPI (<c>/api/cnpj/v1/{cnpj}</c>, CAD-01b). Só os campos do cadastro saem da resposta: o quadro societário
/// (QSA) e o resto são descartados aqui (CA 14). Sem nova tentativa e sem provedor reserva (CA 5).
/// </summary>
public sealed partial class BrasilApiCnpjLookup(PublicDataHttp http) : ICnpjLookup
{
    public const string Integration = "cnpj:brasilapi";

    [GeneratedRegex("^[0-9A-Z]{12}[0-9]{2}$")]
    private static partial Regex CnpjPattern();

    public async Task<Result<CnpjData>> LookupAsync(string cnpj, CancellationToken cancellationToken = default)
    {
        // Regex antes de montar a URL (CA 13): nada além de 14 caracteres alfanuméricos chega ao provedor.
        if (!CnpjPattern().IsMatch(cnpj ?? string.Empty))
        {
            return Error.Validation("validation", "CNPJ inválido.", new Dictionary<string, string[]> { ["cnpj"] = ["CNPJ inválido."] });
        }

        var (status, json) = await http.GetAsync("api/cnpj/v1/" + cnpj, Integration, cancellationToken);
        using (json)
        {
            if (status == LookupStatus.NotFound)
            {
                return Error.NotFound("cnpj_not_found", "CNPJ não encontrado na base pública. Confira o número ou preencha os dados.");
            }

            if (status != LookupStatus.Found || json is null || PublicJson.Text(json.RootElement, "razao_social") is not { } legalName)
            {
                return Error.Unavailable("lookup_unavailable", "Não foi possível consultar o CNPJ agora. Preencha os dados ou tente de novo.");
            }

            var r = json.RootElement;
            string? T(string name) => PublicJson.Text(r, name);
            var zip = new string((T("cep") ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
            var streetType = T("descricao_tipo_de_logradouro");
            var street = T("logradouro") is { } s ? (streetType is not null && !s.StartsWith(streetType, StringComparison.OrdinalIgnoreCase) ? streetType + " " + s : s) : null;
            var ibge = T("codigo_municipio_ibge") is { Length: 7 } code && code.All(char.IsAsciiDigit) ? code : null;
            var state = T("uf")?.ToUpperInvariant();
            var address = new CnpjAddress(zip.Length == 8 ? zip : null, Title(street), T("numero"), Title(T("complemento")), Title(T("bairro")), Title(T("municipio")), state is { Length: 2 } ? state : null, ibge);
            var phone = new string((T("ddd_telefone_1") ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
            return new CnpjData(cnpj!, legalName, T("nome_fantasia"), T("descricao_situacao_cadastral"), address,
                phone.Length is 10 or 11 ? phone : null, T("email")?.ToLowerInvariant());
        }
    }

    /// <summary>A Receita escreve em maiúsculas: "RUA DAS FLORES" vira "Rua das Flores" (o usuário pode ajustar).</summary>
    public static string? Title(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] small = ["da", "das", "de", "do", "dos", "e"];
        var words = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select((w, i) => i > 0 && small.Contains(w) ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
