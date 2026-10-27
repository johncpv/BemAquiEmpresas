using BemAqui.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Email;

public sealed record EmailLogStatus(Guid Id, EmailStatus Status, string? Error, DateTimeOffset? SentAt);

/// <summary>Situação dos e-mails enviados por um documento (COM-03 CA 8 e 13), sem expor a tabela <c>email_logs</c> aos módulos.</summary>
public sealed class EmailLogReader(BemAquiDbContext db)
{
    public async Task<IReadOnlyList<EmailLogStatus>> StatusAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await db.EmailLogs.AsNoTracking().Where(l => ids.Contains(l.Id)).Select(l => new EmailLogStatus(l.Id, l.Status, l.Error, l.SentAt)).ToListAsync(cancellationToken);

    /// <summary>Envios de um documento pelo dono (ex.: boleto, BNK-01b CA 13), do mais novo ao mais antigo.</summary>
    public async Task<IReadOnlyList<EmailLogEntry>> ForAsync(string relatedType, Guid relatedId, CancellationToken cancellationToken = default) =>
        await db.EmailLogs.AsNoTracking().Where(l => l.RelatedType == relatedType && l.RelatedId == relatedId).OrderByDescending(l => l.CreatedAt)
            .Select(l => new EmailLogEntry(l.Id, l.To, l.Status, l.Error, l.CreatedAt, l.SentAt)).ToListAsync(cancellationToken);
}

public sealed record EmailLogEntry(Guid Id, string To, EmailStatus Status, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);
