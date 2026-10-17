using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

public class ResultTests
{
    private static readonly Error NotFound = Error.NotFound("party.not_found", "Terceiro não encontrado.");

    [Fact]
    public void Sucesso_com_valor()
    {
        Result<int> result = 42;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Falha_carrega_o_erro()
    {
        Result<int> result = NotFound;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotFound);
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void Ler_valor_de_falha_e_erro_de_programacao()
    {
        Result<int> result = NotFound;

        var ler = () => result.Value;

        ler.Should().Throw<InvalidOperationException>().WithMessage("*party.not_found*");
    }

    [Fact]
    public void Resultado_sem_valor()
    {
        Result.Success().IsSuccess.Should().BeTrue();
        Result.Failure(NotFound).Error.Should().Be(NotFound);
    }
}
