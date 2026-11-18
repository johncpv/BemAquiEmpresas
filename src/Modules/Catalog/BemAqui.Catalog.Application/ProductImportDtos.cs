using BemAqui.Catalog.Domain;

namespace BemAqui.Catalog.Application;

/// <summary>Local de estoque para o saldo inicial (CA 6).</summary>
public sealed record ImportLocationChoice(Guid Id, string Name);

/// <summary>
/// Importação de produtos (CAT-02): a prévia (CA 2: total, cria, atualiza, erros por linha, os primeiros 200), os avisos (CA 5: preços ignorados;
/// CA 6: saldo inicial, local sugerido, "Loja" ou o padrão), o progresso (job) e o relatório final (CA 8: criados, atualizados, ignorados).
/// </summary>
public sealed record ProductImportView(
    Guid Id,
    string FileName,
    ProductImportStatus Status,
    int TotalRows,
    int ToCreate,
    int ToUpdate,
    int ErrorCount,
    IReadOnlyList<string> Errors,
    bool PricesIgnored,
    int StockRows,
    bool CanLaunchStock,
    IReadOnlyList<ImportLocationChoice> Locations,
    Guid? SuggestedLocationId,
    IReadOnlyList<string> NewCategories,
    bool InBackground,
    int Processed,
    int ToProcess,
    int Created,
    int Updated,
    int Skipped,
    int StockPosted,
    int StockAlreadyPosted,
    bool LaunchStock,
    string? LocationName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? FailureMessage);

/// <summary>
/// Confirmar a importação (CA 3 e 6): só as linhas válidas entram. <c>LaunchInitialStock</c> = "Lançar saldo inicial" no <c>LocationId</c>
/// (exige <c>inventory.adjust</c>); falso importa sem o saldo.
/// </summary>
public sealed record ProductImportConfirmInput(bool LaunchInitialStock, Guid? LocationId);

/// <summary>Argumentos do job (só ids: nada da planilha vai para o Hangfire).</summary>
public sealed record ProductImportJobArgs(Guid RunId, Guid RequestedBy);
