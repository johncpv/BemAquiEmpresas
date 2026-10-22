using System.Text.Json;
using BemAqui.Engineering.Application;
using BemAqui.Engineering.Contracts;
using BemAqui.Engineering.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Engineering.Infrastructure;

internal static class EngineeringSchema
{
    public const string Name = "engineering";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

internal sealed class RoutingTemplateConfiguration : IEntityTypeConfiguration<RoutingTemplate>
{
    public void Configure(EntityTypeBuilder<RoutingTemplate> b)
    {
        b.ToTable("routing_templates", EngineeringSchema.Name);
        b.Property(e => e.Name).HasMaxLength(RoutingRules.MaxName);
        b.Property(e => e.ProductType).HasMaxLength(RoutingRules.MaxProductType);
        b.Property(e => e.SeedKey).HasMaxLength(60);
        // O seed não recria o exemplo excluído (ADR 0016): uma seed_key por tenant, inclusive entre os excluídos.
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ux_routing_templates_seed_key");
    }
}

internal sealed class RoutingTemplateStepConfiguration : IEntityTypeConfiguration<RoutingTemplateStep>
{
    public void Configure(EntityTypeBuilder<RoutingTemplateStep> b)
    {
        b.ToTable("routing_template_steps", EngineeringSchema.Name);
        b.Property(e => e.Code).HasMaxLength(1).IsFixedLength();
        b.Property(e => e.Name).HasMaxLength(RoutingRules.MaxStepName);
        b.Property(e => e.StandardHours).HasPrecision(18, 4);
        // Checklist ordenado em jsonb: [{ "sequence": 1, "text": "..." }] (CA 4).
        b.Property(e => e.Checklist).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, EngineeringSchema.Json),
            s => JsonSerializer.Deserialize<List<RoutingChecklistItem>>(s, EngineeringSchema.Json) ?? new List<RoutingChecklistItem>(),
            new ValueComparer<List<RoutingChecklistItem>>((x, y) => x!.SequenceEqual(y!), v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i.GetHashCode())), v => v.ToList()));
        b.HasOne<RoutingTemplate>().WithMany().HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.TemplateId, e.Sequence });
        // A letra é única no roteiro (D10) por uma constraint DEFERRABLE criada na migration (trocar duas letras de lugar
        // num salvar só não pode falhar no meio); o serviço também valida.
        b.HasIndex(e => e.WorkCenterId);
    }
}

internal sealed class RoutingTemplateItemConfiguration : IEntityTypeConfiguration<RoutingTemplateItem>
{
    public void Configure(EntityTypeBuilder<RoutingTemplateItem> b)
    {
        b.ToTable("routing_template_items", EngineeringSchema.Name);
        b.HasOne<RoutingTemplate>().WithMany().HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Restrict);
        // Um produto tem um roteiro só (CA 7).
        b.HasIndex(e => e.ItemId).IsUnique().HasDatabaseName(RoutingTemplateService.ItemIndex);
        b.HasIndex(e => e.TemplateId);
    }
}