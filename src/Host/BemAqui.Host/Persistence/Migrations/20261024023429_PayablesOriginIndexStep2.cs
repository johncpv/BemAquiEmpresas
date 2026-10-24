using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <summary>
    /// Segundo passo do índice dos títulos por origem (ADR 0024 §2, CMP-03a CA 17): o índice único parcial
    /// <c>(origin, origin_id, installment) WHERE status &lt;&gt; 'Cancelled'</c> já existe (migration <c>PurchaseOrders</c>), então o antigo
    /// <c>(origin, origin_id)</c> sai. Só um índice é removido: nenhum dado, coluna ou tabela muda.
    /// </summary>
    public partial class PayablesOriginIndexStep2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0024 §2: o índice novo nasceu antes deste (dois passos); remover o antigo libera várias parcelas por origem.
            migrationBuilder.DropIndex(
                name: "ix_payables_origin",
                schema: "finance",
                table: "payables");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_payables_origin",
                schema: "finance",
                table: "payables",
                columns: new[] { "origin", "origin_id" },
                unique: true,
                filter: "origin_id IS NOT NULL");
        }
    }
}
