using System.Globalization;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;

namespace BemAqui.Production.Application;

/// <summary>Série candidata a uma linha do romaneio: a série do estoque e o romaneio não cancelado em que ela já está (ou null).</summary>
public sealed record ShipmentSerialCandidate(SerialInfo Serial, string? OpenShipmentNumber);

/// <summary>
/// Regras do romaneio sem banco (PRD-06b CA 1, 2, 6, 10 e 11): limites dos campos, a série que pode entrar numa linha, o estoque geral que
/// exige a confirmação de outra pessoa, as situações que cancelam e estornam, o endereço da obra e os textos fixos.
/// </summary>
public static class ShipmentRules
{
    public const string DocType = "ROM";
    public const string AttachmentOwner = "Shipment";
    public const int MaxAddress = 500;
    public const int MaxPlate = 10;
    public const int MaxDriver = 120;
    public const int MaxReason = 500;
    public const int MaxLines = 100;
    public const int MaxVolumes = 9999;
    public const decimal MaxWeight = 999_999m;

    public const string Confirmed = "Expedição confirmada";
    public const string AwaitingNotice = "Romaneio aguardando a confirmação do estoque geral.";
    public const string OwnBlocked = "Quem montou o romaneio não confirma produto do estoque geral.";
    public const string NotDraft = "Só romaneio em rascunho é confirmado.";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// A série pode entrar na linha (CA 2)? Devolve a mensagem do 422 ou null. A série precisa ser do item, estar no estoque e no local da
    /// linha, não estar em outro romaneio não cancelado e ser do projeto; a de projeto nenhum só entra pela opção "Estoque geral"; a de
    /// outro projeto nunca.
    /// </summary>
    public static string? SerialProblem(ShipmentSerialCandidate candidate, Guid projectId, Guid itemId, Guid locationId, bool fromGeneralStock)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var s = candidate.Serial;
        if (s.ItemId != itemId)
        {
            return $"A série {s.SerialNumber} não é deste item.";
        }

        if (candidate.OpenShipmentNumber is { } other)
        {
            return $"A série {s.SerialNumber} já está no romaneio {other}.";
        }

        if (s.Status != SerialState.InStock)
        {
            return $"A série {s.SerialNumber} não está no estoque.";
        }

        if (s.LocationId != locationId)
        {
            return $"A série {s.SerialNumber} não está neste local.";
        }

        if (s.ProjectId is { } owner && owner != projectId)
        {
            return $"A série {s.SerialNumber} é de outro projeto.";
        }

        if (s.ProjectId is null && !fromGeneralStock)
        {
            return $"A série {s.SerialNumber} é do estoque geral: use \"Incluir produto do estoque geral\".";
        }

        return s.ProjectId is not null && fromGeneralStock ? $"A série {s.SerialNumber} é do projeto: tire a marca \"Estoque geral\" da linha." : null;
    }

    /// <summary>Com alguma linha "Estoque geral", a confirmação passa por outra pessoa com <c>inventory.approve</c> (CA 6, ponto 86).</summary>
    public static bool RequiresStockApproval(IEnumerable<bool> fromGeneralStock) => fromGeneralStock?.Any(g => g) == true;

    /// <summary>"Cancelar romaneio" (CA 10): rascunho ou aguardando a confirmação do estoque.</summary>
    public static bool CanCancel(ShipmentState status) => status is ShipmentState.Draft or ShipmentState.AwaitingStockApproval;

    /// <summary>"Estornar expedição" (CA 11): só o expedido (na Fase 5, também sem NF-e autorizada).</summary>
    public static bool CanReverse(ShipmentState status) => status == ShipmentState.Shipped;

    /// <summary>Romaneio que ainda segura as séries (CA 2 e 16): todos menos o cancelado e o estornado.</summary>
    public static bool HoldsSerials(ShipmentState status) => status is not (ShipmentState.Cancelled or ShipmentState.Reversed);

    /// <summary>Endereço copiado para o romaneio: "Rua X, 100 — sala 2 — Centro — Belo Horizonte/MG — CEP 30100-000".</summary>
    public static string FormatAddress(PartyAddressInfo address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var parts = new List<string> { $"{address.Street}, {address.Number}" };
        if (!string.IsNullOrWhiteSpace(address.Complement))
        {
            parts.Add(address.Complement.Trim());
        }

        parts.Add(address.District);
        parts.Add($"{address.City}/{address.State}");
        if (!string.IsNullOrWhiteSpace(address.ZipCode))
        {
            parts.Add("CEP " + ZipCode(address.ZipCode));
        }

        return string.Join(" — ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>Placa sem espaço e em maiúsculas ("abc 1d23" → "ABC1D23"); vazia vira null.</summary>
    public static string? Plate(string? plate)
    {
        var clean = string.Concat((plate ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        return clean.Length == 0 ? null : clean;
    }

    /// <summary>Motivo do cancelamento ou do estorno: obrigatório, até 500; null se inválido.</summary>
    public static string? Reason(string? reason)
    {
        var clean = reason?.Trim();
        return string.IsNullOrEmpty(clean) || clean.Length > MaxReason ? null : clean;
    }

    /// <summary>Quantidade na tela e no PDF: "2" ou "1,5".</summary>
    public static string Quantity(decimal value) => value.ToString("0.####", PtBr);

    /// <summary>Notificação de quem confirma o estoque geral (CA 6).</summary>
    public static string AwaitingTitle(string number) => $"Romaneio {number} aguardando confirmação: produto do estoque geral.";

    private static string ZipCode(string zip)
    {
        var digits = string.Concat(zip.Where(char.IsDigit));
        return digits.Length == 8 ? $"{digits[..5]}-{digits[5..]}" : zip.Trim();
    }
}
