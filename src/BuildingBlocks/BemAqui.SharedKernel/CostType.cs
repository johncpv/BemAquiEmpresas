using System.Text.Json.Serialization;

namespace BemAqui.SharedKernel;

/// <summary>Natureza do custo (ADR 0019 §14): orçamento (COM-01a), projeto (COM-04a) e título a pagar usam o mesmo vocabulário.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CostType>))]
public enum CostType
{
    Material,
    Labor,
    Installation,
    Freight,
    Tax,
    Other,
}
