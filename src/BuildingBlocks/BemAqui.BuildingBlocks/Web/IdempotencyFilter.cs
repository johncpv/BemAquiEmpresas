using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Web;

/// <summary>
/// Comando sem documento-pai protegido contra clique duplo e reenvio (ADR 0019, FND-09e CA 15): exige o cabeçalho
/// <c>Idempotency-Key</c>; a mesma chave com o mesmo corpo devolve a resposta original sem gravar de novo; com outro corpo,
/// 422 <c>idempotency_key_reused</c>; enquanto o primeiro envio está em andamento, 409 <c>idempotency_in_progress</c>.
/// </summary>
public static class IdempotencyFilter
{
    public const string Header = "Idempotency-Key";

    public static RouteHandlerBuilder RequireIdempotencyKey(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!Guid.TryParse(http.Request.Headers[Header].ToString(), out var key) || key == Guid.Empty)
            {
                return Error.Validation("idempotency_key_required", "Envie o cabeçalho Idempotency-Key.").ToProblem();
            }

            var db = http.RequestServices.GetRequiredService<BemAquiDbContext>();
            var json = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            var scope = http.Request.Method + " " + ((http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? http.Request.Path.Value);
            var hash = BodyHash(context, json);
            var clock = http.RequestServices.GetRequiredService<TimeProvider>();
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO shared.idempotency_records (key, scope, body_hash, created_at) VALUES ({key}, {scope}, {hash}, {clock.GetUtcNow()}) ON CONFLICT (key) DO NOTHING",
                http.RequestAborted);
            if (inserted == 0)
            {
                return await ReplayAsync(db, key, scope, hash, http.RequestAborted);
            }

            object? result;
            try
            {
                result = await next(context);
            }
            catch
            {
                await ForgetAsync(db, key);
                throw;
            }

            if (result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 } success)
            {
                var body = result is IValueHttpResult value ? JsonSerializer.Serialize(value.Value, json) : null;
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE shared.idempotency_records SET status_code = {success.StatusCode}, response = {body} WHERE key = {key}",
                    CancellationToken.None);
            }
            else
            {
                // Recusa (422, 409...) não fica guardada: o usuário corrige e reenvia com a mesma chave.
                await ForgetAsync(db, key);
            }

            return result;
        });
    }

    private static async Task<object?> ReplayAsync(BemAquiDbContext db, Guid key, string scope, string hash, CancellationToken ct)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key, ct);
        if (record is null)
        {
            return Error.Conflict("idempotency_in_progress", "O mesmo envio ainda está em andamento. Aguarde e atualize a tela.").ToProblem();
        }

        if (record.Scope != scope || record.BodyHash != hash)
        {
            return Error.Validation("idempotency_key_reused", "Esta chave já foi usada em outro envio. Abra o formulário de novo.").ToProblem();
        }

        if (record.StatusCode is not { } status)
        {
            return Error.Conflict("idempotency_in_progress", "O mesmo envio ainda está em andamento. Aguarde e atualize a tela.").ToProblem();
        }

        return record.Response is null ? Results.StatusCode(status) : Results.Content(record.Response, "application/json", Encoding.UTF8, status);
    }

    private static Task<int> ForgetAsync(BemAquiDbContext db, Guid key) =>
        db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM shared.idempotency_records WHERE key = {key}", CancellationToken.None);

    /// <summary>Hash dos argumentos vindos da requisição (corpo e rota), sem os serviços injetados.</summary>
    private static string BodyHash(EndpointFilterInvocationContext context, JsonSerializerOptions json)
    {
        var services = context.HttpContext.RequestServices.GetService<IServiceProviderIsService>();
        var values = context.Arguments.Where(a => a is not null && a is not CancellationToken && a is not HttpContext && services?.IsService(a.GetType()) != true);
        var text = JsonSerializer.Serialize(values.Select(v => JsonSerializer.SerializeToElement(v, v!.GetType(), json)), json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
