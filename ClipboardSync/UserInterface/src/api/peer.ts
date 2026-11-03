import {Native} from "@photino-net-utils/bridge";

export type TransferStatus = "synced" | "transferring" | "waiting" | "failed" | "received";
export type TransferDirection = "out" | "in";
export type ClipboardKind = "text" | "link" | "image" | "file";

export interface ClipboardFile {
    path: string;
    size: number;
    directory: boolean;
}

export interface ClipboardItem {
    id: string;
    originDeviceId: string;
    originDeviceName: string;
    kind: ClipboardKind;
    content: string;
    meta?: string | null;
    at: number;
    direction: TransferDirection;
    status: TransferStatus;
    reached: number;
    peers: number;
    files?: ClipboardFile[] | null;
    totalBytes: number;
    progress: number;
}

export interface DeviceInfo {
    id: string;
    name: string;
    self: boolean;
    online: boolean;
    itemCount: number;
}

export interface DiscoveredDevice {
    id: string;
    name: string;
    host: string;
    port: number;
}

export interface DevicesState {
    self: DeviceInfo;
    peers: DeviceInfo[];
    discovered: DiscoveredDevice[];
}

export interface PairCode {
    code: string;
    deviceId: string;
    deviceName: string;
}

export interface IncomingPairRequest {
    requestId: string;
    deviceId: string;
    deviceName: string;
    code: string;
}

export interface PairResult {
    ok: boolean;
    deviceId?: string | null;
    deviceName?: string | null;
    error?: string | null;
}

const EMPTY_DEVICES: DevicesState = {
    self: {id: "", name: "This device", self: true, online: true, itemCount: 0},
    peers: [],
    discovered: [],
};

export async function getDevices(): Promise<DevicesState> {
    if (!Native.available) return EMPTY_DEVICES;

    return await Native.request<DevicesState>("devices.get");
}

export async function getHistory(): Promise<ClipboardItem[]> {
    if (!Native.available) return [];

    const payload = await Native.request<{ items: ClipboardItem[] }>("history.get");
    return payload.items ?? [];
}

const imageCache = new Map<string, string>();

/** Fetches an image preview as a data URL; the backend reads the stored PNG on demand. */
export async function getImagePreview(itemId: string): Promise<string | null> {
    const cached = imageCache.get(itemId);
    if (cached) return cached;
    if (!Native.available) return null;

    const payload = await Native.request<{ dataUrl?: string | null }>("image.get", {itemId});
    if (!payload.dataUrl) return null;

    if (imageCache.size >= 20) {
        const oldest = imageCache.keys().next().value;
        if (oldest) imageCache.delete(oldest);
    }

    imageCache.set(itemId, payload.dataUrl);
    return payload.dataUrl;
}

/** Puts a stored image back on this device's clipboard. */
export async function copyImage(itemId: string): Promise<void> {
    if (!Native.available) return;
    await Native.request("image.copy", {itemId});
}

/** Puts a stored file selection back on this device's clipboard. */
export async function copyFiles(itemId: string): Promise<void> {
    if (!Native.available) return;
    await Native.request("file.copy", {itemId});
}

export async function startDiscovery(): Promise<void> {
    if (!Native.available) return;
    await Native.request("discovery.start");
}

export async function stopDiscovery(): Promise<void> {
    if (!Native.available) return;
    await Native.request("discovery.stop");
}

export async function startPairing(host: string, port: number): Promise<void> {
    if (!Native.available) return;
    await Native.request("pair.start", {host, port});
}

export async function respondToPairing(requestId: string, accept: boolean): Promise<void> {
    if (!Native.available) return;
    await Native.request("pair.respond", {requestId, accept});
}

export async function removeDevice(deviceId: string): Promise<void> {
    if (!Native.available) return;
    await Native.request("device.remove", {deviceId});
}

/** Clears the local board on this device and asks paired devices to clear theirs too. */
export async function clearHistory(): Promise<void> {
    if (!Native.available) return;
    await Native.request("history.clear");
}

function onEvent<T>(type: string, handler: (payload: T) => void): () => void {
    if (!Native.available) return () => undefined;

    return Native.on((message) => {
        if (message.type === type && message.data) handler(message.data as T);
    });
}

export function onDevicesChanged(handler: (devices: DevicesState) => void): () => void {
    return onEvent("devices.changed", handler);
}

export function onHistoryChanged(handler: (items: ClipboardItem[]) => void): () => void {
    return onEvent<{ items: ClipboardItem[] }>("history.changed", (payload) => handler(payload.items ?? []));
}

export function onPairCode(handler: (payload: PairCode) => void): () => void {
    return onEvent("pair.code", handler);
}

export function onPairRequest(handler: (payload: IncomingPairRequest) => void): () => void {
    return onEvent("pair.request", handler);
}

export function onPairResult(handler: (payload: PairResult) => void): () => void {
    return onEvent("pair.result", handler);
}
