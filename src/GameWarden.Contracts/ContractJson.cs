using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameWarden.Contracts;

/// <summary>JSON settings shared by every file and message exchanged between GameWarden processes.</summary>
public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
