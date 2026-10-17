using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Notifications;

public sealed record NotificationDto(Guid Id, string Type, string Title, string Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record NotificationPage(PagedList<NotificationDto> Page, int Unread);

public interface INotificationService
{
    /// <summary>Adiciona a notificação ao DbContext do comando, para gravar junto com ele. Sem link válido, é recusada.</summary>
    Result Notify(Guid userId, string type, string title, string link);
}

/// <summary>Notificações (FND-07 CA 22 e 23). Todo alerta leva à ação (R-PRP-5) e cada usuário só vê as suas.</summary>
public sealed class NotificationService(BemAquiDbContext db, ICurrentUser currentUser, TimeProvider clock) : INotificationService
{
    private const char Backslash = (char)92;

    public Result Notify(Guid userId, string type, string title, string link)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation("notification.invalid", "Notificação sem destinatário, tipo ou título.");
        }

        if (!IsAppLink(link))
        {
            return Error.Validation("notification.link_required", "Toda notificação precisa de um link para a tela onde a ação acontece.");
        }

        db.Notifications.Add(new Notification
        {
            UserId = userId,
            Type = type,
            Title = title.Length > 200 ? title[..200] : title,
            Link = link,
            CreatedAt = clock.GetUtcNow(),
        });
        return Result.Success();
    }

    public async Task<Result<NotificationPage>> ListMineAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Entre no sistema para ver suas notificações.");
        }

        var mine = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        var unread = await mine.CountAsync(n => n.ReadAt == null, cancellationToken);
        var items = await mine
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Link, n.CreatedAt, n.ReadAt))
            .ToPagedListAsync(page, cancellationToken);
        return new NotificationPage(items, unread);
    }

    public async Task<Result> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Entre no sistema para ver suas notificações.");
        }

        // Notificação de outro usuário responde como inexistente: não revela que o id existe.
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken);
        if (notification is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Notificação não encontrada.");
        }

        notification.ReadAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // Só rota relativa do próprio app: evita link vazio e open redirect ("//site", "https://...").
    private static bool IsAppLink(string? link) =>
        !string.IsNullOrWhiteSpace(link) && link[0] == '/' && !link.StartsWith("//", StringComparison.Ordinal) && !link.Contains(Backslash, StringComparison.Ordinal);
}
