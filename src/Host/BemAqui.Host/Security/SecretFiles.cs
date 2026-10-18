using Microsoft.Extensions.FileProviders;

namespace BemAqui.Host.Security;

/// <summary>
/// Segredos como arquivos (Docker secrets, FND-10): cada arquivo de <c>/run/secrets</c> cujo nome tem <c>__</c> vira uma
/// chave de configuração (<c>ConnectionStrings__Catalog</c> → <c>ConnectionStrings:Catalog</c>). Assim a senha não aparece
/// no <c>docker inspect</c>, como apareceria numa variável de ambiente. Arquivos sem <c>__</c> (ex.: o .pfx) são ignorados.
/// </summary>
public static class SecretFiles
{
    public const string DefaultDirectory = "/run/secrets";
    public const string DirectoryVariable = "BQ_SECRETS_DIR";

    public static IConfigurationBuilder AddSecretFiles(this IConfigurationBuilder builder, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var path = directory ?? Environment.GetEnvironmentVariable(DirectoryVariable) ?? DefaultDirectory;
        if (!Directory.Exists(path))
        {
            return builder;
        }

        return builder.AddKeyPerFile(source =>
        {
            source.FileProvider = new PhysicalFileProvider(Path.GetFullPath(path));
            source.Optional = true;
            source.ReloadOnChange = false;
            source.IgnoreCondition = name => !name.Contains("__", StringComparison.Ordinal);
        });
    }
}
