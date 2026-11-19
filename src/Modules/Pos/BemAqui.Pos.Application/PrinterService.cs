using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Pos.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>Impressora do caixa (IMP-01 CA 6): conexão, largura da bobina e colunas, modelo da lista curta, padrão e o "Saiu certo?" do teste.</summary>
public sealed record PrinterInput(
    Guid RegisterId,
    string Name,
    PrinterConnection Connection,
    int PaperWidthMm,
    int Columns,
    string Model,
    bool IsDefault = true,
    bool IsActive = true,
    bool TestConfirmed = false);

public sealed record PrinterView(
    Guid Id,
    Guid StoreId,
    Guid RegisterId,
    string Name,
    PrinterConnection Connection,
    int PaperWidthMm,
    int Columns,
    string Model,
    bool IsDefault,
    bool IsActive,
    DateTimeOffset? TestConfirmedAt,
    uint Version);

/// <summary>
/// Impressão vinda do aparelho (eventos <c>ReceiptPrinted</c> e <c>ReceiptReprinted</c>, IMP-01 CA 4): o id é do aparelho; nunca leva o CPF nem
/// o conteúdo do cupom, só o que foi impresso, de qual venda, qual via, quem e quando.
/// </summary>
public sealed record PrintJobInput(
    Guid Id,
    Guid RegisterId,
    Guid? PrinterId,
    PrintJobKind Kind,
    Guid? SaleId,
    string? SaleNumber,
    int Copy,
    Guid OperatorId,
    DateTimeOffset PrintedAt);

public sealed record PrintJobView(Guid Id, Guid RegisterId, PrintJobKind Kind, Guid? SaleId, string? SaleNumber, int Copy, Guid OperatorId, DateTimeOffset PrintedAt, bool Replayed);

/// <summary>
/// Impressoras dos caixas (IMP-01 CA 6 e 7) e as impressões registradas (CA 4). A impressora é do caixa: todo aparelho daquele caixa a recebe na
/// sincronização (<see cref="ForRegisterAsync"/>). Nesta story só a conexão pelo navegador funciona; USB direto, rede e Bluetooth são da IMP-02 e
/// da IMP-03. A gravação das impressões é chamada pela subida da OFF-02, como a da sessão de caixa (CX-01).
/// </summary>
public sealed class PrinterService(IPosDb db, TimeProvider clock)
{
    public const string DefaultPrinterIndex = "ix_printers_register_default";

    public async Task<IReadOnlyList<PrinterView>> ListAsync(Guid? registerId, CancellationToken cancellationToken = default)
    {
        var query = db.Printers.AsNoTracking();
        if (registerId is { } id)
        {
            query = query.Where(p => p.RegisterId == id);
        }

        var rows = await query.OrderByDescending(p => p.IsActive).ThenByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToListAsync(cancellationToken);
        return rows.Select(ToView).ToList();
    }

    public async Task<Result<PrinterView>> CreateAsync(PrinterInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var register = await db.Registers.AsNoTracking().FirstOrDefaultAsync(r => r.Id == input.RegisterId, cancellationToken);
        if (register is null || !register.IsActive)
        {
            return Invalid("registerId", "Escolha um caixa ativo.");
        }

        if (Validate(input) is { } invalid)
        {
            return invalid;
        }

        var printer = new Printer { StoreId = register.StoreId, RegisterId = register.Id, CreatedAt = clock.GetUtcNow() };
        // A primeira impressora ativa do caixa já nasce padrão: o caixa sempre sabe onde imprimir.
        var first = !await db.Printers.AnyAsync(p => p.RegisterId == register.Id && p.IsActive, cancellationToken);
        Apply(printer, input with { IsDefault = input.IsDefault || first });
        db.Printers.Add(printer);
        return await SaveAsync(printer, cancellationToken);
    }

    public async Task<Result<PrinterView>> UpdateAsync(Guid id, PrinterInput input, uint version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var printer = await db.Printers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (printer is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Impressora não encontrada.");
        }

        if (printer.Version != version)
        {
            return Error.Conflict("concurrency_conflict", "A impressora mudou desde que você abriu. Recarregue.");
        }

        if (input.RegisterId != printer.RegisterId)
        {
            return Invalid("registerId", "A impressora é do caixa em que foi instalada. Para outro caixa, instale de novo nele.");
        }

        if (Validate(input) is { } invalid)
        {
            return invalid;
        }

        Apply(printer, input);
        return await SaveAsync(printer, cancellationToken);
    }

    /// <summary>
    /// As impressoras ativas do caixa para a foto e o delta do aparelho (CA 7). A versão muda quando qualquer uma muda (o aparelho troca a lista
    /// inteira): é a combinação dos ids e das versões de concorrência.
    /// </summary>
    public async Task<PosSyncPrinters> ForRegisterAsync(Guid registerId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Printers.AsNoTracking().Where(p => p.RegisterId == registerId && p.IsActive)
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToListAsync(cancellationToken);
        var version = 0;
        foreach (var p in rows.OrderBy(p => p.Id))
        {
            version = HashCode.Combine(version, p.Id, p.Version);
        }

        return new PosSyncPrinters(
            rows.Count == 0 ? 0 : Math.Abs(version % 1_000_000_000) + 1,
            rows.Select(p => new PosSyncPrinter(p.Id, p.Name, p.Connection, p.PaperWidthMm, p.Columns, p.Model, p.IsDefault)).ToList());
    }

