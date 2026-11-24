using BemAqui.Sales.Application;
using BemAqui.Sales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Sales.Infrastructure;

/// <summary>
/// <c>sales.sales</c> (VND-01): dinheiro em <c>numeric(18,2)</c>, data de negócio em <c>date</c>; número único por canal; o <c>CHECK</c> da
/// validação por canal (CA 6) também no banco: venda do PDV tem caixa e sessão; online e OS, nenhum dos dois.
/// </summary>
internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.ToTable("sales", SalesSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_sales_pos_register", "channel <> 'Pos' OR (register_id IS NOT NULL AND cash_session_id IS NOT NULL)");
            t.HasCheckConstraint("ck_sales_no_register_channels", "channel NOT IN ('Online', 'ServiceOrder') OR (register_id IS NULL AND cash_session_id IS NULL)");
            t.HasCheckConstraint("ck_sales_totals", "subtotal >= 0 AND discount_total >= 0 AND service_fee >= 0 AND total >= 0");
        });
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Number).HasMaxLength(40);
        b.Property(e => e.Channel).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.StockStatus).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.FiscalStatus).HasMaxLength(32);
        b.Property(e => e.SourceType).HasMaxLength(40);
        b.Property(e => e.CancelReason).HasMaxLength(200);
        foreach (var money in new[] { nameof(Sale.Subtotal), nameof(Sale.DiscountTotal), nameof(Sale.ServiceFee), nameof(Sale.Total), nameof(Sale.PaymentDifference) })
        {
            b.Property(money).HasPrecision(18, 2);
        }

        b.HasIndex(e => new { e.Channel, e.Number }).IsUnique().HasDatabaseName(SaleService.NumberIndex);
        // Recebível por dia e canal (CA 11) e receita por dia (CA 12).
        b.HasIndex(e => new { e.BusinessDate, e.Channel }).HasFilter("status = 'Completed'").HasDatabaseName("ix_sales_completed_day_channel");
        b.HasIndex(e => e.CashSessionId).HasFilter("cash_session_id IS NOT NULL");
        b.HasIndex(e => new { e.SourceType, e.SourceId });
    }
}

/// <summary><c>sales.sale_lines</c>: quantidade <c>numeric(18,4)</c>, preço e desconto <c>numeric(18,2)</c>, custo da baixa <c>numeric(18,6)</c>.</summary>
internal sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> b)
    {
        b.ToTable("sale_lines", SalesSchema.Name, t => t.HasCheckConstraint("ck_sale_lines_quantity", "quantity > 0"));
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Description).HasMaxLength(200);
        b.Property(e => e.UnitCode).HasMaxLength(10);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.UnitPrice).HasPrecision(18, 2);
        b.Property(e => e.Discount).HasPrecision(18, 2);
        b.Property(e => e.Total).HasPrecision(18, 2);
        b.Property(e => e.UnitCost).HasPrecision(18, 6);
        b.Property(e => e.Note).HasMaxLength(200);
        b.Property(e => e.Seat).HasMaxLength(20);
        // PDV-04: preço da tabela quando o praticado mudou, e os motivos do desconto e da troca de preço.
        b.Property(e => e.ListUnitPrice).HasPrecision(18, 2);
        b.Property(e => e.DiscountReason).HasMaxLength(SaleLine.MaxReasonLength);
        b.Property(e => e.PriceReason).HasMaxLength(SaleLine.MaxReasonLength);
        b.Ignore(e => e.Gross);
        b.HasOne<Sale>().WithMany().HasForeignKey(e => e.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.SaleId, e.LineNumber }).IsUnique();
        b.HasIndex(e => e.ProductId);
        b.HasIndex(e => e.ParentProductId); // uso do produto pai da grade (IProductUsageProvider)
    }
}

