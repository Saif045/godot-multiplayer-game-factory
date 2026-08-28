using System.Text.Json;
using GameFactory.Diagnostics.Network;

namespace GameFactory.Tests;

public sealed class MasterLogWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gamefactory-master-log-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Concurrent_appends_produce_one_valid_json_document_per_line()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "master.jsonl");
        using (var writer = new MasterLogWriter(path))
        {
            Task[] writes = Enumerable.Range(0, 200)
                .Select(sequence => Task.Run(() => writer.Append(new { sequence })))
                .ToArray();
            await Task.WhenAll(writes);
        }

        string[] lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(200, lines.Length);
        Assert.All(lines, line =>
        {
            using JsonDocument document = JsonDocument.Parse(line);
            Assert.True(document.RootElement.TryGetProperty("sequence", out _));
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
