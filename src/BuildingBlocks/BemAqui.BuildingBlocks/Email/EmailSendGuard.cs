namespace BemAqui.BuildingBlocks.Email;

/// <summary>
/// Guarda de envio por template (FIN-09 CA 5): o <see cref="SendEmailJob"/> chama, logo antes de enviar, as guardas registradas para o
/// template do e-mail. Uma guarda que devolve um motivo marca o e-mail <see cref="EmailStatus.Skipped"/> com ele e nada sai. Templates sem
/// guarda não mudam. Registre com <c>TryAddEnumerable(ServiceDescriptor.Scoped&lt;IEmailSendGuard, ...&gt;())</c>.
/// </summary>
public interface IEmailSendGuard
{
    /// <summary>Template que esta guarda confere (ex.: <c>collection-reminder</c>).</summary>
    string Template { get; }

    /// <summary>Null: pode enviar. Texto: o motivo (PT-BR, sem dado pessoal) de o e-mail não sair.</summary>
    Task<string?> SkipReasonAsync(EmailLog log, CancellationToken cancellationToken);
}
