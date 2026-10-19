namespace BemAqui.Finance.Application;

public sealed record BankInfo(string Code, string Name);

/// <summary>Bancos pelo código COMPE (lista estática do sistema, sem consulta externa; CAD-05b CA 1).</summary>
public static class Banks
{
    public static readonly IReadOnlyList<BankInfo> All =
    [
        new("001", "Banco do Brasil"), new("003", "Banco da Amazônia"), new("004", "Banco do Nordeste"), new("021", "Banestes"),
        new("033", "Santander"), new("037", "Banpará"), new("041", "Banrisul"), new("047", "Banese"), new("070", "BRB"),
        new("077", "Banco Inter"), new("085", "Ailos"), new("097", "Credisis"), new("104", "Caixa Econômica Federal"),
        new("133", "Cresol"), new("136", "Unicred"), new("197", "Stone"), new("208", "BTG Pactual"), new("212", "Banco Original"),
        new("237", "Bradesco"), new("246", "ABC Brasil"), new("260", "Nubank"), new("290", "PagBank"), new("323", "Mercado Pago"),
        new("332", "Acesso Soluções de Pagamento"), new("336", "C6 Bank"), new("341", "Itaú Unibanco"), new("380", "PicPay"),
        new("389", "Banco Mercantil do Brasil"), new("403", "Cora"), new("422", "Banco Safra"), new("623", "Banco Pan"),
        new("633", "Banco Rendimento"), new("637", "Banco Sofisa"), new("655", "Banco Votorantim"), new("707", "Banco Daycoval"),
        new("745", "Citibank"), new("746", "Banco Modal"), new("748", "Sicredi"), new("756", "Sicoob"),
    ];

    public static BankInfo? Find(string? code) => All.FirstOrDefault(b => b.Code == code);
}
