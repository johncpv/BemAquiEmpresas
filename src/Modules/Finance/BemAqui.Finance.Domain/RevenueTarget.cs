using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Meta de faturamento de um mês (DSH-01a CA 9, ADR 0028 §1): <c>finance.revenue_targets</c>, única por ano e mês,
/// <c>numeric(18,2)</c> e ≥ 0; editada em Configurações → Parâmetros com <c>settings.edit</c>, com auditoria. Mês sem linha
/// não tem meta.
/// </summary>
public sealed class RevenueTarget : Entity, IAuditable
{
    public int Year { get; init; }

    public int Month { get; init; }

    public decimal Amount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
