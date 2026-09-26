using System.Text.Json;
using System.Text.RegularExpressions;

namespace Paperdoll.Core.Servers;

/// <summary>A server from the hub's list.</summary>
public sealed record ServerListing(string Address, string Name, int Players, int? MaxPlayers);

/// <summary>What a server says it runs, from its <c>/info</c> page.</summary>
/// <param name="ForkId">The <c>build.fork_id</c>, such as <c>delta-v</c>.</param>
/// <param name="Version">The <c>build.version</c>: usually the git commit the server was built from.</param>
public sealed partial record ServerBuild(string? ForkId, string? Version, string? EngineVersion)
{
    /// <summary>Whether the version is a full git commit id, which Paperdoll can fetch.</summary>
    public bool IsCommit => Version != null && CommitId().IsMatch(Version);

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitId();
}

/// <summary>
/// Reads the public server hub and a server's public <c>/info</c> page. Nothing is sent but the
/// requests themselves.
/// </summary>
public sealed partial class GameServers(HttpClient http)
{
    public static readonly Uri Hub = new("https://hub.spacestation14.com/api/servers");

    /// <summary>Every server the hub lists, busiest first.</summary>
    public async Task<IReadOnlyList<ServerListing>> ListAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Hub);
        request.Headers.UserAgent.ParseAdd("Paperdoll");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return ReadHub(doc.RootElement);
    }

    public static IReadOnlyList<ServerListing> ReadHub(JsonElement root) =>
        root.EnumerateArray()
            .Where(s => s.TryGetProperty("address", out _) && s.TryGetProperty("statusData", out var d) && d.ValueKind == JsonValueKind.Object)
            .Select(s =>
            {
                var data = s.GetProperty("statusData");
                return new ServerListing(
                    s.GetProperty("address").GetString() ?? "",
                    data.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    data.TryGetProperty("players", out var players) && players.TryGetInt32(out var p) ? p : 0,
                    data.TryGetProperty("soft_max_players", out var max) && max.TryGetInt32(out var m) ? m : null);
            })
            .Where(s => s.Address.Length > 0)
            .OrderByDescending(s => s.Players)
            .ToList();

    /// <summary>What the server at this address runs.</summary>
    public async Task<ServerBuild> BuildAsync(string address, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, InfoUri(address));
        request.Headers.UserAgent.ParseAdd("Paperdoll");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return ReadBuild(doc.RootElement);
    }

    public static ServerBuild ReadBuild(JsonElement info)
    {
        if (!info.TryGetProperty("build", out var build) || build.ValueKind != JsonValueKind.Object)
            return new ServerBuild(null, null, null);
        string? Text(string key) => build.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return new ServerBuild(Text("fork_id"), Text("version"), Text("engine_version"));
    }

    /// <summary>
    /// A server address as the launcher takes it (<c>ss14://host</c>, <c>ss14s://host/path</c> or a
    /// bare <c>host:port</c>) turned into its <c>/info</c> page. Plain <c>ss14</c> means HTTP on port
    /// 1212 unless another port is given; <c>ss14s</c> means HTTPS.
    /// </summary>
    public static Uri InfoUri(string address)
    {
        var text = address.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "ss14://" + text;
        var secure = text.StartsWith("ss14s://", StringComparison.OrdinalIgnoreCase);
        if (!secure && !text.StartsWith("ss14://", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"{address} is not a server address (ss14:// or ss14s://).");

        var rest = text[(text.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var uri = new UriBuilder((secure ? "https://" : "http://") + rest);
        if (!secure && !HasPort(rest))
            uri.Port = 1212;
        uri.Path = uri.Path.TrimEnd('/') + "/info";
        return uri.Uri;
    }

    private static bool HasPort(string rest) => HostAndPort().IsMatch(rest);

    [GeneratedRegex(@"^[^/]+:\d+(/|$)")]
    private static partial Regex HostAndPort();
}
