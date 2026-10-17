using BemAqui.BuildingBlocks.Security;

namespace BemAqui.Integration.Tests;

/// <summary>Certificados autoassinados de teste para proteger o key ring, gravados em arquivo temporário.</summary>
public static class TestCertificates
{
    public const string Password = "teste";

    public static string Primary { get; } = Create("primary");

    public static string Other { get; } = Create("other");

    private static string Create(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bemaqui-dp-{name}-{Environment.ProcessId}.pfx");
        File.WriteAllBytes(path, ProtectionCertificate.CreateSelfSigned($"CN=BemAqui teste {name}", Password));
        return path;
    }
}
