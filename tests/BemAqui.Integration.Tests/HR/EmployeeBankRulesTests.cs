using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>CAD-04c2 CA 2, 4, 5, 7, 8 e 9: regras sem banco da conta para pagamento e do dependente.</summary>
public class EmployeeBankRulesTests
{
    private const string EmployeeCpf = "52998224725";
    private const string OtherCpf = "11144477735";

    [Theory]
    [InlineData(EmployeePixKeyType.Cpf, "529.982.247-25", "52998224725")]
    [InlineData(EmployeePixKeyType.Cpf, "529.982.247-24", null)]
    [InlineData(EmployeePixKeyType.Email, " Maria.Silva@Empresa.com.br ", "maria.silva@empresa.com.br")]
    [InlineData(EmployeePixKeyType.Email, "maria@empresa", null)]
    [InlineData(EmployeePixKeyType.Email, "maria silva@empresa.com", null)]
    [InlineData(EmployeePixKeyType.Phone, "(11) 98765-4321", "+5511987654321")]
    [InlineData(EmployeePixKeyType.Phone, "+55 11 98765-4321", "+5511987654321")]
    [InlineData(EmployeePixKeyType.Phone, "98765-4321", null)]
    [InlineData(EmployeePixKeyType.Random, "123E4567-E89B-12D3-A456-426614174000", "123e4567-e89b-12d3-a456-426614174000")]
    [InlineData(EmployeePixKeyType.Random, "123e4567e89b12d3a456426614174000", null)]
    public void Chave_Pix_validada_e_normalizada_pelo_tipo(EmployeePixKeyType type, string key, string? expected) =>
        EmployeeBankRules.NormalizePixKey(type, key).Should().Be(expected);

    [Fact]
    public void Divergencia_quando_titular_ou_chave_CPF_nao_sao_do_funcionario()
    {
        EmployeeBankRules.Mismatches(EmployeeCpf, EmployeeCpf, EmployeePixKeyType.Cpf, EmployeeCpf).Should().BeEmpty();
        EmployeeBankRules.Mismatches(EmployeeCpf, OtherCpf, EmployeePixKeyType.Cpf, EmployeeCpf).Should().Equal(EmployeeBankMismatchKind.Holder);
        EmployeeBankRules.Mismatches(EmployeeCpf, EmployeeCpf, EmployeePixKeyType.Cpf, OtherCpf).Should().Equal(EmployeeBankMismatchKind.PixKey);
        EmployeeBankRules.Mismatches(EmployeeCpf, EmployeeCpf, EmployeePixKeyType.Email, "x@y.com").Should().BeEmpty("só a chave do tipo CPF é comparada");
    }

    [Theory]
    [InlineData(false, "Conferido com o funcionário", false)]
    [InlineData(true, "curto", false)]
    [InlineData(true, "Conta da esposa, conferi com ele", true)]
    public void Segunda_confirmacao_exige_a_marca_e_o_motivo_de_10_a_200(bool checkedWith, string reason, bool expected)
    {
        EmployeeBankRules.MismatchConfirmed(checkedWith, reason).Should().Be(expected);
        EmployeeBankRules.MismatchConfirmed(true, new string('x', 201)).Should().BeFalse();
    }

    [Fact]
    public void Quarentena_de_30_dias_termina_com_a_revisao_ou_com_o_prazo()
    {
        var changed = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var until = EmployeeBankRules.QuarantineEnd(changed);
        until.Should().Be(changed.AddDays(30));
        EmployeeBankRules.InQuarantine(until, null, changed.AddDays(29)).Should().BeTrue();
        EmployeeBankRules.InQuarantine(until, changed.AddDays(2), changed.AddDays(3)).Should().BeFalse("revisada");
        EmployeeBankRules.InQuarantine(until, null, until).Should().BeFalse("terminou sozinha no fim do prazo");
        EmployeeBankRules.InQuarantine(null, null, changed).Should().BeFalse("a primeira conta não entra em quarentena");
    }

    [Fact]
    public void Quem_fez_a_troca_so_revisa_como_Proprietario_unico()
    {
        var author = Guid.NewGuid();
        EmployeeBankRules.CanReview(Guid.NewGuid(), author, soleOwner: false).Should().Be((true, false));
        EmployeeBankRules.CanReview(author, author, soleOwner: false).Should().Be((false, false));
        EmployeeBankRules.CanReview(author, author, soleOwner: true).Should().Be((true, true), "autorrevisado");
    }

    [Fact]
    public void Mascaras_de_CPF_nome_conta_e_chave()
    {
        EmployeeBankRules.MaskCpf("12345678909").Should().Be("***.456.789-**");
        EmployeeBankRules.MaskName("Maria da Silva").Should().Be("M**** d* S****");
        EmployeeBankRules.Tail("123456-7").Should().Be("******-7");
        EmployeeBankRules.MaskPix(EmployeePixKeyType.Email, "maria@empresa.com").Should().Be("m****@empresa.com");
        EmployeeBankRules.MaskPix(EmployeePixKeyType.Phone, "+5511987654321").Should().Be("(**) *****-4321");
        EmployeeBankRules.MaskPix(EmployeePixKeyType.Random, "123e4567-e89b-12d3-a456-426614174000").Should().Be("********4000");
    }

    [Fact]
    public void CPF_do_dependente_so_no_dependente_de_IR()
    {
        DependentRules.Cpf(false, null).Should().Be(((string?)null, (BemAqui.SharedKernel.Error?)null));
        DependentRules.Cpf(false, EmployeeCpf).Error.Should().NotBeNull("sem IR, o CPF não é aceito");
        DependentRules.Cpf(true, "").Error.Should().NotBeNull("com IR, o CPF é obrigatório");
        DependentRules.Cpf(true, "111.111.111-11").Error.Should().NotBeNull("CPF inválido");
        DependentRules.Cpf(true, "529.982.247-25").Cpf.Should().Be(EmployeeCpf);
    }
}