internal sealed class SalePartConfiguration : IEntityTypeConfiguration<SalePart>
{
    public void Configure(EntityTypeBuilder<SalePart> b)
    {
        b.ToTable("sale_parts", SalesSchema.Name);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Label).HasMaxLength(60);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.HasOne<Sale>().WithMany().HasForeignKey(e => e.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.SaleId);
    }
}

/// <summary>
/// <c>sales.sale_payments</c> (tenders): sem coluna de número de cartão, validade, código de segurança ou trilha (CA 8, teste de arquitetura);
/// troco só menor que o valor entregue.
/// </summary>
internal sealed class SalePaymentConfiguration : IEntityTypeConfiguration<SalePayment>
{
    public void Configure(EntityTypeBuilder<SalePayment> b)
    {
        b.ToTable("sale_payments", SalesSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_sale_payments_amount", "amount > 0 AND change_amount >= 0 AND change_amount < amount");
            t.HasCheckConstraint("ck_sale_payments_installments", "installments BETWEEN 1 AND 24");
        });
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.ChangeAmount).HasPrecision(18, 2);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Integration).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Provider).HasMaxLength(60);
        b.Property(e => e.CardBrand).HasMaxLength(30);
        b.Property(e => e.Nsu).HasMaxLength(60);
        b.Property(e => e.AuthorizationCode).HasMaxLength(60);
        b.Property(e => e.Txid).HasMaxLength(60);
        b.Property(e => e.EndToEndId).HasMaxLength(60);
        b.Ignore(e => e.NetAmount);
        b.HasOne<Sale>().WithMany().HasForeignKey(e => e.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalePart>().WithMany().HasForeignKey(e => e.SalePartId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.SaleId);
        b.HasIndex(e => e.PaymentMethodId);
    }
}

/// <summary><c>sales.stock_postings</c>: uma baixa por venda e item no canal sem caixa, com o movimento do estoque e o custo.</summary>
internal sealed class SaleStockPostingConfiguration : IEntityTypeConfiguration<SaleStockPosting>
{
    public void Configure(EntityTypeBuilder<SaleStockPosting> b)
    {
        b.ToTable("stock_postings", SalesSchema.Name);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.ShortfallQuantity).HasPrecision(18, 4);
        b.Property(e => e.UnitCost).HasPrecision(18, 6);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Note).HasMaxLength(200);
        b.HasOne<Sale>().WithMany().HasForeignKey(e => e.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.SaleId, e.ItemId }).IsUnique().HasDatabaseName("ux_stock_postings_sale_item");
        b.HasIndex(e => e.StockMovementId);
    }
}

/// <summary><c>sales.sale_customer_documents</c> (OFF-02 CA 17): uma linha por venda (o id da venda do aparelho); busca pelo índice cego.</summary>
internal sealed class SaleCustomerDocumentConfiguration : IEntityTypeConfiguration<SaleCustomerDocument>
{
    public void Configure(EntityTypeBuilder<SaleCustomerDocument> b)
    {
        b.ToTable("sale_customer_documents", SalesSchema.Name, t => t.HasCheckConstraint("ck_sale_customer_documents_kind", "kind IN ('cpf', 'cnpj')"));
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Kind).HasMaxLength(4);
        b.Property(e => e.BlindIndex).HasMaxLength(64);
        b.HasIndex(e => e.SaleId).IsUnique();
        // Sem chave estrangeira: o documento chega no mesmo lote da venda e fica guardado mesmo com a venda em quarentena (OFF-02).
        b.HasIndex(e => e.BlindIndex);
    }
}

/// <summary><c>sales.blind_index_keys</c>: a chave do índice cego do tenant, cifrada.</summary>
internal sealed class BlindIndexKeyConfiguration : IEntityTypeConfiguration<BlindIndexKey>
{
    public void Configure(EntityTypeBuilder<BlindIndexKey> b)
    {
        b.ToTable("blind_index_keys", SalesSchema.Name);
        b.Property(e => e.Id).ValueGeneratedNever();
    }
}
