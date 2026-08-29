using System.Text.Json;
using GameFactory.Diagnostics.Network;
using GameFactory.Networking.Peers;

namespace GameFactory.Tests;

public sealed class SessionManifestWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gamefactory-manifest-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Records_host_and_clients_with_platform_metadata()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "manifest.json");
        var writer = new SessionManifestWriter(path, "session");
        writer.Record("host", PeerId.Server, "host-run", new Dictionary<string, string?> { ["steam_id"] = "1" });
        writer.Record("client", new PeerId(42), "client-run", new Dictionary<string, string?> { ["steam_id"] = "2" });

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("session", document.RootElement.GetProperty("session_id").GetString());
        Assert.Equal("host-run", document.RootElement.GetProperty("host").GetProperty("RunId").GetString());
        Assert.Equal(42, document.RootElement.GetProperty("clients")[0].GetProperty("PeerId").GetInt64());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
