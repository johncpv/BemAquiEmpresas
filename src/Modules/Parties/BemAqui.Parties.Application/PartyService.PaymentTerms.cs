using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Parties.Application;

public sealed partial class PartyService
{
    /// <summary>Condições ativas para o seletor da aba Dados (CA 16), lidas do Finance pelo contrato.</summary>
    public async Task<IReadOnlyList<PaymentTermOption>> PaymentTermOptionsAsync(CancellationToken cancellationToken = default) =>
        (await paymentTerms.ActiveAsync(cancellationToken)).Select(t => new PaymentTermOption(t.Id, t.Name)).ToList();

    /// <summary>
    /// Condição nova precisa existir e estar ativa (CA 16). Manter a mesma que ficou inativa é permitido: ela continua no
    /// terceiro, com o chip "Inativa" (CA 17).
    /// </summary>
    private async Task<Error?> InvalidTermAsync(Guid? wanted, Guid? current, CancellationToken cancellationToken)
    {
        if (wanted is not { } id || id == current)
        {
            return null;
        }

        var term = await paymentTerms.FindAsync(id, cancellationToken);
        return term is { IsActive: true } ? null
            : Error.Validation("validation", "Condição de pagamento inválida.", new Dictionary<string, string[]> { ["defaultPaymentTermId"] = ["Condição de pagamento inválida."] });
    }

    private async Task<PaymentTermRef?> TermRefAsync(Guid? id, CancellationToken cancellationToken) =>
        id is { } termId && await paymentTerms.FindAsync(termId, cancellationToken) is { } term ? new PaymentTermRef(term.Id, term.Name, term.IsActive) : null;
}

/// <summary>Uso de uma condição como padrão de terceiro (CA 17): em uso, a condição não é excluída, só inativada.</summary>
public sealed class PartyPaymentTermUsage(IPartiesDb db) : IPaymentTermUsageProvider
{
    public async Task<string?> UsageAsync(Guid termId, CancellationToken cancellationToken = default)
    {
        var count = await db.Parties.CountAsync(p => p.DefaultPaymentTermId == termId, cancellationToken);
        return count == 0 ? null : count == 1 ? "condição padrão de 1 terceiro" : $"condição padrão de {count} terceiros";
    }
}
