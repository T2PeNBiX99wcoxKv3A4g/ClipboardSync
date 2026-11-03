namespace ClipboardSync.Models;

/// <summary>A device found by the local-network discovery socket but not paired yet.</summary>
internal sealed record DiscoveredDevice(string Id, string Name, string Host, int Port);
