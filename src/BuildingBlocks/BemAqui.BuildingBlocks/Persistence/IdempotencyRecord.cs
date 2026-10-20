namespace BemAqui.BuildingBlocks.Persistence;

/// <summary>
/// Envio já recebido com um <c>Idempotency-Key</c> (<c>shared.idempotency_records</c>, ADR 0019, FND-09e CA 15): o reenvio
/// com a mesma chave e o mesmo corpo devolve a resposta guardada; com outro corpo, é recusado.
/// </summary>
public sealed class IdempotencyRecord
{
    public Guid Key { get; init; }

    /// <summary>Método e rota do endpoint ("POST /api/v1/finance/transfers").</summary>
    public required string Scope { get; init; }

    /// <summary>SHA-256 do corpo enviado.</summary>
    public required string BodyHash { get; init; }

    /// <summary>Nulo enquanto o primeiro envio está em andamento.</summary>
    public int? StatusCode { get; set; }

    /// <summary>Corpo JSON da resposta original, como texto, para o reenvio sair idêntico.</summary>
    public string? Response { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}
