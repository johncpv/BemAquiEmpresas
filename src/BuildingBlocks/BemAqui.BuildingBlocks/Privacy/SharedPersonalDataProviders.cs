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

    public Task<PersonalDataAnonymizationPlan?> PlanAnonymizationAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, false, cancellationToken);

    public Task<PersonalDataAnonymizationPlan?> AnonymizeAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, true, cancellationToken);

    /// <summary>
    /// LGPD-01b CA 9: nos e-mails ao titular, o destinatário, a cópia e a resposta trocam o endereço dele pelo marcador, e o modelo
    /// (<c>model</c>) fica vazio (pode ter o nome, o documento ou o link dele). O envio continua no histórico, com data, tipo e situação.
    /// </summary>
    private async Task<PersonalDataAnonymizationPlan?> RunAsync(PersonalDataAnonymizationContext context, bool apply, CancellationToken ct)
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
            var query = db.EmailLogs.Where(l => EF.Functions.ILike(l.To, exact) || (l.Cc != null && EF.Functions.ILike(l.Cc, pattern)) || (l.ReplyTo != null && EF.Functions.ILike(l.ReplyTo, exact)));
            foreach (var e in await (apply ? query : query.AsNoTracking()).ToListAsync(ct))
            {
                found[e.Id] = e;
            }
        }

        var decision = context.Decide(context.ContactGroup);
        if (apply && !decision.Retained)
        {
            foreach (var log in found.Values)
            {
                var entry = db.Entry(log);
                entry.Property(l => l.To).CurrentValue = PersonalDataMarker.Recipients(log.To, emails, context.Marker)!;
                entry.Property(l => l.Cc).CurrentValue = PersonalDataMarker.HasAny(log.Cc, emails) ? PersonalDataMarker.Recipients(log.Cc, emails, context.Marker) : log.Cc;
                entry.Property(l => l.ReplyTo).CurrentValue = PersonalDataMarker.HasAny(log.ReplyTo, emails) ? null : log.ReplyTo;
                log.Model = "{}";
                if (context.FullName is { } name && log.Subject.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    log.Subject = log.Subject.Replace(name, context.Marker, StringComparison.OrdinalIgnoreCase);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        return new PersonalDataPlanBuilder(Module, Title).Fields(decision, "Destinatário e modelo dos e-mails enviados", "shared.email_logs", found.Count).Build();
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

    public Task<PersonalDataAnonymizationPlan?> PlanAnonymizationAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, false, cancellationToken);

    public Task<PersonalDataAnonymizationPlan?> AnonymizeAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, true, cancellationToken);

    /// <summary>
    /// LGPD-01b CA 9: as notificações do usuário titular têm o texto trocado pelo marcador; nas dos outros usuários, o nome completo do titular
    /// (quando aparece) vira o marcador. O link e a data ficam.
    /// </summary>
    private async Task<PersonalDataAnonymizationPlan?> RunAsync(PersonalDataAnonymizationContext context, bool apply, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var own = context.Subject.UserId is { } userId
            ? await (apply ? db.Notifications : db.Notifications.AsNoTracking()).Where(n => n.UserId == userId).ToListAsync(ct)
            : [];
        var mentions = new List<Notification>();
        if (context.FullName is { } name)
        {
            var pattern = PersonalDataQuery.Contains(name);
            var query = db.Notifications.Where(n => EF.Functions.ILike(n.Title, pattern) && (context.Subject.UserId == null || n.UserId != context.Subject.UserId));
            mentions = await (apply ? query : query.AsNoTracking()).ToListAsync(ct);
        }

        if (own.Count == 0 && mentions.Count == 0)
        {
            return null;
        }

        var decision = context.Decide(context.ContactGroup);
        if (apply && !decision.Retained)
        {
            foreach (var n in own.Where(n => n.Title != context.Marker))
            {
                db.Entry(n).Property(x => x.Title).CurrentValue = context.Marker;
            }

            foreach (var n in mentions)
            {
                db.Entry(n).Property(x => x.Title).CurrentValue = n.Title.Replace(context.FullName!, context.Marker, StringComparison.OrdinalIgnoreCase);
            }

            await db.SaveChangesAsync(ct);
        }

        return new PersonalDataPlanBuilder(Module, Title)
            .Fields(decision, "Texto das notificações do usuário", "shared.notifications", own.Count)
            .Fields(decision, "Nome do titular em notificações de outros usuários", "shared.notifications", mentions.Count)
            .Build();
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
