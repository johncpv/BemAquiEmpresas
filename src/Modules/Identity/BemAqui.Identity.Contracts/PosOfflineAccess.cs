using System.Globalization;

namespace BemAqui.Identity.Contracts;

/// <summary>
/// Verificador sem rede de uma pessoa da loja, como desce para o aparelho de caixa (OFF-03 CA 1, adendo à ADR 0026). O segredo (PIN do operador ou
/// código de aprovação sem rede do supervisor, PP-39) nunca desce: descem os parâmetros da função lenta que já protege o segredo no servidor
/// (PBKDF2, com o sal e as iterações do hash guardado) e o verificador <c>HMAC-SHA256(chave derivada, mensagem do aparelho)</c>, com um
/// <c>Nonce</c> novo a cada descida e o id do aparelho na mensagem. Quem tem o verificador não tem a chave derivada: não forja a prova que o servidor
/// confere na subida (<see cref="PosOfflineProof"/>); quebrar exige tentar os 10<sup>6</sup> segredos com a função lenta, com o aparelho na mão.
/// <c>Kind</c>: <c>operator</c> (entra no caixa) ou <c>supervisor</c> (só aprova). <c>Actions</c>: as ações do caixa (sem o <c>pos.</c>) no momento da
/// descida, para a tela; o servidor reconfere a permissão no instante de cada evento.
/// </summary>
public sealed record PosOfflineVerifier(
    Guid UserId,
    string DisplayName,
    string Kind,
    string Algorithm,
    int Iterations,
    string Salt,
    int KeyLength,
    string Nonce,
    string Verifier,
    IReadOnlyList<string> Actions);

/// <summary>Resultado da conferência de uma entrada ou aprovação feita sem rede (OFF-03 CA 4 e 7).</summary>
public enum PosOfflineCheck
{
    /// <summary>A prova confere com o segredo atual e a pessoa está ativa e na loja.</summary>
    Valid,

    /// <summary>A pessoa foi desativada, saiu da loja ou perdeu o PIN do caixa depois da descida.</summary>
    UserInactive,

    /// <summary>O segredo mudou (ou foi apagado) depois da descida: a prova feita com o antigo não confere mais.</summary>
    SecretChanged,

    /// <summary>A prova não confere (forjada, ausente ou de outro aparelho).</summary>
    InvalidProof,
}

/// <summary>
/// Entrada e aprovação sem rede para o <c>Pos</c> (OFF-03). O Identity é o dono do PIN, do código e da auditoria; o <c>Pos</c> pede os verificadores
/// na descida e manda conferir as provas na subida.
/// </summary>
public interface IPosOfflineAccess
{
    /// <summary>
    /// Os verificadores dos operadores e supervisores da loja para este aparelho (só os da loja; quem tem o PIN para trocar fica de fora). Grava em
    /// <c>identity.device_pin_verifiers</c> o que desceu e apaga as linhas de quem saiu da lista.
    /// </summary>
    Task<IReadOnlyList<PosOfflineVerifier>> VerifiersAsync(Guid deviceId, Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>Confere a entrada sem rede (CA 2 e 4) e grava a auditoria (<c>PosPinOfflineLogin</c>), sem o PIN.</summary>
    Task<PosOfflineCheck> CheckLoginAsync(Guid deviceId, Guid storeId, Guid userId, string message, string? proof, CancellationToken cancellationToken = default);

    /// <summary>Confere a aprovação sem rede com o código do supervisor (CA 7 e 15) e grava a auditoria (<c>PosOfflineApproval</c>).</summary>
    Task<PosOfflineCheck> CheckApprovalAsync(Guid deviceId, Guid storeId, Guid supervisorId, string message, string? proof, CancellationToken cancellationToken = default);

    /// <summary>Bloqueio sem rede por 5 erros (CA 3): chega na sincronização e vai para a auditoria (<c>PosPinOfflineLocked</c>).</summary>
    Task RecordLockAsync(Guid deviceId, Guid userId, string kind, DateTimeOffset at, CancellationToken cancellationToken = default);
}

/// <summary>
/// As mensagens assinadas do protocolo sem rede (OFF-03), iguais no aparelho (<c>src/pdv/offline/offline-crypto.ts</c>) e no servidor. Ids em
/// minúsculas com hífen; instantes como vieram no evento; dinheiro em centavos.
/// </summary>
public static class PosOfflineProof
{
    public const string Algorithm = "HMAC-SHA256";

    public static string VerifierMessage(string kind, Guid deviceId, Guid userId, string nonce) =>
        $"bq-pos-offline-verifier|v1|{kind}|{deviceId:D}|{userId:D}|{nonce}";

    public static string LoginMessage(Guid deviceId, Guid userId, Guid loginId, string at) =>
        $"bq-pos-offline-login|v1|{deviceId:D}|{userId:D}|{loginId:D}|{at}";

    public static string ApprovalMessage(Guid deviceId, Guid approvalId, string action, Guid operatorId, Guid supervisorId, Guid saleId, Guid? lineId, long beforeCents,
        long afterCents, string at) =>
        string.Create(CultureInfo.InvariantCulture,
            $"bq-pos-offline-approval|v1|{deviceId:D}|{approvalId:D}|{action}|{operatorId:D}|{supervisorId:D}|{saleId:D}|{(lineId is { } l ? l.ToString("D") : string.Empty)}|{beforeCents}|{afterCents}|{at}");
}
