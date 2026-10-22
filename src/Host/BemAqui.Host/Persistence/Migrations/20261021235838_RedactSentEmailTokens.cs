using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BemAqui.Host.Persistence.Migrations
{
    /// <summary>
    /// D19: apaga o link com token de convite e de redefinição de senha dos e-mails já resolvidos (Sent, Failed, Skipped),
    /// gravados antes de o envio passar a limpar o modelo. Apagar o token é a intenção: ele não serve mais a ninguém e
    /// quem precisar de link novo gera outro token. E-mail ainda na fila (Queued) fica como está, porque o job precisa dele.
    /// </summary>
    public partial class RedactSentEmailTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE shared.email_logs
                SET model = jsonb_set(model, '{link}', to_jsonb('[removido após o envio]'::text))
                WHERE template IN ('invite', 'password-reset')
                  AND status IN ('Sent', 'Failed', 'Skipped')
                  AND model ->> 'link' LIKE '%token=%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem volta: o token apagado não é recuperável (nem deve ser).
        }
    }
}
