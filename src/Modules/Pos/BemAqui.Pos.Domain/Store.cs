using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BemAqui.SharedKernel;

namespace BemAqui.Pos.Domain;

/// <summary>
/// Loja (<c>pos.stores</c>, PDV-01 CA 2): código curto (único, entra no número da venda), nome, local de estoque (Estoque, só o id) e a tabela de
/// preço padrão (Catálogo, só o id). O CNPJ próprio da loja (estabelecimento) é da NFCE-07; o modo fiscal e os parâmetros da loja chegam com as
/// stories que os usam (PP-05 e PP-22). Não é apagada: com caixa, só desativa.
/// </summary>
[DocumentName("Loja")]
public sealed class Store : Entity, IAuditable, IConcurrencyVersioned
{
    public const int MaxNameLength = 80;

    /// <summary>Chave do pacote de dados iniciais do Comércio (<c>loja</c>); null para as criadas pelo tenant.</summary>
    public const string RetailSeedKey = "loja";

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Guid StockLocationId { get; set; }

    public Guid DefaultPriceListId { get; set; }

    public bool IsActive { get; set; } = true;

    public string? SeedKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }
}

/// <summary>Modo de tela do caixa (PDV-01 CA 3): toque (grade de produtos) ou teclado e leitor de código de barras.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RegisterUiMode>))]
public enum RegisterUiMode
{
    Touch,
    Keyboard,
}

/// <summary>
/// Caixa de uma loja (<c>pos.registers</c>, PDV-01 CA 3 e 6): número único na loja, a conta do tipo Caixa do Financeiro (só o id, única entre os
/// caixas), o modo de tela e o aparelho ativo. <see cref="DeviceEpoch"/> sobe a cada troca de aparelho: a sequência das vendas recomeça em 1.
/// </summary>
[DocumentName("Caixa")]
public sealed class Register : Entity, IAuditable, IConcurrencyVersioned
{
    public const int MaxNumber = 99;

    public Guid StoreId { get; init; }

    public int Number { get; set; }

    public Guid CashAccountId { get; set; }

    public RegisterUiMode UiMode { get; set; } = RegisterUiMode.Keyboard;

    public Guid? ActiveDeviceId { get; private set; }

    /// <summary>Época do aparelho: 0 antes do primeiro aparelho; 1, 2... a cada aparelho registrado no caixa.</summary>
    public int DeviceEpoch { get; private set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }

    /// <summary>Aparelho novo no caixa (CA 6): a época sobe e devolve o aparelho que saiu.</summary>
    public Guid? AssignDevice(Guid deviceId)
    {
        var previous = ActiveDeviceId;
        ActiveDeviceId = deviceId;
        DeviceEpoch++;
        return previous;
    }

    /// <summary>O aparelho foi revogado: o caixa fica sem aparelho (a época continua; o próximo aparelho abre outra).</summary>
    public void ReleaseDevice(Guid deviceId)
    {
        if (ActiveDeviceId == deviceId)
        {
            ActiveDeviceId = null;
        }
    }
}

/// <summary>Regras de cadastro da loja e do caixa (PDV-01 CA 2 e 3).</summary>
public static partial class PosRules
{
    /// <summary>Código da loja: 2 a 6 letras maiúsculas ou números (entra no número da venda: <c>LOJA-01.1-000001</c>).</summary>
    public static bool IsValidStoreCode(string code) => StoreCodePattern().IsMatch(code);

    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public static bool IsValidRegisterNumber(int number) => number is >= 1 and <= Register.MaxNumber;

    [GeneratedRegex("^[A-Z0-9]{2,6}$")]
    private static partial Regex StoreCodePattern();
}