    /// <summary>
    /// Grava a impressão vinda do aparelho (CA 4), idempotente pelo id: reenviar devolve o que já foi gravado. A 2ª via (<c>Reprint</c>) tem
    /// <c>Copy</c> 2 ou mais e a venda; o operador é obrigatório (quem reimprimiu).
    /// </summary>
    public async Task<Result<PrintJobView>> RecordPrintAsync(PrintJobInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var existing = await db.PrintJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == input.Id, cancellationToken);
        if (existing is not null)
        {
            return existing.RegisterId == input.RegisterId && existing.Kind == input.Kind
                ? ToView(existing, replayed: true)
                : Error.Conflict("print_job_conflict", "Esta impressão já foi gravada com outros dados.");
        }

        if (input.OperatorId == Guid.Empty)
        {
            return Error.Validation("operator_required", "Informe quem imprimiu.");
        }

        if (!Enum.IsDefined(input.Kind) || input.Copy < 1 || (input.Kind == PrintJobKind.Reprint) != (input.Copy > 1))
        {
            return Error.Validation("invalid_copy", "A 2ª via é a reimpressão; o cupom original é a 1ª via.");
        }

        if (input.Kind is PrintJobKind.Receipt or PrintJobKind.Reprint && (input.SaleId is null || string.IsNullOrWhiteSpace(input.SaleNumber) || input.SaleNumber.Length > 40))
        {
            return Error.Validation("sale_required", "Informe a venda do cupom.");
        }

        var now = clock.GetUtcNow();
        if (input.PrintedAt > now + CashSessionIngestion.ClockTolerance)
        {
            return Error.Validation("invalid_time", "A data da impressão está no futuro. Confira o relógio do aparelho.");
        }

        var register = await db.Registers.AsNoTracking().FirstOrDefaultAsync(r => r.Id == input.RegisterId, cancellationToken);
        if (register is null)
        {
            return Error.NotFound("register_not_found", "Caixa não encontrado.");
        }

        var job = PrintJob.FromDevice(input.Id, register.StoreId, register.Id, input.PrinterId, input.Kind, input.SaleId, input.SaleNumber?.Trim(), input.Copy, input.OperatorId, input.PrintedAt, now);
        db.PrintJobs.Add(job);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Error.Conflict("print_job_conflict", "A impressão acabou de ser gravada por outra entrega. Envie de novo para receber a resposta.");
        }

        return ToView(job, replayed: false);
    }

    /// <summary>Quantas vias da venda já saíram e a última (quem e quando), para a tela da venda e a conferência.</summary>
    public async Task<IReadOnlyList<PrintJobView>> JobsOfSaleAsync(Guid saleId, CancellationToken cancellationToken = default) =>
        (await db.PrintJobs.AsNoTracking().Where(j => j.SaleId == saleId).OrderBy(j => j.PrintedAt).ToListAsync(cancellationToken)).Select(j => ToView(j, false)).ToList();

    private static Error? Validate(PrinterInput input)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > Printer.MaxNameLength)
        {
            return Invalid("name", "Dê um nome à impressora, com até 60 caracteres (ex.: Bobina do caixa 1).");
        }

        if (!Enum.IsDefined(input.Connection))
        {
            return Invalid("connection", "Escolha a conexão da impressora.");
        }

        if (input.Connection != PrinterConnection.Browser)
        {
            return Invalid("connection", "Por enquanto, a impressora é ligada pelo navegador (instalada no computador ou no aparelho). USB direto, rede e Bluetooth chegam numa próxima versão.");
        }

        if (!PrinterRules.IsValidLayout(input.PaperWidthMm, input.Columns))
        {
            return Invalid("columns", "Bobina de 58 mm usa 32 colunas; a de 80 mm, 42 ou 48.");
        }

        var model = (input.Model ?? string.Empty).Trim();
        return model.Length is 0 or > Printer.MaxModelLength ? Invalid("model", "Escolha o modelo da impressora na lista (ou \"Outro modelo\").") : null;
    }

    private void Apply(Printer printer, PrinterInput input)
    {
        printer.Name = input.Name.Trim();
        printer.Connection = input.Connection;
        printer.PaperWidthMm = input.PaperWidthMm;
        printer.Columns = input.Columns;
        printer.Model = input.Model.Trim();
        printer.IsActive = input.IsActive;
        printer.IsDefault = input.IsDefault && input.IsActive;
        if (input.TestConfirmed && printer.TestConfirmedAt is null)
        {
            printer.TestConfirmedAt = clock.GetUtcNow();
        }
    }

    /// <summary>Uma padrão por caixa: a nova padrão tira a marca das outras na mesma gravação (o índice único parcial garante na corrida).</summary>
    private async Task<Result<PrinterView>> SaveAsync(Printer printer, CancellationToken cancellationToken)
    {
        try
        {
            if (printer.IsDefault)
            {
                var others = await db.Printers.Where(p => p.RegisterId == printer.RegisterId && p.Id != printer.Id && p.IsDefault).ToListAsync(cancellationToken);
                if (others.Count > 0)
                {
                    // Tira a marca das outras antes de gravar a nova padrão: o índice único parcial é conferido a cada comando do lote.
                    foreach (var other in others)
                    {
                        other.IsDefault = false;
                    }

                    printer.IsDefault = false;
                    await db.SaveChangesAsync(cancellationToken);
                    printer.IsDefault = true;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, DefaultPrinterIndex))
        {
            return Error.Conflict("printer_default_conflict", "Outra impressora acabou de virar a padrão deste caixa. Recarregue e tente de novo.");
        }

        return ToView(printer);
    }

    private static PrinterView ToView(Printer p) =>
        new(p.Id, p.StoreId, p.RegisterId, p.Name, p.Connection, p.PaperWidthMm, p.Columns, p.Model, p.IsDefault, p.IsActive, p.TestConfirmedAt, p.Version);

    private static PrintJobView ToView(PrintJob j, bool replayed) => new(j.Id, j.RegisterId, j.Kind, j.SaleId, j.SaleNumber, j.Copy, j.OperatorId, j.PrintedAt, replayed);

    private static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
