using BemAqui.BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace BemAqui.BuildingBlocks.Attachments;

public static class AttachmentEndpoints
{
    /// <summary>Upload, lista e download de anexos (mapeados no Host com o tenant do request, FND-02).</summary>
    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/attachments").WithTags("Anexos");

        group.MapPost("/{ownerType}/{ownerId:guid}", async (string ownerType, Guid ownerId, IFormFile file, AttachmentService service, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return (await service.UploadAsync(ownerType, ownerId, file.FileName, content, ct))
                    .ToHttpResult(dto => Results.Created($"/api/v1/attachments/{dto.Id}", dto));
            })
            .DisableAntiforgery() // a proteção CSRF da API é por Origin + header (FND-04), não por token de formulário
            .WithName("UploadAttachment").Produces<AttachmentDto>(StatusCodes.Status201Created);

        group.MapGet("/{ownerType}/{ownerId:guid}", async (string ownerType, Guid ownerId, AttachmentService service, CancellationToken ct) =>
                (await service.ListAsync(ownerType, ownerId, ct)).ToHttpResult())
            .WithName("ListAttachments").Produces<IReadOnlyList<AttachmentDto>>();

        group.MapGet("/{id:guid}/content", async (Guid id, AttachmentService service, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.DownloadAsync(id, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
            })
            .WithName("DownloadAttachment");

        return api;
    }

    /// <summary>Nunca inline (Results.File com nome já envia Content-Disposition: attachment), sem sniffing e em sandbox.</summary>
    public static void ApplyDownloadHeaders(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        response.Headers[HeaderNames.ContentSecurityPolicy] = "sandbox; default-src 'none'";
        response.Headers[HeaderNames.CacheControl] = "private, no-store";
    }
}
