using System.Text.Json;
using GameFactory.Diagnostics.Network;

namespace GameFactory.Tests;

public sealed class ReceivedSequenceLedgerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gamefactory-sequence-ledger-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Retry_after_a_derived_render_failure_does_not_duplicate_authoritative_entries()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "master.jsonl");
        var ledger = new ReceivedSequenceLedger();
        using (var writer = new MasterLogWriter(path))
        {
            Ingest(writer, ledger, "client-run", [1L, 2L, 3L]);
            // The session renderer may fail here; its result never changes committed sequence state.
            Ingest(writer, ledger, "client-run", [1L, 2L, 3L]);
            Ingest(writer, ledger, "client-run", [4L]);
        }

        long[] sequences = File.ReadAllLines(path)
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("sequence").GetInt64())
            .ToArray();
        Assert.Equal([1L, 2L, 3L, 4L], sequences);
        Assert.Equal(4, ledger.GetHighest("client-run"));
    }

    private static void Ingest(MasterLogWriter writer, ReceivedSequenceLedger ledger, string runId, IEnumerable<long> sequences)
    {
        foreach (long sequence in sequences)
        {
            if (sequence <= ledger.GetHighest(runId)) continue;
            writer.Append(new { run_id = runId, sequence });
            ledger.Commit(runId, sequence);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
