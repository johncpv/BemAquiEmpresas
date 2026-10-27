using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// BNK-01a CA 9 (ADR 0032 §2 e §3): nenhum código de produção aceita qualquer certificado do servidor (callback de validação
/// permissivo ou o validador "perigoso"), e a URL do Inter só existe no <c>InterEnvironments</c> (URL base fixa, contra SSRF).
/// </summary>
public class InterRulesTests
{
    private static readonly string[] PermissiveValidation =
        ["ServerCertificateCustomValidationCallback", "RemoteCertificateValidationCallback", "DangerousAcceptAnyServerCertificateValidator"];

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", "dist", ".git" };

    private static IEnumerable<string> CsFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory).Where(d => !SkippedDirectories.Contains(Path.GetFileName(d))))
        {
            foreach (var file in CsFiles(child))
            {
                yield return file;
            }
        }
    }

    public static IReadOnlyList<string> PermissiveCertificateValidation(IEnumerable<(string File, string Content)> sources) =>
        sources.SelectMany(s => PermissiveValidation.Where(p => s.Content.Contains(p, StringComparison.Ordinal)).Select(p => $"{s.File}: {p} (proibido fora dos testes)")).ToList();

    private static IEnumerable<(string File, string Content)> ProductionSources() =>
        CsFiles(Path.Combine(Solution.RootDirectory, "src"))
            .Select(f => (Path.GetRelativePath(Solution.RootDirectory, f).Replace('\\', '/'), File.ReadAllText(f)));

    [Fact]
    public void Nenhum_callback_permissivo_de_certificado_do_servidor_fora_dos_testes() =>
        PermissiveCertificateValidation(ProductionSources()).Should().BeEmpty();

    [Fact]
    public void Regra_acusa_o_callback_permissivo() =>
        PermissiveCertificateValidation([("x.cs", "handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;")]).Should().ContainSingle();

    [Fact]
    public void URL_do_Inter_so_no_InterEnvironments()
    {
        ProductionSources().Where(s => s.Content.Contains("partners.uatinter.co", StringComparison.Ordinal) || s.Content.Contains("partners.bancointer.com.br", StringComparison.Ordinal))
            .Select(s => s.File).Should().Equal("src/Modules/Finance/BemAqui.Finance.Infrastructure/Inter/InterHandlers.cs");
    }
}
