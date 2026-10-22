using System.Text.Json;
using System.Text.Json.Serialization;

namespace RequestManagement.Tests.Infrastructure;

public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
