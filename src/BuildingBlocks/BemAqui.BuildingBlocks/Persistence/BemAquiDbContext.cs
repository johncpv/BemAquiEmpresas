using System.Linq.Expressions;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Persistence;

/// <summary>
/// Base do DbContext de tenant: tabelas compartilhadas (<c>shared</c>, <c>audit</c>) e filtro global de
/// exclusão lógica. O <c>TenantDbContext</c> do Host deriva daqui e aplica as configurações dos módulos (ADR 0006).
/// </summary>
public abstract class BemAquiDbContext(DbContextOptions options) : DbContext(options)
{
    public const string SharedSchema = "shared";
    public const string AuditSchema = "audit";

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    public DbSet<NumberFormatOverride> NumberFormatOverrides => Set<NumberFormatOverride>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    public DbSet<WebhookCredential> WebhookCredentials => Set<WebhookCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable("audit_log", AuditSchema);
            b.HasKey(e => e.Id);
            b.Property(e => e.Entity).HasMaxLength(128);
            b.Property(e => e.EntityId).HasMaxLength(64);
            b.Property(e => e.Action).HasMaxLength(64);
            b.Property(e => e.Changes).HasColumnType("jsonb");
            b.Property(e => e.Ip).HasMaxLength(64);
            b.Property(e => e.Origin).HasMaxLength(300);
            b.HasIndex(e => new { e.Entity, e.EntityId, e.At });
        });

        modelBuilder.Entity<NumberSequence>(b =>
        {
            b.ToTable("number_sequences", SharedSchema);
            b.HasKey(e => e.Id);
            b.Property(e => e.DocType).HasMaxLength(16);
            b.HasIndex(e => new { e.DocType, e.Year }).IsUnique();
        });

        modelBuilder.Entity<NumberFormatOverride>(b =>
        {
            b.ToTable("number_formats", SharedSchema);
            b.HasKey(e => e.Id);
            b.Property(e => e.DocType).HasMaxLength(16);
            b.Property(e => e.Prefix).HasMaxLength(6);
            b.HasIndex(e => e.DocType).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages", SharedSchema);
            b.HasKey(e => e.Id);
            b.Property(e => e.Type).HasMaxLength(256);
            b.Property(e => e.Payload).HasColumnType("jsonb");
            b.HasIndex(e => new { e.ProcessedAt, e.DeadLetteredAt, e.NextAttemptAt, e.OccurredAt });
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox_messages", SharedSchema);
            b.HasKey(e => new { e.MessageId, e.Handler });
            b.Property(e => e.Handler).HasMaxLength(256);
        });

        modelBuilder.Entity<Attachment>(b =>
        {
            b.ToTable("attachments", SharedSchema);
            b.HasKey(e => e.Id);
            b.Property(e => e.OwnerType).HasMaxLength(64);
            b.Property(e => e.FileName).HasMaxLength(200);
            b.Property(e => e.ContentType).HasMaxLength(128);
            b.Property(e => e.Sha256).HasMaxLength(64);
            b.Property(e => e.StoragePath).HasMaxLength(256);
            b.Property(e => e.ScanStatus).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(e => new { e.OwnerType, e.OwnerId });
            b.HasIndex(e => e.ScanStatus);
        });

        modelBuilder.Entity<Notification>(b =>
        {
            b.ToTable("notifications", SharedSchema);
            b.HasKey(e => e.Id);
            b.Property(e => e.Type).HasMaxLength(64);
            b.Property(e => e.Title).HasMaxLength(200);
            b.Property(e => e.Link).HasMaxLength(512);
            b.HasIndex(e => new { e.UserId, e.ReadAt, e.CreatedAt });
            b.Property(e => e.DedupKey).HasMaxLength(200);
            b.HasIndex(e => new { e.UserId, e.DedupKey }).IsUnique().HasFilter("dedup_key IS NOT NULL").HasDatabaseName(NotificationService.DedupIndex);
        });

        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("idempotency_records", SharedSchema);
            b.HasKey(e => e.Key);
            b.Property(e => e.Scope).HasMaxLength(256);
            b.Property(e => e.BodyHash).HasMaxLength(64);
            b.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<EmailLog>(b =>
        {
            b.ToTable("email_logs", SharedSchema);
            b.HasKey(e => e.Id);
            b.Property(e => e.To).HasMaxLength(320);
            b.Property(e => e.Template).HasMaxLength(64);
            b.Property(e => e.Subject).HasMaxLength(300);
            b.Property(e => e.Model).HasColumnType("jsonb");
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(e => e.Error).HasMaxLength(1000);
            b.Property(e => e.RelatedType).HasMaxLength(64);
            b.HasIndex(e => new { e.Status, e.CreatedAt });
            b.HasIndex(e => new { e.RelatedType, e.RelatedId });
            b.HasIndex(e => new { e.Template, e.CreatedAt });
        });

        // FND-16 (ADR 0031 §4): evento cru com deduplicação pelo id da entrega e pelo hash do corpo; credencial por provedor e escopo.
        modelBuilder.Entity<WebhookEvent>(b =>
        {
            b.ToTable("webhook_events", SharedSchema);
            b.HasKey(e => e.Id);
            b.Property(e => e.Provider).HasMaxLength(32);
            b.Property(e => e.ExternalId).HasMaxLength(WebhookReceiver.ExternalIdMaxLength + 8);
            b.Property(e => e.BodySha256).HasColumnType("bytea");
            b.Property(e => e.EventType).HasMaxLength(WebhookReceiver.EventTypeMaxLength);
            b.Property(e => e.Payload).HasColumnType("bytea");
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(e => e.LastError).HasMaxLength(WebhookEvent.ErrorMaxLength);
            b.HasIndex(e => new { e.Provider, e.ExternalId }).IsUnique().HasDatabaseName(WebhookReceiver.UniqueExternalId);
            b.HasIndex(e => new { e.Provider, e.BodySha256 }).IsUnique().HasDatabaseName(WebhookReceiver.UniqueBodyHash);
            b.HasIndex(e => e.Status).HasFilter("status IN ('Received','Failed')").HasDatabaseName("ix_webhook_events_pending");
            b.HasIndex(e => e.ReceivedAt);
        });

        modelBuilder.Entity<WebhookCredential>(b =>
        {
            b.ToTable("webhook_tokens", SharedSchema);
            b.HasKey(e => e.Id);
            b.Property(e => e.Provider).HasMaxLength(32);
            b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
            b.Property(e => e.TokenHash).HasMaxLength(64);
            b.Property(e => e.PreviousTokenHash).HasMaxLength(64);
            b.Property(e => e.SecretCipher).HasMaxLength(1024);
            b.Property(e => e.PreviousSecretCipher).HasMaxLength(1024);
            b.HasIndex(e => new { e.Provider, e.ScopeId }).IsUnique();
        });

        ConfigureModules(modelBuilder);
        ApplySoftDeleteFilter(modelBuilder);
        ApplyConcurrencyTokens(modelBuilder);
    }

    /// <summary>Configurações dos módulos (assemblies <c>*.Infrastructure</c>).</summary>
    protected virtual void ConfigureModules(ModelBuilder modelBuilder)
    {
    }

    /// <summary>Documento <see cref="IConcurrencyVersioned"/>: <c>Version</c> é o <c>xmin</c> do PostgreSQL (ADR 0019).</summary>
    private static void ApplyConcurrencyTokens(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(IConcurrencyVersioned).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType).Property(nameof(IConcurrencyVersioned.Version)).IsRowVersion();
        }
    }

    private static void ApplySoftDeleteFilter(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(ISoftDeletable).IsAssignableFrom(t.ClrType)))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var filter = Expression.Lambda(Expression.Not(Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted))), parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
