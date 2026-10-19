using BemAqui.Parties.Application;
using BemAqui.Parties.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Parties.Infrastructure;

internal static class PartiesSchema
{
    public const string Name = "parties";
}

internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> b)
    {
        b.ToTable("parties", PartiesSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(2);
        b.Property(e => e.TaxRegime).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Document).HasMaxLength(14);
        b.Property(e => e.LegalName).HasMaxLength(200);
        b.Property(e => e.TradeName).HasMaxLength(200);
        b.Property(e => e.SearchText).HasMaxLength(420);
        b.Property(e => e.StateRegistration).HasMaxLength(20);
        b.Property(e => e.MunicipalRegistration).HasMaxLength(20);
        b.Property(e => e.Email).HasMaxLength(254);
        b.Property(e => e.Phone).HasMaxLength(11);
        b.Property(e => e.Notes).HasMaxLength(2000);
        b.Property(e => e.ImportSource).HasMaxLength(100);

        // Um documento por cadastro não excluído (CA 4 e 5); sem documento é permitido (tabela 1.1).
        // Índices com nome: o EF fundiria dois índices sem nome na mesma coluna (e GIN não pode ser único).
        b.HasIndex(e => e.Document, PartyService.DocumentIndex).HasDatabaseName(PartyService.DocumentIndex).IsUnique().HasFilter("NOT is_deleted AND document IS NOT NULL");
        // Busca por parte do nome e do CNPJ com 50 mil cadastros em menos de 1 s (CA 18).
        b.HasIndex(e => e.SearchText, "ix_parties_search_text_trgm").HasDatabaseName("ix_parties_search_text_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
        b.HasIndex(e => e.Document, "ix_parties_document_trgm").HasDatabaseName("ix_parties_document_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");

        b.HasMany(e => e.Roles).WithOne().HasForeignKey(r => r.PartyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(e => e.Addresses).WithOne().HasForeignKey(a => a.PartyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(e => e.Contacts).WithOne().HasForeignKey(c => c.PartyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PartyRoleConfiguration : IEntityTypeConfiguration<PartyRole>
{
    public void Configure(EntityTypeBuilder<PartyRole> b)
    {
        b.ToTable("party_roles", PartiesSchema.Name);
        b.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(e => new { e.PartyId, e.Role }).IsUnique();
        b.HasIndex(e => e.Role);
    }
}

internal sealed class PartyAddressConfiguration : IEntityTypeConfiguration<PartyAddress>
{
    public void Configure(EntityTypeBuilder<PartyAddress> b)
    {
        b.ToTable("party_addresses", PartiesSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.ZipCode).HasMaxLength(8);
        b.Property(e => e.Street).HasMaxLength(200);
        b.Property(e => e.Number).HasMaxLength(20);
        b.Property(e => e.Complement).HasMaxLength(100);
        b.Property(e => e.District).HasMaxLength(100);
        b.Property(e => e.City).HasMaxLength(100);
        b.Property(e => e.IbgeCode).HasMaxLength(7);
        b.Property(e => e.State).HasMaxLength(2);
        // Um padrão por terceiro e tipo (CA 12).
        b.HasIndex(e => new { e.PartyId, e.Kind }).IsUnique().HasFilter("is_default").HasDatabaseName("ix_party_addresses_default");
    }
}

internal sealed class PartyContactConfiguration : IEntityTypeConfiguration<PartyContact>
{
    public void Configure(EntityTypeBuilder<PartyContact> b)
    {
        b.ToTable("party_contacts", PartiesSchema.Name);
        b.Property(e => e.Name).HasMaxLength(150);
        b.Property(e => e.Role).HasMaxLength(100);
        b.Property(e => e.Email).HasMaxLength(254);
        b.Property(e => e.Phone).HasMaxLength(11);
        b.HasIndex(e => e.PartyId);
    }
}

internal sealed class CnpjLookupCacheConfiguration : IEntityTypeConfiguration<CnpjLookupCache>
{
    public void Configure(EntityTypeBuilder<CnpjLookupCache> b)
    {
        b.ToTable("cnpj_lookups", PartiesSchema.Name);
        b.Property(e => e.Document).HasMaxLength(14);
        b.Property(e => e.Payload).HasColumnType("jsonb");
        b.HasIndex(e => e.Document).IsUnique().HasDatabaseName("ix_cnpj_lookups_document");
        b.HasIndex(e => e.LookedUpAt);
    }
}
