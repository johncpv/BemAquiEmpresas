using System.Text.RegularExpressions;

namespace BemAqui.Identity.Application.Ai;

/// <summary>
/// A pergunta digitada pode trazer dado da empresa (ADR 0014): CPF, CNPJ, e-mail, telefone, valor em reais ou número
/// longo. Nesse caso o sistema avisa e só envia à IA depois que o usuário confirma. Não bloqueia nem altera a pergunta.
/// </summary>
public static partial class BusinessDataGuard
{
    public const string Warning =
        "Sua pergunta parece ter dados da empresa (documento, e-mail, telefone ou valor) e seria enviada ao provedor de IA. Prefira perguntar sem esses dados, ou confirme para enviar assim mesmo.";

    public static bool LooksLikeBusinessData(string question) => Pattern().IsMatch(question);

    [GeneratedRegex(@"[^\s@]+@[^\s@]+\.[^\s@]+|R\$\s*\d|\d[\d.\-/() ]{6,}\d", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
