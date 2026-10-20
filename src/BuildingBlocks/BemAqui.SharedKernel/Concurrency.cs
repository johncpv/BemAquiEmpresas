namespace BemAqui.SharedKernel;

/// <summary>
/// Documento com concorrência otimista (ADR 0019, FND-09e CA 13): <see cref="Version"/> é o <c>xmin</c> do PostgreSQL. O DTO
/// de detalhe expõe a versão e o comando a devolve; duas gravações sobre a mesma versão dão 409 <c>concurrency_conflict</c>.
/// </summary>
public interface IConcurrencyVersioned
{
    uint Version { get; set; }
}

/// <summary>Nome do documento na mensagem de conflito ("{Documento} mudou desde que você abriu. Recarregue.").</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DocumentNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
