using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Identity.Application.Ai;

/// <summary>Pergunta do painel: o texto, a ajuda da tela atual (prioridade na busca) e a confirmação do aviso de dado da empresa.</summary>
public sealed record AskHelpRequest(string Question, string? CurrentKey, bool Confirmed = false);

/// <summary>Seção usada ou encontrada; o front monta o link pela ajuda (<c>Key</c>) e pela seção.</summary>
public sealed record HelpCitation(string Key, string DocumentTitle, string Section, string Body);

/// <summary>
/// <c>Kind</c>: <c>answer</c> (IA com citação válida), <c>snippets</c> (trechos, sem IA ou com resposta descartada),
/// <c>not_found</c> (nada na ajuda) ou <c>confirm</c> (a pergunta parece ter dado da empresa e precisa de confirmação).
/// </summary>
public sealed record HelpAnswer(string Kind, string Text, IReadOnlyList<HelpCitation> Citations)
{
    public const string AnswerKind = "answer";
    public const string SnippetsKind = "snippets";
    public const string NotFoundKind = "not_found";
    public const string ConfirmKind = "confirm";
}

/// <summary>
/// Pergunte ao BemAqui (FND-15 CA 1 a 8, R-AJD-5): busca só nas ajudas permitidas ao usuário, manda à IA só as instruções
/// fixas, a pergunta e os trechos, e valida a citação no servidor. Sem IA, com falha ou sem citação válida, devolve os trechos.
/// </summary>
public sealed class HelpAnswerService(HelpAccess access, AiSettingsService settings)
{
    public const string SnippetsIntro = "Encontrei estes trechos na ajuda:";
    public const string NotFound = "Não encontrei isso na ajuda.";
    private const int SnippetCount = 3;

    public async Task<Result<HelpAnswer>> AskAsync(Guid userId, AskHelpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length is 0 or > HelpPrompt.MaxQuestionLength)
        {
            var message = question.Length == 0 ? "Escreva a pergunta." : $"Use até {HelpPrompt.MaxQuestionLength} caracteres na pergunta.";
            return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["question"] = [message] });
        }

        // R-AJD-6 e CA 8: a busca só enxerga o que o usuário pode abrir; o resto nunca vai para a IA nem volta.
        var hits = HelpSearch.Find(await access.IndexAsync(userId, cancellationToken), question, request.CurrentKey);
        if (hits.Count == 0)
        {
            return new HelpAnswer(HelpAnswer.NotFoundKind, NotFound, []);
        }

        var active = await settings.ActiveAsync(cancellationToken);
        if (active is null)
        {
            return Snippets(hits);
        }

        if (!request.Confirmed && BusinessDataGuard.LooksLikeBusinessData(question))
        {
            return new HelpAnswer(HelpAnswer.ConfirmKind, BusinessDataGuard.Warning, []);
        }

        var outcome = await settings.CallAsync(active, userId, HelpPrompt.System, HelpPrompt.User(question, hits), 800, null, AiUsageLog.Failed, cancellationToken);
        if (outcome.Completion is not { } completion)
        {
            return Snippets(hits);
        }

        var validated = HelpPrompt.Validate(completion.Text, hits.Count);
        await settings.LogAsync(active, userId, completion, outcome.DurationMs, validated is null ? AiUsageLog.NoCitation : AiUsageLog.Answered, cancellationToken);
        return validated is null
            ? Snippets(hits)
            : new HelpAnswer(HelpAnswer.AnswerKind, validated.Text, validated.Cited.Select(i => Cite(hits[i])).ToList());
    }

    private static HelpAnswer Snippets(IReadOnlyList<HelpHit> hits) => new(HelpAnswer.SnippetsKind, SnippetsIntro, hits.Take(SnippetCount).Select(Cite).ToList());

    private static HelpCitation Cite(HelpHit hit) => new(hit.Document.Key, hit.Document.Title, hit.Section.Title, hit.Section.Body);
}
