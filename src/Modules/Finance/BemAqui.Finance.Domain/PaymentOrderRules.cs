using BemAqui.Finance.Contracts;

namespace BemAqui.Finance.Domain;

/// <summary>Regras da OP sem banco (FIN-03a CA 9 e 14), para os casos de uso e os testes de unidade.</summary>
public static class PaymentOrderRules
{
    /// <summary>
    /// Quem solicitou decide a própria OP? (FIN-03a CA 9, FIN-10a CA 4 a 6, ADR 0054). Nunca: só o Proprietário único (D6 a); Só o
    /// Proprietário: quem é do Proprietário, com qualquer número de membros; Qualquer aprovador: quem chegou aqui com
    /// <c>finance.approve</c> (a alçada do nível 1 confere o resto). Devolve se pode e se é autoaprovação.
    /// </summary>
    public static (bool Allowed, bool Self) CanDecide(bool isRequester, bool requesterIsOwner, int ownerMembers, SelfApprovalMode mode)
    {
        if (!isRequester)
        {
            return (true, false);
        }

        var allowed = mode switch
        {
            SelfApprovalMode.AnyApprover => true,
            SelfApprovalMode.OwnerOnly => requesterIsOwner,
            _ => requesterIsOwner && ownerMembers == 1,
        };
        return (allowed, allowed);
    }

    /// <summary>Valor gravado em <c>finance.owner_can_approve_own</c>: 0, 1 ou 2; qualquer outro vale Nunca (falha fechada).</summary>
    public static SelfApprovalMode ModeOf(decimal stored) => stored switch
    {
        1m => SelfApprovalMode.OwnerOnly,
        2m => SelfApprovalMode.AnyApprover,
        _ => SelfApprovalMode.Never,
    };

    /// <summary>
    /// Modo pedido no PUT (FIN-10a CA 15): o campo novo vence; sem ele, o booleano legado só alterna Nunca e Só o Proprietário e nunca
    /// rebaixa Qualquer aprovador; sem nenhum dos dois, fica como está.
    /// </summary>
    public static SelfApprovalMode ResolveMode(SelfApprovalMode current, SelfApprovalMode? requested, bool? legacyOwnerCanApproveOwn)
    {
        if (requested is { } mode)
        {
            return mode;
        }

        return (legacyOwnerCanApproveOwn, current) switch
        {
            (true, SelfApprovalMode.Never) => SelfApprovalMode.OwnerOnly,
            (false, SelfApprovalMode.OwnerOnly) => SelfApprovalMode.Never,
            _ => current,
        };
    }

    /// <summary>Total que sai da conta por item: principal + juros + multa − desconto, em centavos.</summary>
    public static decimal TotalOf(decimal principal, decimal interest, decimal fine, decimal discount) =>
        Math.Round(principal + interest + fine - discount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Quanto o pagamento passa do aprovado mais a tolerância (CA 14); zero quando cabe.</summary>
    public static decimal Excess(decimal total, decimal approvedTotal, decimal tolerance) =>
        Math.Max(0, total - approvedTotal - tolerance);
}
