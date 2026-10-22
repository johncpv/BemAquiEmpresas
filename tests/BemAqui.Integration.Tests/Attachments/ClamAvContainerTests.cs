using System.Text;
using BemAqui.BuildingBlocks.Attachments;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BemAqui.Integration.Tests.Attachments;

/// <summary>clamd real (clamav/clamav) para o protocolo INSTREAM; a imagem já traz as assinaturas.</summary>
public sealed class ClamAvFixture : IAsyncLifetime
{
    public IContainer Container { get; } = new ContainerBuilder("clamav/clamav:1.4")
        .WithPortBinding(3310, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("socket found, clamd started"))
        .Build();

    public ClamAvScanner Scanner => new(Options.Create(new ClamAvOptions
    {
        Host = Container.Hostname,
        Port = Container.GetMappedPublicPort(3310),
        Timeout = TimeSpan.FromSeconds(60),
    }));

    public async ValueTask InitializeAsync() => await Container.StartAsync();

    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}

/// <summary>FND-07 CA 19 com o antivírus de verdade: EICAR é detectado, arquivo comum passa.</summary>
public class ClamAvContainerTests(ClamAvFixture clamav) : IClassFixture<ClamAvFixture>
{
    // Arquivo de teste padrão EICAR, montado em tempo de execução (base64) para não ficar literal no repositório.
    private static byte[] Eicar() => Convert.FromBase64String("WDVPIVAlQEFQWzRcUFpYNTQoUF4pN0NDKTd9JEVJQ0FSLVNUQU5EQVJELUFOVElWSVJVUy1URVNULUZJTEUhJEgrSCo=");

    [Fact]
    public async Task EICAR_e_detectado()
    {
        var result = await clamav.Scanner.ScanAsync(new MemoryStream(Eicar()));

        result.Verdict.Should().Be(ScanVerdict.Infected);
        result.Signature.Should().Contain("Eicar");
    }

    [Fact]
    public async Task Arquivo_comum_passa()
    {
        var result = await clamav.Scanner.ScanAsync(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 proposta comercial")));

        result.Verdict.Should().Be(ScanVerdict.Clean);
    }

    /// <summary>ADR 0019 §7 adendo (PRD-01 CA 12): o desenho de 25 MB é varrido; acima de 30 MB é recusado sem ir ao clamd.</summary>
    [Fact]
    public async Task Arquivo_de_25_MB_e_varrido_e_acima_de_30_MB_e_recusado()
    {
        var big = new byte[25 * 1024 * 1024];
        "AC1032"u8.CopyTo(big);
        (await clamav.Scanner.ScanAsync(new MemoryStream(big))).Verdict.Should().Be(ScanVerdict.Clean);

        var tooBig = new MemoryStream(new byte[(30 * 1024 * 1024) + 1]);
        (await clamav.Scanner.ScanAsync(tooBig)).Verdict.Should().Be(ScanVerdict.TooLarge);
    }

    [Fact]
    public async Task Sem_clamd_a_varredura_fica_indisponivel()
    {
        var offline = new ClamAvScanner(Options.Create(new ClamAvOptions { Host = "127.0.0.1", Port = 1, Timeout = TimeSpan.FromSeconds(3) }));

        (await offline.ScanAsync(new MemoryStream([1, 2, 3]))).Verdict.Should().Be(ScanVerdict.Unavailable);
    }
}
