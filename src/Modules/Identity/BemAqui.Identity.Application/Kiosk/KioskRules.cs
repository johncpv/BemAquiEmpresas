using System.Net;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Identity.Domain;

namespace BemAqui.Identity.Application.Kiosk;

/// <summary>Token novo de uma tela: o valor vai só para o link mostrado uma vez; no banco ficam o hash e o prefixo.</summary>
public sealed record KioskToken(string Value, string Hash, string Prefix);

/// <summary>Regras puras do Painel da TV (PNL-01a CA 3, 7, 10 e 16; ADR 0030): token, redes, visões, rotação e nome.</summary>
public static class KioskRules
{
    public const string InvalidLinkMessage = "Link inválido ou desligado.";
    public const string NetworkMessage = "Esta tela só funciona na rede da fábrica.";
    public const string ReconnectMessage = "Esta tela precisa ser conectada de novo. Peça um novo link a quem cuida do painel.";
    public const string UnavailableMessage = "Painel indisponível nesta versão do sistema.";

    /// <summary>Tempo mínimo da resposta de falha da troca do token (tempo uniforme, ADR 0030 §1).</summary>
    public static readonly TimeSpan UniformFailure = TimeSpan.FromMilliseconds(250);

    /// <summary>256 bits aleatórios (32 bytes, base64url), SHA-256 no banco e os 6 primeiros caracteres para identificar.</summary>
    public static KioskToken NewToken()
    {
        var value = OpaqueToken.New();
        return new KioskToken(value, OpaqueToken.Hash(value), value[..KioskScreen.PrefixLength]);
    }

    /// <summary>Token ou cookie que vale a pena procurar: nem vazio nem maior que o formato (contra abuso do hash).</summary>
    public static bool IsPlausible(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length is >= 40 and <= 64;

    public static string? NameProblem(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        return value.Length is 0 or > KioskScreen.MaxNameLength ? "Dê um nome de até 60 letras à tela, por exemplo TV da Montagem." : null;
    }

    public static string? RotationProblem(int seconds) =>
        seconds is < KioskScreen.MinRotationSeconds or > KioskScreen.MaxRotationSeconds ? "A rotação vai de 10 a 120 segundos." : null;

    /// <summary>Visões escolhidas na ordem do painel; vazio = todas. Visão desconhecida é erro.</summary>
    public static (string[] Views, string? Problem) NormalizeViews(IEnumerable<string>? views)
    {
        var chosen = (views ?? []).Select(v => (v ?? string.Empty).Trim()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (chosen.Count == 0)
        {
            return ([.. KioskViews.All], null);
        }

        return chosen.Any(v => !KioskViews.All.Contains(v, StringComparer.Ordinal))
            ? ([], "Escolha entre Quadro por etapa, Fila e previsões e Atrasos.")
            : ([.. KioskViews.All.Where(chosen.Contains)], null);
    }

    /// <summary>
    /// Redes permitidas (CA 16): até 5, em CIDR (<c>192.168.10.0/24</c>) ou um IP só; gravadas na forma canônica. Prefixo 0 (qualquer rede)
    /// não é aceito: para liberar qualquer rede, deixe a lista vazia.
    /// </summary>
    public static (string[] Networks, string? Problem) NormalizeNetworks(IEnumerable<string>? networks)
    {
        var items = (networks ?? []).Select(n => (n ?? string.Empty).Trim()).Where(n => n.Length > 0).ToList();
        if (items.Count > KioskScreen.MaxNetworks)
        {
            return ([], "Informe no máximo 5 redes.");
        }

        var result = new List<string>();
        foreach (var item in items)
        {
            if (TryParseNetwork(item) is not { } network || network.PrefixLength == 0)
            {
                return ([], $"A rede {item} não é válida. Use o formato 192.168.10.0/24.");
            }

            var canonical = network.ToString();
            if (!result.Contains(canonical, StringComparer.Ordinal))
            {
                result.Add(canonical);
            }
        }

        return ([.. result], null);
    }

    /// <summary>CIDR ou IP único; os bits de host do endereço são zerados (192.168.10.7/24 vira 192.168.10.0/24).</summary>
    private static IPNetwork? TryParseNetwork(string text)
    {
        var slash = text.IndexOf('/', StringComparison.Ordinal);
        if (!IPAddress.TryParse(slash < 0 ? text : text[..slash], out var address))
        {
            return null;
        }

        var bytes = address.GetAddressBytes();
        var bits = bytes.Length * 8;
        var prefix = bits;
        if (slash >= 0 && (!int.TryParse(text[(slash + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out prefix) || prefix > bits))
        {
            return null;
        }

        for (var i = 0; i < bytes.Length; i++)
        {
            var keep = Math.Clamp(prefix - (i * 8), 0, 8);
            bytes[i] &= (byte)(0xFF << (8 - keep));
        }

        return new IPNetwork(new IPAddress(bytes), prefix);
    }

    /// <summary>IP real (do proxy confiável) dentro de alguma rede permitida; lista vazia = qualquer rede.</summary>
    public static bool IsAllowed(IPAddress? address, IReadOnlyCollection<string> networks)
    {
        ArgumentNullException.ThrowIfNull(networks);
        if (networks.Count == 0)
        {
            return true;
        }

        if (address is null)
        {
            return false;
        }

        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return networks.Any(n => IPNetwork.TryParse(n, out var network) && network.Contains(ip));
    }

    /// <summary>Link da tela: o token no fragmento, que o navegador não envia ao servidor (ADR 0030 §1).</summary>
    public static string Link(string host, string token) => "https://" + host + "/tv#" + token;
}
