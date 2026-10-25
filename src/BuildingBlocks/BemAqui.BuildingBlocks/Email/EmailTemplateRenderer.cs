using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Encodings.Web;
using Fluid;
using Fluid.Values;
using Microsoft.Extensions.FileProviders;

namespace BemAqui.BuildingBlocks.Email;

public sealed record RenderedEmail(string Subject, string Html, string Text);

/// <summary>
/// Renderiza os templates Liquid em sandbox (FND-08 CA 4 e 7): o modelo é só um dicionário de valores simples,
/// nenhum objeto .NET fica exposto, não há include de arquivo e há limite de passos e de recursão. No HTML,
/// todo valor sai escapado.
/// </summary>
public sealed class EmailTemplateRenderer
{
    public const int MaxSteps = 10_000;
    public const int MaxRecursion = 10;

    private static readonly FluidParser Parser = new();
    private readonly ConcurrentDictionary<string, IFluidTemplate> _cache = new(StringComparer.Ordinal);

    public static IReadOnlyCollection<string> SystemTemplates { get; } = ["invite", "password-reset", "account-locked", "payment-order-pending", "quote-sent", "purchase-order-pending", "purchase-order-sent"];

    public RenderedEmail Render(string template, IReadOnlyDictionary<string, object?> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new RenderedEmail(
            RenderPart(template, "subject", model, NullEncoder.Default).Trim(),
            RenderPart(template, "html", model, HtmlEncoder.Default),
            RenderPart(template, "txt", model, NullEncoder.Default));
    }

    /// <summary>Renderiza um template vindo de fora (ex.: personalizado pelo tenant) com as mesmas restrições.</summary>
    public static string RenderUntrusted(string source, IReadOnlyDictionary<string, object?> model, bool html)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Parser.TryParse(source, out var parsed, out var error))
        {
            throw new InvalidOperationException("Template inválido: " + error);
        }

        return parsed.Render(Context(model), html ? HtmlEncoder.Default : NullEncoder.Default);
    }

    private string RenderPart(string template, string part, IReadOnlyDictionary<string, object?> model, TextEncoder encoder)
    {
        var key = template + "." + part;
        var parsed = _cache.GetOrAdd(key, k => Parse(Load(k)));
        return parsed.Render(Context(model), encoder);
    }

    private static TemplateContext Context(IReadOnlyDictionary<string, object?> model)
    {
        var options = new TemplateOptions
        {
            MaxSteps = MaxSteps,
            MaxRecursion = MaxRecursion,
            FileProvider = new NullFileProvider(), // sem include/render de arquivo
            CultureInfo = new System.Globalization.CultureInfo("pt-BR"),
            TimeZone = Time.BusinessTime.Zone,
        };
        var context = new TemplateContext(options);
        foreach (var (name, value) in model)
        {
            // Só valores simples entram: nenhum objeto .NET fica navegável pelo template.
            context.SetValue(name, value switch
            {
                null => NilValue.Instance,
                string s => new StringValue(s),
                bool b => BooleanValue.Create(b),
                int or long or decimal or double => NumberValue.Create(Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture)),
                DateTimeOffset d => new StringValue(Time.BusinessTime.ToBusiness(d).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))),
                _ => new StringValue(value.ToString() ?? string.Empty),
            });
        }

        return context;
    }

    private static IFluidTemplate Parse(string source) =>
        Parser.TryParse(source, out var template, out var error)
            ? template
            : throw new InvalidOperationException("Template de e-mail inválido: " + error);

    private static string Load(string name)
    {
        var resource = typeof(EmailTemplateRenderer).Assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith(".Email.Templates." + name + ".liquid", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Template de e-mail não encontrado: " + name);
        using var stream = typeof(EmailTemplateRenderer).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
