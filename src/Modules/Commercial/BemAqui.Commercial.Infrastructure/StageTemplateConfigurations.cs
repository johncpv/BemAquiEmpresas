using System.Text.Json;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Commercial.Infrastructure;

internal static class StageTemplateJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// Modelo de etapas (EXE-02). Os únicos parciais de nome (<c>lower(name)</c> entre os ativos e não excluídos) e de padrão
/// (<c>coalesce(segment, '')</c> entre os padrão não excluídos) são expressões: entram por SQL na migration, fora do modelo do EF.
/// </summary>
internal sealed class StageTemplateConfiguration : IEntityTypeConfiguration<StageTemplate>
{
    public void Configure(EntityTypeBuilder<StageTemplate> b)
    {
        b.ToTable("stage_templates", CommercialSchema.Name);
        b.Property(e => e.Name).HasMaxLength(StageTemplateRules.MaxName);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(12);
        b.Property(e => e.Segment).HasMaxLength(40);
        b.Property(e => e.SeedKey).HasMaxLength(60);
        b.Property(e => e.SuggestedMilestones).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, StageTemplateJson.Options),
            s => JsonSerializer.Deserialize<List<SuggestedMilestone>>(s, StageTemplateJson.Options) ?? new List<SuggestedMilestone>(),
            new ValueComparer<List<SuggestedMilestone>>((x, y) => x!.SequenceEqual(y!), v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i.GetHashCode())), v => v.ToList()));
        // O pacote do perfil não recria o modelo excluído: uma seed_key por tenant, inclusive entre os excluídos (CA 9 e 11).
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ux_stage_templates_seed_key");
    }
}

internal sealed class StageTemplateStepConfiguration : IEntityTypeConfiguration<StageTemplateStep>
{
    public void Configure(EntityTypeBuilder<StageTemplateStep> b)
    {
        b.ToTable("stage_template_steps", CommercialSchema.Name);
        b.Property(e => e.Code).HasMaxLength(1).IsFixedLength();
        b.Property(e => e.Name).HasMaxLength(StageTemplateRules.MaxStepName);
        b.Property(e => e.Role).HasConversion<string>().HasMaxLength(12);
        b.Property(e => e.StandardHoursPerUnit).HasPrecision(18, 4);
        b.Property(e => e.Checklist).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, StageTemplateJson.Options),
            s => JsonSerializer.Deserialize<List<string>>(s, StageTemplateJson.Options) ?? new List<string>(),
            new ValueComparer<List<string>>((x, y) => x!.SequenceEqual(y!), v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i.GetHashCode(StringComparison.Ordinal))), v => v.ToList()));
        b.HasOne<StageTemplate>().WithMany().HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.TemplateId, e.Sequence });
        // A letra é única no modelo por uma constraint DEFERRABLE criada na migration (trocar duas letras de lugar num salvar só não falha no
        // meio); o serviço também valida.
        b.HasIndex(e => e.PositionId);
    }
}

internal sealed class StageTemplateItemConfiguration : IEntityTypeConfiguration<StageTemplateItem>
{
    public void Configure(EntityTypeBuilder<StageTemplateItem> b)
    {
        b.ToTable("stage_template_items", CommercialSchema.Name);
        b.HasOne<StageTemplate>().WithMany().HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.Restrict);
        // Um item de serviço tem um modelo só (CA 10).
        b.HasIndex(e => e.ItemId).IsUnique().HasDatabaseName(StageTemplateService.ItemIndex);
        b.HasIndex(e => e.TemplateId);
    }
}
