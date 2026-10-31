using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.BuildingBlocks.Privacy;

/// <summary>
/// E-mails enviados ao titular (<c>shared.email_logs</c>, LGPD-01a CA 6 e 9): destinatário, assunto, data e situação. Os outros endereços
/// da mesma mensagem (cópias de terceiros) saem mascarados; o modelo do template não sai (pode ter dado de outra pessoa).
/// </summary>
[PersonalDataCovers(typeof(EmailLog))]
public sealed class EmailLogPersonalDataProvider(BemAquiDbContext db) : IPersonalDataProvider
{
    public string Module => "shared";

    public string Title => "E-mails enviados";

    public async Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var emails = context.Subject.Emails;
        if (emails.Count == 0)
        {
            return null;
        }

        var found = new Dictionary<Guid, EmailLog>();
        foreach (var email in emails)
        {
            var exact = PersonalDataQuery.Exact(email);
            var pattern = PersonalDataQuery.Contains(email);
            foreach (var e in await db.EmailLogs.AsNoTracking().Where(l => EF.Functions.ILike(l.To, exact) || (l.Cc != null && EF.Functions.ILike(l.Cc, pattern))).ToListAsync(cancellationToken))
            {
                found[e.Id] = e;
            }
        }

        var records = found.Values.OrderBy(e => e.CreatedAt).Select(e => PersonalDataRecord.Of(e.CreatedAt,
            ("Destinatário", PersonalDataMask.Recipients(e.To, emails)),
            ("Cópia", PersonalDataMask.Recipients(e.Cc, emails)),
            ("Assunto", e.Subject),
            ("Situação", e.Status switch { EmailStatus.Sent => "Enviado", EmailStatus.Failed => "Falhou", EmailStatus.Skipped => "Não enviado", _ => "Na fila" }),
            ("Enviado em", PersonalDataMask.At(e.SentAt)))).ToList();
        return new PersonalDataSection(Module, Title, [new PersonalDataBlock("E-mails enviados", "shared.email_logs", records)]);
    }
}

/// <summary>Notificações do sino do usuário titular (<c>shared.notifications</c>, CA 9): título, data e leitura.</summary>
[PersonalDataCovers(typeof(Notification))]
public sealed class NotificationPersonalDataProvider(BemAquiDbContext db) : IPersonalDataProvider
{
    public string Module => "shared.notifications";

    public string Title => "Notificações";

    public async Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Subject.UserId is not { } userId)
        {
            return null;
        }

        var records = (await db.Notifications.AsNoTracking().Where(n => n.UserId == userId).OrderBy(n => n.CreatedAt).ToListAsync(cancellationToken))
            .Select(n => PersonalDataRecord.Of(n.CreatedAt, ("Título", n.Title), ("Tipo", n.Type), ("Lida em", PersonalDataMask.At(n.ReadAt)))).ToList();
        return new PersonalDataSection(Module, Title, [new PersonalDataBlock("Notificações", "shared.notifications", records)]);
    }
}

public static class PrivacyServiceCollectionExtensions
{
    /// <summary>Provedores da base (LGPD-01a CA 8): e-mails e notificações. Os anexos saem pelo provedor do módulo dono.</summary>
    public static IServiceCollection AddBemAquiPrivacy(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPersonalDataProvider, EmailLogPersonalDataProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPersonalDataProvider, NotificationPersonalDataProvider>());
        return services;
    }
}
