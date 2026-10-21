using BemAqui.Finance.Application;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>FIN-06b CA 2: atraso médio ponderado pelo valor, antecipação conta 0, % pago com atraso.</summary>
public class PaymentStatsTests
{
    private static readonly Guid Party = Guid.NewGuid();
    private static readonly DateOnly Due = new(2026, 6, 10);

    [Fact]
    public void Atraso_ponderado_pelo_valor_e_antecipacao_conta_zero()
    {
        var (avg, late) = PaymentStatsService.Compute([
            new SettledParcel(Party, 1_000m, Due, Due.AddDays(10)),
            new SettledParcel(Party, 3_000m, Due, Due.AddDays(-5)),
        ]);
        avg.Should().Be(2.5m, "(1.000 × 10 + 3.000 × 0) ÷ 4.000");
        late.Should().Be(25m);
    }

    [Fact]
    public void Sem_titulo_quitado_fica_zerado()
    {
        PaymentStatsService.Compute([]).Should().Be((0m, 0m));
    }
}
