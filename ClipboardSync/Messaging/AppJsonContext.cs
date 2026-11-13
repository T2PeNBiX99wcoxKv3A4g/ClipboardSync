using System.Text.Json;
using System.Text.Json.Serialization;
using ClipboardSync.Models;
using ClipboardSync.Security;
using PhotinoNetUtils.Localization;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Services;
using PhotinoNetUtils.Updates;

namespace ClipboardSync.Messaging;

/// <summary>
///     Source-generated serializer context. The Release build is trimmed, which turns off
///     reflection-based serialization, so every shape that crosses the native bridge must be
///     registered here.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(NativeRequest))]
[JsonSerializable(typeof(NativeResponse))]
[JsonSerializable(typeof(NativeEvent))]
[JsonSerializable(typeof(LocaleRequest))]
[JsonSerializable(typeof(LocalePayload))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(OkPayload))]
[JsonSerializable(typeof(AppInfoPayload))]
[JsonSerializable(typeof(UpdateCheckPayload))]
[JsonSerializable(typeof(UpdateAckPayload))]
[JsonSerializable(typeof(UpdateProgressPayload))]
[JsonSerializable(typeof(UpdateChangelogPayload))]
[JsonSerializable(typeof(SettingsPayload))]
[JsonSerializable(typeof(StartupRequest))]
[JsonSerializable(typeof(StartupPayload))]
[JsonSerializable(typeof(WindowStatePayload))]
[JsonSerializable(typeof(WindowResizeRequest))]
[JsonSerializable(typeof(AppNavigatePayload))]
[JsonSerializable(typeof(OpenUrlRequest))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(PairedDevice))]
[JsonSerializable(typeof(PairedDevice[]))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(ClipboardRecord))]
[JsonSerializable(typeof(ClipboardRecord[]))]
[JsonSerializable(typeof(ClipboardFileEntry))]
[JsonSerializable(typeof(ClipboardFileEntry[]))]
[JsonSerializable(typeof(DiscoveredDevicePayload))]
[JsonSerializable(typeof(DiscoveredDevicePayload[]))]
[JsonSerializable(typeof(DevicePayload))]
[JsonSerializable(typeof(DevicePayload[]))]
[JsonSerializable(typeof(DevicesPayload))]
[JsonSerializable(typeof(ClipboardItemPayload))]
[JsonSerializable(typeof(ClipboardItemPayload[]))]
[JsonSerializable(typeof(HistoryPayload))]
[JsonSerializable(typeof(PairStartRequest))]
[JsonSerializable(typeof(PairRespondRequest))]
[JsonSerializable(typeof(DeviceRemoveRequest))]
[JsonSerializable(typeof(PairCodePayload))]
[JsonSerializable(typeof(PairRequestPayload))]
[JsonSerializable(typeof(PairResultPayload))]
[JsonSerializable(typeof(ImageRequest))]
[JsonSerializable(typeof(ImagePayload))]
[JsonSerializable(typeof(FileItemRequest))]
[JsonSerializable(typeof(DiscoveryMessage))]
[JsonSerializable(typeof(ClipboardItemMessage))]
[JsonSerializable(typeof(HistoryClearMessage))]
[JsonSerializable(typeof(FileBlobRequestMessage))]
[JsonSerializable(typeof(FileBlobChunkMessage))]
[JsonSerializable(typeof(FileBlobErrorMessage))]
[JsonSerializable(typeof(ClipboardAckMessage))]
[JsonSerializable(typeof(PairRequestMessage))]
[JsonSerializable(typeof(PairChallengeMessage))]
[JsonSerializable(typeof(PairAcceptMessage))]
[JsonSerializable(typeof(PairRejectMessage))]
[JsonSerializable(typeof(SessionHelloMessage))]
[JsonSerializable(typeof(SessionReplyMessage))]
[JsonSerializable(typeof(SecureDataMessage))]
[JsonSerializable(typeof(SecureStatusMessage))]
internal sealed partial class AppJsonContext : JsonSerializerContext;