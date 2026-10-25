namespace BemAqui.Finance.Contracts;

/// <summary>Receita bruta de um mês (FIN-08 CA 15): <see cref="IsProvisional"/> enquanto a receita vem dos títulos, antes das notas reais.</summary>
public sealed record MonthlyRevenue(int Year, int Month, decimal Amount, bool IsProvisional);

/// <summary>Receita bruta de um projeto num mês (DSH-01b).</summary>
public sealed record ProjectMonthlyRevenue(Guid ProjectId, int Year, int Month, decimal Amount, bool IsProvisional);

/// <summary>
/// Resumo da receita para o painel do dono (FIN-08 CA 15, ADR 0028): a mesma regra da linha "Receita bruta" do DRE (receita pela
/// data da nota; antes das notas reais, pela emissão do título, "provisório"; o sinal fica em adiantamentos até a expedição ou o
/// marco seguinte). A DSH-01a usa o mensal; a DSH-01b, o por projeto. Meses sem receita vêm com zero.
/// </summary>
public interface IRevenueSummary
{
    /// <summary>Receita bruta por mês de <paramref name="from"/> a <paramref name="to"/> (meses inteiros), igual à linha do DRE.</summary>
    Task<IReadOnlyList<MonthlyRevenue>> GrossRevenueAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Receita bruta por projeto e mês: só a receita reconhecida dos títulos com projeto.</summary>
    Task<IReadOnlyList<ProjectMonthlyRevenue>> GrossRevenueByProjectsAsync(IReadOnlyCollection<Guid> projectIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
