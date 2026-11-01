using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Domain;

/// <summary>
/// Instalação aceita à espera da NFS-e (<c>fiscal.installation_billings</c>, FIS-06 CA 1): gravada pelo consumidor do <c>InstallationAccepted</c>
/// (COM-06), uma linha por OS (índice único). A fila "A emitir" e a pauta mostram as linhas sem NFS-e ativa cujo projeto tem itens
/// <c>Service</c> ainda não faturados. Só ids e a data (ADR 0006); nunca é apagada.
/// </summary>
public sealed class InstallationBilling : Entity
{
    public Guid InstallationOrderId { get; init; }

    public Guid ProjectId { get; init; }

    public DateOnly AcceptedOn { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
