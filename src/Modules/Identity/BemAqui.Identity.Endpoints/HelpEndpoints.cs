using BemAqui.BuildingBlocks.Help;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application;
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
            .WithName("ListHelp").Produces<IReadOnlyList<HelpDocument>>();

        help.MapGet("/vistas", async (ICurrentUser user, HelpAccess access, CancellationToken ct) => Results.Ok(await access.SeenAsync(user.UserId!.Value, ct)))
            .WithName("ListSeenHelp").Produces<IReadOnlyList<string>>();

        help.MapGet("/{chave}", async (string chave, ICurrentUser user, HelpAccess access, CancellationToken ct) => (await access.GetAsync(user.UserId!.Value, chave, ct)).ToHttpResult())
            .WithName("GetHelp").Produces<HelpDocument>();

        help.MapPost("/{chave}/vista", async (string chave, ICurrentUser user, HelpAccess access, CancellationToken ct) =>
            {
                await access.MarkSeenAsync(user.UserId!.Value, chave, ct);
                return Results.NoContent();
            })
            .WithName("MarkHelpSeen");

        return api;
    }
}
