using System.Text.Json;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

/// <summary>Régua de cobrança (FIN-09): os passos em jsonb <c>[{ "days": -3, "text": "..." }]</c>, uma régua por tenant.</summary>
internal sealed class CollectionRuleConfiguration : IEntityTypeConfiguration<CollectionRule>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<CollectionRule> b)
    {
        b.ToTable("collection_rules", FinanceSchema.Name);
        b.Property(e => e.Steps).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Json),
            s => JsonSerializer.Deserialize<List<CollectionStep>>(s, Json) ?? new List<CollectionStep>(),
            new ValueComparer<List<CollectionStep>>((x, y) => x!.SequenceEqual(y!), v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i.GetHashCode())), v => v.ToList()));
        b.Property(e => e.ReplyTo).HasMaxLength(254);
    }
}

/// <summary>Lembretes: um por título e passo (índice único, CA 4); pela fila do e-mail, pelo cliente no mês e pelos adiados.</summary>
internal sealed class CollectionNoticeConfiguration : IEntityTypeConfiguration<CollectionNotice>
{
    public const string UniqueIndex = "ux_collection_notices_receivable_step";

    public void Configure(EntityTypeBuilder<CollectionNotice> b)
    {
        b.ToTable("collection_notices", FinanceSchema.Name);
        b.HasOne<Receivable>().WithMany().HasForeignKey(e => e.ReceivableId).OnDelete(DeleteBehavior.Restrict);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(12);
        b.Property(e => e.SkipReason).HasMaxLength(200);
        b.HasIndex(e => new { e.ReceivableId, e.Step }).IsUnique().HasDatabaseName(UniqueIndex);
        b.HasIndex(e => e.EmailLogId);
        b.HasIndex(e => new { e.PartyId, e.CreatedAt });
        b.HasIndex(e => e.Status).HasFilter("status = 'Deferred'").HasDatabaseName("ix_collection_notices_deferred");
    }
}

/// <summary>Pausas da cobrança, do cliente ou do título; só a ativa interessa ao job (sem retomada).</summary>
internal sealed class CollectionPauseConfiguration : IEntityTypeConfiguration<CollectionPause>
{
    public void Configure(EntityTypeBuilder<CollectionPause> b)
    {
        b.ToTable("collection_pauses", FinanceSchema.Name, t => t.HasCheckConstraint("ck_collection_pauses_target", "(party_id IS NULL) <> (receivable_id IS NULL)"));
        b.HasOne<Receivable>().WithMany().HasForeignKey(e => e.ReceivableId).OnDelete(DeleteBehavior.Restrict);
        b.Property(e => e.Reason).HasMaxLength(200);
        b.HasIndex(e => e.PartyId).HasFilter("resumed_at IS NULL");
        b.HasIndex(e => e.ReceivableId).HasFilter("resumed_at IS NULL");
    }
}
