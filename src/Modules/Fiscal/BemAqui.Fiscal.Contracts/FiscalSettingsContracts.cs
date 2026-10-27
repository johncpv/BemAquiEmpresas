using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Contracts;

/// <summary>
/// Valores de Configurações → Fiscal mudaram (FIS-02a): só as chaves (nunca os valores), para a FIS-02b listar as regras a revisar.
/// <paramref name="RegimeChanged"/> avisa que as regras do regime anterior ficam inativas (CA 9).
/// </summary>
public sealed record FiscalSettingsChanged(IReadOnlyList<string> ChangedKeys, bool RegimeChanged) : IIntegrationEvent;
