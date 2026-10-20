namespace BemAqui.HR.Contracts;

/// <summary>Cargo para os seletores dos outros módulos (COM-01a CA 9): sem salário; o custo-hora vem de <see cref="IHourlyCost"/>.</summary>
public sealed record PositionOption(Guid Id, string Name, Guid? DefaultWorkCenterId, bool IsActive);

/// <summary>Cargos para Commercial e Production: só ativos nos seletores, por id inclusive inativos.</summary>
public interface IPositions
{
    Task<IReadOnlyList<PositionOption>> ActiveAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PositionOption>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
