namespace BemAqui.Pos.Contracts;

/// <summary>
/// Caixa de uma loja para os outros módulos (PDV-01): o Identity registra o aparelho do caixa e mostra a loja e o caixa na lista de aparelhos.
/// <c>Label</c> é o texto curto da tela ("Loja Centro · Caixa 02").
/// </summary>
public sealed record PosRegisterRef(
    Guid StoreId,
    string StoreCode,
    string StoreName,
    Guid RegisterId,
    int RegisterNumber,
    Guid? ActiveDeviceId,
    int DeviceEpoch,
    bool IsActive)
{
    public string Label => $"{StoreName} · Caixa {RegisterNumber:00}";
}

/// <summary>O caixa depois de receber o aparelho: a época nova e o aparelho que saiu (null se o caixa estava sem aparelho).</summary>
public sealed record PosRegisterAssignment(PosRegisterRef Register, int Epoch, Guid? PreviousDeviceId);

/// <summary>
/// Caixas e o aparelho de cada um (PDV-01 CA 6, ADR 0048 §3). O <c>Pos</c> é o dono da tabela <c>pos.registers</c>; o Identity, dono do
/// aparelho, troca o aparelho do caixa por aqui, <b>na transação de quem chama</b> (os métodos de escrita só rastreiam a mudança; quem chama
/// grava), como o <c>IStockMovements</c>.
/// </summary>
public interface IPosRegisters
{
    /// <summary>O caixa (inclusive inativo, para o histórico); null se não existe.</summary>
    Task<PosRegisterRef?> FindAsync(Guid registerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, PosRegisterRef>> FindManyAsync(IReadOnlyCollection<Guid> registerIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Põe o aparelho no caixa: a época sobe (a sequência das vendas recomeça em 1 na época nova) e o aparelho anterior sai. Caixa inexistente
    /// ou inativo: null. Não grava.
    /// </summary>
    Task<PosRegisterAssignment?> AssignDeviceAsync(Guid registerId, Guid deviceId, CancellationToken cancellationToken = default);

    /// <summary>Tira o aparelho do caixa quando ele é revogado (só se ainda for o aparelho ativo do caixa). Não grava.</summary>
    Task ReleaseDeviceAsync(Guid registerId, Guid deviceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Número da venda do caixa (PDV-01 CA 6, rascunho A): <c>{loja}-{caixa}.{época}-{seq}</c>, por exemplo <c>LOJA-02.3-000127</c>. A época sobe a
/// cada troca de aparelho do caixa, então as vendas que o aparelho antigo ainda não enviou não colidem com as do novo, nem na chave
/// (<c>register_id</c>, <c>device_epoch</c>, <c>seq</c>) nem no número. O aparelho gera o número; o servidor só confere o formato.
/// </summary>
public static class PosSaleNumber
{
    public static string Format(string storeCode, int registerNumber, int epoch, long seq)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(registerNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(epoch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seq);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{storeCode}-{registerNumber:00}.{epoch}-{seq:000000}");
    }

    /// <summary>O prefixo da época (<c>LOJA-02.3-</c>), que o aparelho completa com a sequência.</summary>
    public static string Prefix(string storeCode, int registerNumber, int epoch) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{storeCode}-{registerNumber:00}.{epoch}-");
}
