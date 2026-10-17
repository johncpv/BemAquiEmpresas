namespace BemAqui.Host.Security;

/// <summary>
/// Cabeçalhos de segurança em toda resposta (FND-02 CA 11). Uma resposta que já define a própria CSP
/// (ex.: download de anexo em sandbox) mantém a dela.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self'; " +
        "connect-src 'self' wss:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            if (!environment.IsDevelopment())
            {
                headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
            }

            if (string.IsNullOrEmpty(headers.ContentSecurityPolicy))
            {
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
            }

            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=(self), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            return Task.CompletedTask;
        });
        return next(context);
    }
}
