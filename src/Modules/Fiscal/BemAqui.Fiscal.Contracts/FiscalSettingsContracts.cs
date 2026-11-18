using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Contracts;

/// <summary>
/// Valores de Configurações → Fiscal mudaram (FIS-02a): só as chaves (nunca os valores), para a FIS-02b listar as regras a revisar.
/// <paramref name="RegimeChanged"/> avisa que as regras do regime anterior ficam inativas (CA 9).
/// </summary>
public sealed record FiscalSettingsChanged(IReadOnlyList<string> ChangedKeys, bool RegimeChanged) : IIntegrationEvent;

/// <summary>
/// Situação da configuração fiscal para o checklist "Primeiros passos" do Início (PERF-04 CA 6 e 7): o regime tributário foi conferido
/// (a conferência vale para o valor em uso, pelo hash, ADR 0042). Só um sim ou não, sem o valor.
/// </summary>
public interface IFiscalSetupStatus
{
    Task<bool> RegimeConfirmedAsync(CancellationToken cancellationToken = default);
}
