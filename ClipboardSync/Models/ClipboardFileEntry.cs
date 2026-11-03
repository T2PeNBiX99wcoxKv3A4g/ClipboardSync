using System.Text.Json.Serialization;

namespace ClipboardSync.Models;

/// <summary>One file or directory inside a clipboard file item. Paths use forward slashes.</summary>
public sealed class ClipboardFileEntry
{
    public string Path { get; set; } = string.Empty;

    public long Size { get; set; }

    /// <summary>SHA-256 of the file contents; empty for a directory.</summary>
    public string Hash { get; set; } = string.Empty;

    public bool Directory { get; set; }

    /// <summary>Only used while snapshotting on the source device; never sent or persisted.</summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }
}
