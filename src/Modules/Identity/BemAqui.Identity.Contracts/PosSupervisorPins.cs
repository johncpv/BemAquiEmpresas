using BemAqui.SharedKernel;

namespace BemAqui.Identity.Contracts;

/// <summary>Supervisor da loja na lista do diálogo "Chamar supervisor": primeiro nome e inicial, e as ações do caixa que ele aprova.</summary>
public sealed record PosSupervisorOption(Guid UserId, string DisplayName, IReadOnlyList<string> Actions);

/// <summary>
/// PIN do supervisor digitado no aparelho do caixa (PDV-04 CA 10). <c>PosCookie</c> é o segredo do aparelho (cookie <c>__Host-bq_pos</c>);
/// <c>OperatorId</c>, o operador da sessão por PIN que pediu; <c>Permission</c>, a ação do catálogo (<c>pos.discount</c>, ...).
/// </summary>
public sealed record PosSupervisorPinRequest(string? PosCookie, Guid OperatorId, Guid SupervisorId, string Pin, string Permission)
{
    public override string ToString() => $"PosSupervisorPinRequest {{ OperatorId = {OperatorId}, SupervisorId = {SupervisorId}, Permission = {Permission} }}";
}

/// <summary>PIN aceito: o aparelho, a loja e o caixa em que a aprovação vale.</summary>
public sealed record PosSupervisorCheck(Guid DeviceId, Guid StoreId, Guid RegisterId);

/// <summary>
/// O aparelho de caixa do cookie <c>__Host-bq_pos</c> (PDV-06: a devolução acontece no caixa do aparelho, nunca no caixa que vem no corpo): o aparelho
/// ativo, a loja e o caixa; null sem cookie, desconhecido, revogado ou vencido.
/// </summary>
public interface IPosRegisterDevice
{
    Task<PosSupervisorCheck?> ResolveAsync(string? posCookie, CancellationToken cancellationToken = default);
}

/// <summary>
/// PIN de supervisor no balcão (PDV-04, exceção nomeada da PP-11: ADR 0048 §5, adendos das ADRs 0008 e 0026). O Identity é dono do PIN, do aparelho
/// e das permissões; o <c>Pos</c> grava a trilha (<c>pos.supervisor_approvals</c>). Mesmo cuidado da entrada por PIN: tempo uniforme, 5 erros
/// bloqueiam o PIN, 20 erros no aparelho bloqueiam o aparelho, tudo auditado e o PIN nunca em log.
/// </summary>
public interface IPosSupervisorPins
{
    /// <summary>Supervisores da loja do aparelho (com PIN do caixa e alguma ação de supervisor), sem o operador que pede.</summary>
    Task<IReadOnlyList<PosSupervisorOption>> SupervisorsAsync(string? posCookie, Guid operatorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confere o PIN do supervisor neste aparelho e a permissão da ação no instante. Recusa: o próprio operador (<c>supervisor_self</c>), PIN errado
    /// (com as tentativas restantes), PIN ou aparelho bloqueado, ou sem a permissão.
    /// </summary>
    Task<Result<PosSupervisorCheck>> VerifyAsync(PosSupervisorPinRequest request, CancellationToken cancellationToken = default);
}
