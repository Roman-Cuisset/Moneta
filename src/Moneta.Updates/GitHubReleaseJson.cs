using System.Text.Json.Serialization;

namespace Moneta.Updates;

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("draft")] bool? Draft,
    [property: JsonPropertyName("prerelease")] bool? Prerelease,
    [property: JsonPropertyName("assets")] GitHubAsset?[]? Assets);

internal sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("state")] string? State);

[JsonSerializable(typeof(GitHubRelease[]))]
internal sealed partial class GitHubReleaseJson : JsonSerializerContext;
