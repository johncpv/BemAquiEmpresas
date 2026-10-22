using BemAqui.SharedKernel;

namespace BemAqui.Production.Domain;

/// <summary>
/// Parâmetro da Produção (<c>production.parameters</c>, decisão do PO de 22/10/2026 na PRD-04): vazio = padrão. Editado em
/// Configurações → Parâmetros com <c>settings.edit</c>, auditado (dono <c>settings</c>).
/// </summary>
public sealed class ProductionParameter : Entity, IAuditable
{
    public string Key { get; init; } = string.Empty;

    public decimal? Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}