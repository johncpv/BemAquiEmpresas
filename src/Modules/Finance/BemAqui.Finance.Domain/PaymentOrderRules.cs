namespace BemAqui.Finance.Domain;

/// <summary>Regras da OP sem banco (FIN-03a CA 9 e 14), para os casos de uso e os testes de unidade.</summary>
public static class PaymentOrderRules
{
    /// <summary>
    /// Quem solicitou não aprova a própria OP (CA 9), exceto: o Proprietário tem um único membro e é ele; ou o Proprietário
    /// tem mais membros e o parâmetro <c>finance.owner_can_approve_own</c> está ligado. Devolve se pode e se é autoaprovação.
    /// </summary>
    public static (bool Allowed, bool Self) CanDecide(bool isRequester, bool requesterIsOwner, int ownerMembers, bool ownerCanApproveOwn)
    {
        if (!isRequester)
        {
            return (true, false);
        }

        var allowed = requesterIsOwner && (ownerMembers == 1 || ownerCanApproveOwn);
        return (allowed, allowed);
    }

    /// <summary>Total que sai da conta por item: principal + juros + multa − desconto, em centavos.</summary>
    public static decimal TotalOf(decimal principal, decimal interest, decimal fine, decimal discount) =>
        Math.Round(principal + interest + fine - discount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Quanto o pagamento passa do aprovado mais a tolerância (CA 14); zero quando cabe.</summary>
    public static decimal Excess(decimal total, decimal approvedTotal, decimal tolerance) =>
        Math.Max(0, total - approvedTotal - tolerance);
}
