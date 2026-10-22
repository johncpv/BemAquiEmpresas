namespace BemAqui.Production.Application;

/// <summary>
/// Ponto de extensão do bloqueio por BOM no início de etapa (PRD-03a CA 9, D12): a primeira etapa inicia sem BOM; as seguintes
/// exigem a BOM da OS liberada. Até a ENG-02, vale <see cref="NoBomGate"/> (nunca bloqueia); a ENG-02 troca pelo
/// <c>IBoms.IsReleasedAsync</c> e liga o bloqueio também na "Concluir etapa" do supervisor (ENG-02 CA 16).
/// </summary>
public interface IBomGate
{
    Task<bool> IsReleasedAsync(Guid productionOrderId, CancellationToken cancellationToken = default);
}

/// <summary>Sem a ENG-02: todas as etapas iniciam sem conferir BOM.</summary>
public sealed class NoBomGate : IBomGate
{
    public Task<bool> IsReleasedAsync(Guid productionOrderId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}