using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace GameFactory.Diagnostics;

/// <summary>Reads and reports the immutable identity of an exported test build.</summary>
public static class BuildIdentity
{
    public static void LogCurrent()
    {
        string path = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? string.Empty, "build_manifest.json");
        if (!File.Exists(path))
        {
            if (OS.HasFeature("editor")) return;
            GameLog.Warning("build.identity", "manifest_missing", fields: new Dictionary<string, string?> { ["path"] = path });
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            GameLog.Info("build.identity", "loaded", fields: new Dictionary<string, string?>
            {
                ["build_id"] = ReadString(root, "build_id"),
                ["git_commit"] = ReadString(root, "git_commit"),
                ["content_sha256"] = ReadString(root, "content_sha256"),
                ["source_dirty"] = root.TryGetProperty("source_dirty", out JsonElement dirty) ? dirty.GetBoolean().ToString() : null,
                ["file_count"] = root.TryGetProperty("file_count", out JsonElement count) ? count.GetInt32().ToString() : null
            });
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            GameLog.Error("build.identity", "manifest_invalid", exception.Message, new Dictionary<string, string?> { ["path"] = path });
        }
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement value) ? value.GetString() : null;
}
