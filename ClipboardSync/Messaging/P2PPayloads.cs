namespace ClipboardSync.Messaging;

// Shapes that cross the bridge for the device board, clipboard history and pairing flow.

internal sealed record DiscoveredDevicePayload(string Id, string Name, string Host, int Port);

internal sealed record DevicePayload(string Id, string Name, bool Self, bool Online, int ItemCount);

internal sealed record DevicesPayload(DevicePayload Self, DevicePayload[] Peers, DiscoveredDevicePayload[] Discovered);

internal sealed record ClipboardItemPayload(
    string Id,
    string OriginDeviceId,
    string OriginDeviceName,
    string Kind,
    string Content,
    string? Meta,
    long At,
    string Direction,
    string Status,
    int Reached,
    int Peers);

internal sealed record HistoryPayload(ClipboardItemPayload[] Items);

/// <summary>Payload for <c>pair.start</c>: the address of the device to pair with.</summary>
internal sealed record PairStartRequest(string Host, int Port);

/// <summary>Payload for <c>pair.respond</c>: the incoming request the user answered.</summary>
internal sealed record PairRespondRequest(string RequestId, bool Accept);

/// <summary>Payload for <c>device.remove</c>.</summary>
internal sealed record DeviceRemoveRequest(string DeviceId);

/// <summary>Body of the <c>pair.code</c> event: the code both devices should show.</summary>
internal sealed record PairCodePayload(string Code, string DeviceId, string DeviceName);

/// <summary>Body of the <c>pair.request</c> event: a peer is waiting for the local user.</summary>
internal sealed record PairRequestPayload(string RequestId, string DeviceId, string DeviceName, string Code);

/// <summary>Body of the <c>pair.result</c> event.</summary>
internal sealed record PairResultPayload(bool Ok, string? DeviceId, string? DeviceName, string? Error);

/// <summary>Payload for <c>image.get</c> and <c>image.copy</c>.</summary>
internal sealed record ImageRequest(string ItemId);

/// <summary>Body for <c>image.get</c>: a data URL the interface can render directly.</summary>
internal sealed record ImagePayload(string? DataUrl);