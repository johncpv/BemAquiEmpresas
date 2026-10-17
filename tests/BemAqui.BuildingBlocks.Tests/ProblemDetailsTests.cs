using System.Text.Json;
using BemAqui.BuildingBlocks.Validation;
using BemAqui.BuildingBlocks.Web;
using BemAqui.SharedKernel;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>FND-07 CA 1: falha vira application/problem+json (RFC 9457) com code, title em PT-BR e errors por campo.</summary>
public class ProblemDetailsTests
{
    private sealed record CreateParty(string Name, string Document);

    private sealed class CreatePartyValidator : AbstractValidator<CreateParty>
    {
        public CreatePartyValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Informe o nome.");
            RuleFor(x => x.Document).Length(11, 14).WithMessage("CPF/CNPJ inválido.");
        }
    }

    private static async Task<(int Status, string ContentType, JsonElement Body)> ExecuteAsync(IResult result)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, context.Response.ContentType ?? string.Empty, body.RootElement.Clone());
    }

    [Fact]
    public async Task Validacao_vira_422_com_erros_por_campo()
    {
        var validation = new CreatePartyValidator().Validate(new CreateParty(string.Empty, "123"));
        var result = Result.Failure(validation.ToError()).ToHttpResult();

        var (status, contentType, body) = await ExecuteAsync(result);

        status.Should().Be(422);
        contentType.Should().StartWith("application/problem+json");
        body.GetProperty("title").GetString().Should().Be("Dados inválidos");
        body.GetProperty("code").GetString().Should().Be("validation");
        body.GetProperty("errors").GetProperty("name")[0].GetString().Should().Be("Informe o nome.");
        body.GetProperty("errors").GetProperty("document")[0].GetString().Should().Be("CPF/CNPJ inválido.");
    }

    [Theory]
    [InlineData(ErrorType.NotFound, 404, "Não encontrado")]
    [InlineData(ErrorType.Conflict, 409, "Conflito")]
    [InlineData(ErrorType.Forbidden, 403, "Acesso negado")]
    [InlineData(ErrorType.Unavailable, 503, "Indisponível no momento")]
    public async Task Tipo_de_erro_define_status_e_titulo(ErrorType type, int expectedStatus, string expectedTitle)
    {
        var (status, _, body) = await ExecuteAsync(new Error("x.code", "Mensagem.", type).ToProblem());

        status.Should().Be(expectedStatus);
        body.GetProperty("title").GetString().Should().Be(expectedTitle);
        body.GetProperty("detail").GetString().Should().Be("Mensagem.");
        body.GetProperty("code").GetString().Should().Be("x.code");
    }

    [Fact]
    public async Task Sucesso_sem_valor_e_204_e_com_valor_e_200()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var noContent = new DefaultHttpContext { RequestServices = services };
        await Result.Success().ToHttpResult().ExecuteAsync(noContent);

        var ok = new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };
        await Result.Success(42).ToHttpResult().ExecuteAsync(ok);

        noContent.Response.StatusCode.Should().Be(204);
        ok.Response.StatusCode.Should().Be(200);
    }
}
