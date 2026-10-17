using BemAqui.BuildingBlocks.Security;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BemAqui.Integration.Tests;

/// <summary>FND-11 CA 3: certificado ausente, inválido ou fraco falha com mensagem clara; certificado de dev só com opt-in.</summary>
public class ProtectionCertificateTests
{
    private sealed class FakeEnvironment(string name, string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "BemAqui.Tests";

        public string ContentRootPath { get; set; } = contentRoot;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static readonly FakeEnvironment Production = new(Environments.Production, Path.GetTempPath());

    [Fact]
    public void Certificado_valido_com_senha_em_arquivo_carrega()
    {
        using var certificate = ProtectionCertificate.Load(
            new SecretsOptions { CertificatePath = TestCertificates.Primary, CertificatePasswordFile = TestCertificates.PasswordFile }, Production);

        certificate.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public void Senha_errada_falha_com_mensagem_clara()
    {
        var carregar = () => ProtectionCertificate.Load(new SecretsOptions { CertificatePath = TestCertificates.Primary, CertificatePassword = "errada" }, Production);

        carregar.Should().Throw<InvalidOperationException>().WithMessage("*senha errada ou arquivo inválido*");
    }

    [Fact]
    public void Certificado_sem_chave_privada_e_recusado()
    {
        var carregar = () => ProtectionCertificate.Load(new SecretsOptions { CertificatePath = TestCertificates.WithoutPrivateKey, CertificatePassword = TestCertificates.Password }, Production);

        carregar.Should().Throw<InvalidOperationException>().WithMessage("*não tem chave privada*");
    }

    [Fact]
    public void Certificado_com_RSA_abaixo_de_3072_e_recusado()
    {
        var carregar = () => ProtectionCertificate.Load(new SecretsOptions { CertificatePath = TestCertificates.Weak, CertificatePassword = TestCertificates.Password }, Production);

        carregar.Should().Throw<InvalidOperationException>().WithMessage("*mínimo é 3072*");
    }

    [Fact]
    public void Desenvolvimento_sem_opt_in_nao_gera_certificado()
    {
        var root = Directory.CreateTempSubdirectory("bemaqui-dev-").FullName;
        try
        {
            var carregar = () => ProtectionCertificate.Load(new SecretsOptions(), new FakeEnvironment(Environments.Development, root));

            carregar.Should().Throw<InvalidOperationException>().WithMessage("*GenerateDevelopmentCertificate*");
            File.Exists(Path.Combine(root, ProtectionCertificate.DevelopmentFile)).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Producao_nunca_gera_certificado_mesmo_com_opt_in()
    {
        var carregar = () => ProtectionCertificate.Load(new SecretsOptions { GenerateDevelopmentCertificate = true }, Production);

        carregar.Should().Throw<InvalidOperationException>().WithMessage("*CertificatePath não configurado*");
    }
}
