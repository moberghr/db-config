using System.Text.Json;
using System.Text.Json.Serialization;

namespace DbConfig.Http.Endpoints;

/// <summary>Shared <see cref="JsonSerializerOptions"/> for the DbConfig HTTP API.</summary>
internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
