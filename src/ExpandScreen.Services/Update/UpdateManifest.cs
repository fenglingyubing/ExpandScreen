using System.Text.Json.Serialization;

namespace ExpandScreen.Services.Update
{
    public sealed class UpdateManifest
    {
        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; init; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; init; }

        [JsonPropertyName("releaseNotes")]
        public string? ReleaseNotes { get; init; }

        [JsonPropertyName("publishedAt")]
        public string? PublishedAt { get; init; }

        [JsonPropertyName("signature")]
        public string? Signature { get; init; }
    }
}

