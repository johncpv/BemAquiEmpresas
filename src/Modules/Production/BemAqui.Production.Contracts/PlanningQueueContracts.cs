namespace BemAqui.Production.Contracts;

/// <summary>
/// Fila de prioridade dos projetos (PLN-02 CA 9, ADR 0029 §3), do Production: os ids dos projetos com OS de fabricação aberta na ordem
/// da fila (a gravada, com quem ganhou a primeira OS já na posição da data prometida). Usada pelo motor (PLN-01a), pelo painel da TV
/// ("próximos na fila", PNL-01b) e pela simulação do orçamento (PLN-05). Só leitura. O Estoque não usa a fila para ordenar as faltas
/// (item 28 da Fase 3: primeiro a OS que originou a compra, depois a BOM liberada há mais tempo).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "Nome do contrato definido na PLN-02 e citado pelas stories PNL-01b e PLN-05: é a fila de prioridade, não uma coleção.")]
public interface IPlanningQueue
{
    Task<IReadOnlyList<Guid>> OrderAsync(CancellationToken cancellationToken = default);
}
