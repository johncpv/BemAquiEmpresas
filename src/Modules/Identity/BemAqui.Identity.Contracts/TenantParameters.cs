using System.Text.Json.Serialization;

namespace BemAqui.Identity.Contracts;

/// <summary>Como o preço sai do custo (P-14): markup sobre o custo ou margem sobre o preço.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PricingMode>))]
public enum PricingMode
{
    Markup,
    Margin,
}

/// <summary>
/// Parâmetros de cálculo do tenant para os outros módulos (CAD-06 CA 19): o HR (custo-hora, CAD-04a) e o Commercial
/// (precificação, COM-01) leem por aqui, sem exigir <c>settings.view</c> do usuário.
/// </summary>
public interface ITenantParameters
{
    /// <summary>% de encargos estimados sobre o salário (ex.: 80,00).</summary>
    Task<decimal> PayrollChargesPctAsync(CancellationToken cancellationToken = default);

    Task<PricingMode> PricingModeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Envios de documento por e-mail por usuário por dia (CMP-03b CA 5, <c>email.dispatch_max_per_user_day</c>): orçamentos e pedidos de
    /// compra somados; padrão 20, de 1 a 50 (o teto do tenant).
    /// </summary>
    Task<int> EmailDispatchMaxPerUserDayAsync(CancellationToken cancellationToken = default);
}
