namespace BemAqui.BuildingBlocks.Persistence.Numbering;

/// <summary>Formato de um tipo de documento: prefixo, se reinicia por ano e quantidade de dígitos.</summary>
public sealed record NumberFormat(string Prefix, bool Yearly, int Digits)
{
    public string Format(int year, long value) =>
        Yearly ? $"{Prefix}-{year}-{value.ToString(new string('0', Digits), System.Globalization.CultureInfo.InvariantCulture)}"
               : $"{Prefix}-{value.ToString(new string('0', Digits), System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>Formatos padrão de <c>docs/04-fluxos-estados.md</c> §4 (editáveis por tenant em Configurações, CAD-06).</summary>
public static class NumberFormats
{
    public static IReadOnlyDictionary<string, NumberFormat> Defaults { get; } = new Dictionary<string, NumberFormat>(StringComparer.Ordinal)
    {
        ["ORC"] = new("ORC", true, 4),
        ["CTR"] = new("CTR", true, 4),
        ["PRJ"] = new("PRJ", true, 4),
        ["OS"] = new("OS", true, 5),
        ["NC"] = new("NC", true, 4),
        ["SC"] = new("SC", true, 4),
        ["COT"] = new("COT", true, 4),
        ["PC"] = new("PC", true, 4),
        ["REC"] = new("REC", true, 4),
        ["CR"] = new("CR", true, 5),
        ["CP"] = new("CP", true, 5),
        ["OP"] = new("OP", true, 4),
        ["MAN"] = new("MAN", true, 4),
        ["PAT"] = new("PAT", false, 5),
        ["MOV"] = new("MOV", true, 6),
        ["REQ"] = new("REQ", true, 4),
        ["ROM"] = new("ROM", true, 4),
        ["AJ"] = new("AJ", true, 4),
    };

    /// <summary>Nome de cada tipo na tela (CA 9) e na confirmação ("O próximo orçamento sairá como...").</summary>
    public static IReadOnlyDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ORC"] = "Orçamento", ["CTR"] = "Contrato", ["PRJ"] = "Projeto", ["OS"] = "Ordem de serviço", ["NC"] = "Não conformidade",
        ["SC"] = "Solicitação de compra", ["COT"] = "Cotação", ["PC"] = "Pedido de compra", ["REC"] = "Recebimento",
        ["CR"] = "Conta a receber", ["CP"] = "Conta a pagar", ["OP"] = "Ordem de pagamento", ["MAN"] = "Manutenção", ["PAT"] = "Patrimônio",
        ["MOV"] = "Movimento de estoque", ["REQ"] = "Requisição de material", ["ROM"] = "Romaneio de expedição", ["AJ"] = "Ajuste de estoque",
    };
}
