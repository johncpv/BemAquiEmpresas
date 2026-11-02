using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.GoLive;

/// <summary>
/// <c>tenant copy-files</c> (GOL-01a, ADR 0034 §2): copia só os arquivos dos anexos do tenant, no mesmo caminho, com o hash conferido;
/// relata órfãos, hash divergente e arquivos sem dono; a simulação não copia nada e rodar de novo não copia de novo.
/// </summary>
[Collection(GoLiveGroup.Name)]
public class GoLiveFilesTests(GoLiveFixture golive)
{
    private static string Sha(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    [Fact]
    public async Task Copia_so_os_anexos_do_tenant_com_hash_conferido_e_relata_orfaos_e_arquivos_sem_dono()
    {
        var tenant = await golive.ProvisionAsync("arquivos");
        var from = golive.TempDirectory();
        var to = golive.TempDirectory();
        byte[] good = [1, 2, 3, 4];
        void Write(string relative, byte[] content)
        {
            var path = Path.Combine(from, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        Write("2026/10/bom", good);
        Write("2026/10/alterado", [9, 9]);
        Write("2026/10/do-demo", [5]);
        Write("2026/10/solto", [6]);
        var others = golive.TempFile("2026/10/do-demo\n");

        var info = (await golive.Tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        await using (var scope = await golive.Tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAquiDbContext>();
            Attachment Row(string path, byte[] content) => new()
            {
                OwnerType = "Teste", OwnerId = Guid.CreateVersion7(), FileName = "arquivo.pdf", ContentType = "application/pdf", Size = content.Length, Sha256 = Sha(content),
                StoragePath = path, ScanStatus = ScanStatus.Clean, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Attachments.AddRange(Row("2026/10/bom", good), Row("arquivos/2026/10/sumiu", good), Row("2026/10/alterado", good));
            var removed = Row("2026/10/apagado-lgpd", good);
            removed.ContentRemovedAt = DateTimeOffset.UtcNow;
            db.Attachments.Add(removed);
            await db.SaveChangesAsync();
        }

        string[] args = ["tenant", "copy-files", "--tenant", "arquivos", "--from", from, "--to", to, "--other-tenants-paths", others];
        var dry = new StringWriter();
        (await golive.RunOldAsync(dry, [.. args, "--dry-run"])).Should().Be(2, "há órfão e hash divergente: " + dry);
        Directory.EnumerateFiles(to, "*", SearchOption.AllDirectories).Should().BeEmpty("a simulação não copia");
        dry.ToString().Should().Contain("A copiar: 1").And.Contain("Órfãos (no banco, sem arquivo na origem): 1").And.Contain("Hash diferente do gravado no anexo: 1")
            .And.Contain("outros tenants (lista informada): 1").And.Contain("Arquivos sem dono (na origem, sem anexo deste tenant nem da lista dos outros): 1").And.Contain("2026/10/solto");

        var real = new StringWriter();
        (await golive.RunOldAsync(real, [.. args, "--confirm-tenant", "arquivos"])).Should().Be(2, real.ToString());
        Directory.EnumerateFiles(to, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(to, f).Replace('\\', '/')).Should().Equal("2026/10/bom");
        (await File.ReadAllBytesAsync(Path.Combine(to, "2026", "10", "bom"))).Should().Equal(good);

        var again = new StringWriter();
        await golive.RunOldAsync(again, [.. args, "--confirm-tenant", "arquivos"]);
        again.ToString().Should().Contain("Copiados e conferidos: 0").And.Contain("já no destino com o mesmo hash: 1");
    }
}
