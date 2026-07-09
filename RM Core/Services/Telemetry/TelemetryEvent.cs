using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RM_Core.Services.Telemetry
{
    public class TelemetryEvent
    {
        [JsonPropertyName("event")]
        public string Event { get; set; } = string.Empty;

        [JsonPropertyName("install_id")]
        public string InstallId { get; set; } = string.Empty;

        [JsonPropertyName("ts")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("app_version")]
        public string AppVersion { get; set; } = "1.0.0";

        [JsonPropertyName("os_version")]
        public string OsVersion { get; set; } = string.Empty;

        [JsonPropertyName("dotnet_version")]
        public string DotnetVersion { get; set; } = string.Empty;

        [JsonPropertyName("country")]
        public string Country { get; set; } = string.Empty;

        [JsonPropertyName("client_count")]
        public int ClientCount { get; set; }

        [JsonPropertyName("base_count")]
        public int BaseCount { get; set; }

        [JsonPropertyName("props")]
        public Dictionary<string, object>? Props { get; set; }
    }
}
