using System.Text;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Integration.Tests.Messaging;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BemAqui.Integration.Tests.Attachments;

public partial class AttachmentsTests
{
    [Fact]
    public async Task ClamAV_fora_do_ar_bloqueia_o_download_ate_o_rescan()
    {
        var (provider, scanner, _) = await CreateAsync(postgres);
        await using var disposeProvider = provider;
        scanner.Verdict = ScanVerdict.Unavailable;

        var uploaded = await UploadAsync(provider, "proposta.pdf", Pdf);
        uploaded.Value.ScanStatus.Should().Be(ScanStatus.Pending);
        (await DownloadAsync(provider, uploaded.Value.Id)).Error!.Code.Should().Be("attachment.not_scanned");

        scanner.Verdict = ScanVerdict.Clean;
        var tenant = new TenantInfo(Guid.CreateVersion7(), "konebras", TenantStatus.Active);
        var directory = new FakeTenantDirectory();
        directory.Tenants.Add(tenant);
        var job = new RescanPendingAttachmentsJob(directory, new SameDatabaseScopeFactory(provider), NullLogger<RescanPendingAttachmentsJob>.Instance);
        await job.ExecuteAsync(tenant.Id, new RescanArgs(), CancellationToken.None);

        (await DownloadAsync(provider, uploaded.Value.Id)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Sem_permissao_no_modulo_dono_e_forbidden()
    {
        var (provider, _, _) = await CreateAsync(postgres, new DenyAllPermissionChecker());
        await using var disposeProvider = provider;

        (await UploadAsync(provider, "proposta.pdf", Pdf)).Error!.Type.Should().Be(ErrorType.Forbidden);

        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AttachmentService>();
        (await service.ListAsync("Quote", Guid.CreateVersion7())).Error!.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Download_de_anexo_existente_sem_permissao_e_forbidden()
    {
        var (provider, _, root) = await CreateAsync(postgres);
        await using var disposeProvider = provider;
        var uploaded = await UploadAsync(provider, "proposta.pdf", Pdf);

        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var denied = new AttachmentService(
            services.GetRequiredService<BemAqui.BuildingBlocks.Persistence.BemAquiDbContext>(),
            services.GetRequiredService<IFileStorage>(),
            services.GetRequiredService<IVirusScanner>(),
            services.GetRequiredService<AttachmentOwnerRegistry>(),
            new DenyAllPermissionChecker(),
            services.GetRequiredService<BemAqui.BuildingBlocks.Identity.ICurrentUser>(),
            TimeProvider.System);

        var download = await denied.DownloadAsync(uploaded.Value.Id);

        download.Error!.Type.Should().Be(ErrorType.Forbidden);
        download.Error.Code.Should().Be(Error.Codes.Forbidden);
        FilesIn(root).Should().ContainSingle();
    }

    [Fact]
    public async Task Download_sempre_como_anexo_sem_sniffing_e_em_sandbox()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        AttachmentEndpoints.ApplyDownloadHeaders(context.Response);
        await Results.File(new MemoryStream(Encoding.UTF8.GetBytes("<svg/>")), "image/svg+xml", "logo.svg").ExecuteAsync(context);

        context.Response.Headers.ContentDisposition.ToString().Should().StartWith("attachment;");
        context.Response.Headers.XContentTypeOptions.ToString().Should().Be("nosniff");
        context.Response.Headers.ContentSecurityPolicy.ToString().Should().Contain("sandbox");
    }
}
