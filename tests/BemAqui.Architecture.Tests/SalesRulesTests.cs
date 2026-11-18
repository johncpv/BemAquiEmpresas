using System.Reflection;
using System.Text.RegularExpressions;
using BemAqui.Host.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// VND-01 (ADR 0006, 02 seção 4.1): o <c>Sales</c> tem as cinco camadas e só referencia <c>*.Contracts</c>; nenhum outro módulo escreve no schema
/// <c>sales</c>; e <b>[Segurança]</b> (CA 8) nenhum campo do modelo nem dos contratos guarda número de cartão, validade, código de segurança ou trilha.
/// </summary>
public partial class SalesRulesTests
{
    private const string Module = "Sales";

    /// <summary>Nomes de campo proibidos (PCI): número do cartão (PAN), validade, código de segurança e trilha.</summary>
    [GeneratedRegex(@"(card_?(number|no|num)|(^|_)pan(_|$)|primary_?account|cvv|cvc|(^|_)cvn(_|$)|security_?code|card_?(exp|valid)|valid_?thru|(^|_)exp_?(month|year|date)(_|$)|track_?[12]?_?data|^track[12]?$|magstripe|pin_?block)", RegexOptions.IgnoreCase)]
    private static partial Regex CardField();

    private static string Snake(string name) => Regex.Replace(name, "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant();

    public static bool IsCardField(string name) => CardField().IsMatch(Snake(name)) || CardField().IsMatch(name);

    [Fact]
    public void Sales_tem_as_cinco_camadas_e_so_referencia_Contracts_de_outros_modulos()
    {
        var sales = Solution.Projects.Where(p => p.Module == Module).ToList();
        sales.Select(p => p.Layer).Should().BeEquivalentTo(["Domain", "Application", "Infrastructure", "Endpoints", "Contracts"]);
        Rules.CrossModuleReferences(sales).Should().BeEmpty();
        Rules.ContractsReferences(sales).Should().BeEmpty();

        // Nenhum outro módulo enxerga as camadas internas do Sales: só o Sales.Contracts (ISaleIngestion, IOrderPayments, ISaleLookup, ISalesRevenue).
        Solution.Projects.Where(p => p.Module is not null && p.Module != Module)
            .SelectMany(p => p.ProjectReferences.Select(r => (p.Name, Target: Solution.Classify(r))))
            .Where(x => x.Target.Module == Module && x.Target.Layer != "Contracts")
            .Select(x => x.Name).Should().BeEmpty();
    }

    [Fact]
    public void So_o_Sales_tem_entidade_no_schema_sales()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql("Host=localhost").UseSnakeCaseNamingConvention().Options;
        using var db = new TenantDbContext(options);
        var inSchema = db.Model.GetEntityTypes().Where(t => t.GetSchema() == "sales").ToList();
        inSchema.Select(t => t.GetTableName()).Should().Contain(["sales", "sale_lines", "sale_parts", "sale_payments", "stock_postings"]);
        inSchema.Select(t => t.ClrType.Assembly.GetName().Name).Distinct().Should().Equal("BemAqui.Sales.Domain");
        // E o Sales não tem tabela fora do schema dele.
        db.Model.GetEntityTypes().Where(t => t.ClrType.Assembly.GetName().Name!.StartsWith("BemAqui.Sales.", StringComparison.Ordinal))
            .Select(t => t.GetSchema()).Distinct().Should().Equal("sales");
    }

    [Fact]
    public void Seguranca_nenhuma_coluna_do_banco_guarda_dado_de_cartao()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql("Host=localhost").UseSnakeCaseNamingConvention().Options;
        using var db = new TenantDbContext(options);
        var columns = db.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties().Select(p => (Table: t.GetSchema() + "." + t.GetTableName(), Property: p.Name, Column: p.GetColumnName())))
            .ToList();
        columns.Should().Contain(c => c.Table == "sales.sale_payments" && c.Column == "nsu", "a varredura enxerga a tabela de pagamentos");
        columns.Where(c => IsCardField(c.Property) || IsCardField(c.Column)).Select(c => $"{c.Table}.{c.Column}")
            .Should().BeEmpty("o sistema nunca guarda número de cartão, validade, código de segurança nem trilha (regra 14 do PRD do PDV, VND-01 CA 8)");
    }

    [Fact]
    public void Seguranca_nenhum_contrato_de_venda_ou_pagamento_tem_campo_de_cartao()
    {
        Assembly[] assemblies = [typeof(BemAqui.Sales.Contracts.ISaleIngestion).Assembly, typeof(BemAqui.Sales.Domain.Sale).Assembly, typeof(BemAqui.Finance.Contracts.IPaymentMethods).Assembly];
        var properties = assemblies.SelectMany(a => a.GetExportedTypes())
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => $"{t.Name}.{p.Name}").Concat(
                t.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => $"{t.Name}({p.Name})")))
            .Distinct().ToList();
        properties.Should().Contain("SalePaymentInput.Nsu");
        properties.Where(p => IsCardField(p.Split('.', '(')[^1].TrimEnd(')'))).Should().BeEmpty("dos cartões ficam só bandeira, NSU, autorização e a referência do provedor (CA 8)");
    }

    [Theory]
    [InlineData("CardNumber")]
    [InlineData("card_number")]
    [InlineData("Pan")]
    [InlineData("Cvv")]
    [InlineData("SecurityCode")]
    [InlineData("CardExpiry")]
    [InlineData("ExpMonth")]
    [InlineData("Track2")]
    [InlineData("TrackData")]
    public void Regra_acusa_campo_de_cartao(string name) => IsCardField(name).Should().BeTrue();

    [Theory]
    [InlineData("CardBrand")]
    [InlineData("Nsu")]
    [InlineData("AuthorizationCode")]
    [InlineData("Txid")]
    [InlineData("Company")]
    [InlineData("ExpectedFee")]
    [InlineData("Expires")]
    [InlineData("Panel")]
    public void Regra_nao_acusa_campo_permitido(string name) => IsCardField(name).Should().BeFalse();
}
