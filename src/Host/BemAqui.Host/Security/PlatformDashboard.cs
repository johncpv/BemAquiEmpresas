using BemAqui.Platform.Endpoints;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;

namespace BemAqui.Host.Security;

/// <summary>
/// Painel do Hangfire só para o super admin logado no host da plataforma (FND-13 CA 6). Sem sessão, leva ao login
/// da plataforma; nos hosts de tenant, o roteamento por host já respondeu 404 (CA 5).
/// </summary>
public static class PlatformDashboard
{
    public const string Path = "/hangfire";
    public const string LoginPath = "/plataforma/entrar";

    public static WebApplication UsePlatformDashboard(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseWhen(c => c.Request.Path.StartsWithSegments(Path, StringComparison.OrdinalIgnoreCase), branch => branch.Use(async (context, next) =>
        {
            var result = await context.AuthenticateAsync(PlatformSessionAuthenticationHandler.SchemeName);
            if (!result.Succeeded)
            {
                context.Response.Redirect(LoginPath + "?volta=" + Uri.EscapeDataString(Path));
                return;
            }

            context.User = result.Principal;
            await next(context);
        }));
        app.UseHangfireDashboard(Path, new DashboardOptions
        {
            Authorization = [new PlatformDashboardAuthorization()],
            DashboardTitle = "BemAqui Plataforma: jobs",
            AppPath = "/",
        });
        return app;
    }

    private sealed class PlatformDashboardAuthorization : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context) =>
            context.GetHttpContext().User.Identity is { IsAuthenticated: true, AuthenticationType: PlatformSessionAuthenticationHandler.SchemeName };
    }
}
