using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using BemAqui.Finance.Application.Inter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>
/// Formulário de "Salvar integração bancária" e "Trocar credenciais" (<c>multipart/form-data</c>, CA 4): os arquivos do certificado
/// vêm no corpo (nunca na query), até 20 KB cada, e não passam pela pasta de anexos nem pelo <c>IFileStorage</c>.
/// </summary>
public sealed class BankIntegrationForm
{
    [FromForm(Name = "environment")]
    public string? Environment { get; set; }

    [FromForm(Name = "clientId")]
    public string? ClientId { get; set; }

    [FromForm(Name = "clientSecret")]
    public string? ClientSecret { get; set; }

    [FromForm(Name = "accountNumber")]
    public string? AccountNumber { get; set; }

    /// <summary>Certificado <c>.crt</c> ou <c>.pem</c>.</summary>
    [FromForm(Name = "certificate")]
    public IFormFile? Certificate { get; set; }

    /// <summary>Chave privada <c>.key</c> ou <c>.pem</c>, sem senha.</summary>
    [FromForm(Name = "privateKey")]
    public IFormFile? PrivateKey { get; set; }

    /// <summary>Alternativa ao par acima: <c>.pfx</c> com a senha.</summary>
    [FromForm(Name = "pfx")]
    public IFormFile? Pfx { get; set; }

    [FromForm(Name = "pfxPassword")]
    public string? PfxPassword { get; set; }

    /// <summary>Confirmação do diálogo "Ligar a cobrança real" (CA 12), só com a cobrança real liberada.</summary>
    [FromForm(Name = "confirmProduction")]
    public bool ConfirmProduction { get; set; }

    [FromForm(Name = "version")]
    public uint? Version { get; set; }

    public override string ToString() => "BankIntegrationForm(***)";
}

/// <summary>
/// Configurações → Integrações → Banco Inter (BNK-01a CA 17): os módulos <c>settings</c> e <c>finance</c> liberados (um por grupo);
/// leitura com <c>settings.view</c> ou <c>finance.view</c>; salvar, trocar e desligar com <c>settings.edit</c> e o 2FA de novo; testar
/// com <c>settings.edit</c> e rate limit. Respostas sem cache (dados bancários). Conta de outro tenant ou excluída: 404.
/// </summary>
internal static partial class FinanceEndpoints
{
    private static readonly string[] CertificateExtensions = [".crt", ".pem", ".cer"];
    private static readonly string[] KeyExtensions = [".key", ".pem"];
    private static readonly string[] PfxExtensions = [".pfx", ".p12"];

    public static IEndpointRouteBuilder MapBankIntegrationEndpoints(this IEndpointRouteBuilder api)
    {
        var settings = api.MapGroup("/settings/integrations/inter").RequireModule("settings").WithTags("Configurações")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        var inter = settings.MapGroup(string.Empty).RequireModule("finance");

        inter.MapGet(string.Empty, async (BankIntegrationService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequireAnyPermissionOf("settings.view", "finance.view").WithName("ListBankIntegrations").Produces<BankIntegrationList>();
        inter.MapGet("/{bankAccountId:guid}", async (Guid bankAccountId, BankIntegrationService service, CancellationToken ct) => (await service.GetAsync(bankAccountId, ct)).ToHttpResult())
            .RequireAnyPermissionOf("settings.view", "finance.view").WithName("GetBankIntegration").Produces<BankIntegrationDetail>();
        inter.MapPut("/{bankAccountId:guid}", async (Guid bankAccountId, [FromForm] BankIntegrationForm form, BankIntegrationService service, CancellationToken ct) =>
                await ReadAsync(form, ct) is { } command ? (await service.SaveAsync(bankAccountId, command, ct)).ToHttpResult() : InvalidFile())
            .RequirePermission("settings.edit").RequireRecentTwoFactor().DisableAntiforgery().Accepts<BankIntegrationForm>("multipart/form-data")
            .WithName("SaveBankIntegration").Produces<BankIntegrationDetail>();
        inter.MapPost("/{bankAccountId:guid}/rotate", async (Guid bankAccountId, [FromForm] BankIntegrationForm form, BankIntegrationService service, CancellationToken ct) =>
                await ReadAsync(form, ct) is { } command ? (await service.RotateAsync(bankAccountId, command, ct)).ToHttpResult() : InvalidFile())
            .RequirePermission("settings.edit").RequireRecentTwoFactor().DisableAntiforgery().Accepts<BankIntegrationForm>("multipart/form-data")
            .WithName("RotateBankIntegration").Produces<BankIntegrationDetail>();
        inter.MapPost("/{bankAccountId:guid}/test", async (Guid bankAccountId, BankIntegrationService service, CancellationToken ct) => (await service.TestAsync(bankAccountId, ct)).ToHttpResult())
            .RequirePermission("settings.edit").RequireRateLimiting(BankIntegrationService.TestRateLimitPolicy).WithName("TestBankIntegration").Produces<BankConnectionTest>();
        inter.MapDelete("/{bankAccountId:guid}", async (Guid bankAccountId, uint? version, BankIntegrationService service, CancellationToken ct) => (await service.DisableAsync(bankAccountId, version, ct)).ToHttpResult())
            .RequirePermission("settings.edit").RequireRecentTwoFactor().WithName("DisableBankIntegration");
        return api;
    }

    /// <summary>Lê os arquivos do formulário com o limite de 20 KB e as extensões aceitas (CA 4); fora disso, nulo (422).</summary>
    private static async Task<BankIntegrationCommand?> ReadAsync(BankIntegrationForm form, CancellationToken ct)
    {
        var certificate = await FileAsync(form.Certificate, CertificateExtensions, ct);
        var key = await FileAsync(form.PrivateKey, KeyExtensions, ct);
        var pfx = await FileAsync(form.Pfx, PfxExtensions, ct);
        if (certificate.Invalid || key.Invalid || pfx.Invalid)
        {
            return null;
        }

        return new BankIntegrationCommand(form.Environment, form.ClientId, form.ClientSecret, form.AccountNumber,
            new InterCertificateUpload(certificate.Bytes, key.Bytes, pfx.Bytes, string.IsNullOrEmpty(form.PfxPassword) ? null : form.PfxPassword), form.ConfirmProduction, form.Version);
    }

    private static async Task<(byte[]? Bytes, bool Invalid)> FileAsync(IFormFile? file, string[] extensions, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return (null, false);
        }

        if (file.Length > InterCertificateValidator.MaxFileBytes || !extensions.Contains(Path.GetExtension(file.FileName), StringComparer.OrdinalIgnoreCase))
        {
            return (null, true);
        }

        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);
        return (memory.ToArray(), false);
    }

    private const string InvalidFileMessage = "Envie só .crt, .pem, .key ou .pfx, com até 20 KB cada.";

    private static IResult InvalidFile() =>
        BemAqui.SharedKernel.Error.Validation("bank_certificate_invalid", InvalidFileMessage, new Dictionary<string, string[]> { ["certificate"] = [InvalidFileMessage] }).ToProblem();
}
