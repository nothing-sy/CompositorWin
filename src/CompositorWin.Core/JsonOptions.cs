using System.Text.Json;
using System.Text.Json.Serialization;

namespace CompositorWin.Core;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Manifest = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
}
