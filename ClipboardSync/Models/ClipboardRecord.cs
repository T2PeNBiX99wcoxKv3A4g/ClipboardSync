namespace ClipboardSync.Models;

/// <summary>
///     One clipboard item in the replicated board. The origin device owns the item; every device in
///     the mesh stores a copy. The delivery lists are only meaningful on the origin, which is the one
///     that sends the item out.
/// </summary>
public sealed class ClipboardRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string OriginDeviceId { get; set; } = string.Empty;

    public string OriginDeviceName { get; set; } = string.Empty;

    /// <summary>Only <c>text</c>, <c>image</c> and <c>file</c> are produced today.</summary>
    public string Kind { get; set; } = "text";

    public string Content { get; set; } = string.Empty;

    public string? Meta { get; set; }

    /// <summary>Relative path under the data folder for image/file payloads.</summary>
    public string? DataFile { get; set; }

    /// <summary>The files and directories of a <c>file</c> item, in transfer order.</summary>
    public List<ClipboardFileEntry>? Files { get; set; }

    /// <summary>The top-level entries that go back on the clipboard when the item is applied.</summary>
    public List<string>? Roots { get; set; }

    /// <summary>Total bytes of the files in a <c>file</c> item.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Bytes sent or received so far, used for the progress line in the interface.</summary>
    public long Progress { get; set; }

    /// <summary>SHA-256 of the binary payload, used to avoid duplicate image captures.</summary>
    public string? Hash { get; set; }

    /// <summary>Unix milliseconds.</summary>
    public long At { get; set; }

    public List<string> PendingFor { get; set; } = [];

    public List<string> DeliveredTo { get; set; } = [];

    public List<string> FailedFor { get; set; } = [];
}