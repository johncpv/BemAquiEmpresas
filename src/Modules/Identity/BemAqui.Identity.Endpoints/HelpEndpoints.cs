using BemAqui.BuildingBlocks.Help;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application;
using BemAqui.Identity.Application.Ai;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

/// <summary>Ajuda contextual (FND-14): índice permitido, ajuda por chave e o registro de "já vista" no perfil.</summary>
internal static class HelpEndpoints
{
    public static IEndpointRouteBuilder MapHelpEndpoints(this IEndpointRouteBuilder api)
    {
        var help = api.MapGroup("/ajuda").WithTags("Ajuda");

        help.MapGet(string.Empty, async (ICurrentUser user, HelpAccess access, CancellationToken ct) => Results.Ok(await access.IndexAsync(user.UserId!.Value, ct)))
            .AllowPinSession().WithName("ListHelp").Produces<IReadOnlyList<HelpDocument>>();

        help.MapGet("/vistas", async (ICurrentUser user, HelpAccess access, CancellationToken ct) => Results.Ok(await access.SeenAsync(user.UserId!.Value, ct)))
            .AllowPinSession().WithName("ListSeenHelp").Produces<IReadOnlyList<string>>();

        // Pergunte ao BemAqui (FND-15): todo usuário logado; o rate limit protege a conta do provedor.
        help.MapPost("/perguntar", async (AskHelpRequest request, ICurrentUser user, HelpAnswerService answers, CancellationToken ct) =>
                (await answers.AskAsync(user.UserId!.Value, request, ct)).ToHttpResult())
            .AllowPinSession().WithName("AskHelp").Produces<HelpAnswer>();

        help.MapGet("/{chave}", async (string chave, ICurrentUser user, HelpAccess access, CancellationToken ct) => (await access.GetAsync(user.UserId!.Value, chave, ct)).ToHttpResult())
            .AllowPinSession().WithName("GetHelp").Produces<HelpDocument>();

        help.MapPost("/{chave}/vista", async (string chave, ICurrentUser user, HelpAccess access, CancellationToken ct) =>
            {
                await access.MarkSeenAsync(user.UserId!.Value, chave, ct);
                return Results.NoContent();
            })
            .AllowPinSession().WithName("MarkHelpSeen");

        // Configurações → Inteligência artificial (FND-15 CA 9 a 13): a chave entra e nunca sai.
        var ai = api.MapGroup("/settings/ia").WithTags("Configurações").RequireModule("settings");
        ai.MapGet(string.Empty, async (AiSettingsService settings, CancellationToken ct) => Results.Ok(await settings.GetAsync(ct)))
            .RequirePermission("settings.view").WithName("GetAiSettings").Produces<AiSettingsDto>();
        ai.MapPut(string.Empty, async (AiSettingsRequest request, ICurrentUser user, AiSettingsService settings, CancellationToken ct) =>
                (await settings.SaveAsync(request, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("SaveAiSettings").Produces<AiSettingsDto>();
        ai.MapDelete(string.Empty, async (AiSettingsService settings, CancellationToken ct) =>
            {
                await settings.RemoveAsync(ct);
                return Results.NoContent();
            })
            .RequirePermission("settings.edit").WithName("RemoveAiSettings");
        ai.MapPost("/testar", async (AiSettingsRequest request, ICurrentUser user, AiSettingsService settings, CancellationToken ct) =>
                (await settings.TestAsync(request, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("TestAiSettings").Produces<AiTestResult>();

        return api;
    }
}
