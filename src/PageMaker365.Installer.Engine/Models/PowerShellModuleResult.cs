using System.Text.Json;
using System.Text.Json.Serialization;

namespace PageMaker365.Installer.Engine.Models;

public sealed class PowerShellModuleResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("details")]
    public string Details { get; set; } = "";

    [JsonPropertyName("retrySafe")]
    public bool RetrySafe { get; set; }

    [JsonPropertyName("data")]
    public Dictionary<string, JsonElement> Data { get; set; } = [];
}

