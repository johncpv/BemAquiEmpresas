using System.Text;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Integration.Tests.Messaging;
using BemAqui.Integration.Tests.Persistence;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BemAqui.Integration.Tests.Attachments;

public sealed class FakeScanner : IVirusScanner
{
    public ScanVerdict Verdict { get; set; } = ScanVerdict.Clean;

    public Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ScanResult(Verdict, Verdict == ScanVerdict.Infected ? "Teste-Signature" : null));
}

/// <summary>FND-07 CA 18 a 21: allowlist, ClamAV com falha fechada, permissão do documento dono e cabeçalhos seguros.</summary>
[Collection(PostgresTestGroup.Name)]
public partial class AttachmentsTests(PostgresFixture postgres)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7 conteudo de teste");

    private static async Task<(ServiceProvider Provider, FakeScanner Scanner, string Root)> CreateAsync(PostgresFixture postgres, IPermissionChecker? permissions = null)
    {
        var scanner = new FakeScanner();
        var root = Directory.CreateTempSubdirectory("bemaqui-files-").FullName;
        var provider = await TestPersistence.CreateAsync(
            await postgres.CreateDatabaseAsync("anexo"),
            permissions: permissions ?? new AllowAllPermissionChecker(),
            configure: s =>
            {
                s.AddSingleton<IVirusScanner>(scanner);
                s.Configure<FileStorageOptions>(o => o.Root = root);
                s.AddSingleton(new AttachmentOwnerRegistry().Register("Quote", "commercial"));
            });
        return (provider, scanner, root);
    }

    private static async Task<Result<AttachmentDto>> UploadAsync(IServiceProvider provider, string fileName, byte[] content)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AttachmentService>().UploadAsync("Quote", Guid.CreateVersion7(), fileName, new MemoryStream(content));
    }

    private static async Task<Result<AttachmentContent>> DownloadAsync(IServiceProvider provider, Guid id)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AttachmentService>().DownloadAsync(id);
    }

    private static string[] FilesIn(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories);
}
