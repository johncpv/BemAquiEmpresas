namespace BemAqui.Commercial.Contracts;

/// <summary>Parâmetros do Commercial (ADR 0016 adendo M6): % padrão de markup ou margem; nulo enquanto o tenant não definiu.</summary>
public interface ICommercialParameters
{
    Task<decimal?> DefaultPricingPctAsync(CancellationToken cancellationToken = default);
}
