import {Native} from "@photino-net-utils/bridge";
import {readBlock} from "./data";

export type TransferStatus =
    | "synced"
    | "queued"
    | "sending"
    | "waiting"
    | "retrying"
    | "receiving"
    | "received"
    | "failed";
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
    self: {id: "", name: "this_device", self: true, online: true, itemCount: 0},
    peers: [],
    discovered: [],
};

export async function getDevices(): Promise<DevicesState> {
    if (!Native.available) return EMPTY_DEVICES;

    return await fetchDevices();
}

export async function getHistory(): Promise<ClipboardItem[]> {
    if (!Native.available) return [];

    return await fetchHistory();
}

let dataBaseTask: Promise<string> | null = null;

/** The local server's data prefix, asked once. */
function dataBase(): Promise<string> {
    dataBaseTask ??= Native.request<{ base: string }>("app.data")
        .then((payload) => payload.base ?? "")
        .catch(() => "");

    return dataBaseTask;
}

/**
 * A loopback URL for a stored picture. The page takes it with its own connection pool and cache, so
 * a screenshot never travels through the bridge as base64.
 */
export async function getImagePreview(itemId: string): Promise<string | null> {
    if (!Native.available) return null;

    const base = await dataBase();
    return base ? `${base}/image/${encodeURIComponent(itemId)}` : null;
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

/** A change notice without a body: the listener fetches what it needs. */
function onPing(type: string, handler: () => void): () => void {
    if (!Native.available) return () => undefined;

    return Native.on((message) => {
        if (message.type === type) handler();
    });
}

export function onDevicesChanged(handler: (devices: DevicesState) => void): () => void {
    return onPing("devices.changed", () => {
        void fetchDevices().then(handler, () => {
            // The host did not answer; the board on screen stays as it was.
        });
    });
}

export function onHistoryChanged(handler: (items: ClipboardItem[]) => void): () => void {
    return onPing("history.changed", () => {
        void refreshHistory().then(handler, () => {
            // The host did not answer; the next ping asks again.
        });
    });
}

/**
 * The history fetcher for the pings: a transfer pings every 100 ms, and a page that asked again
 * while a fetch was still running would queue a copy of the same list.
 */
let historyInFlight: Promise<ClipboardItem[]> | null = null;

function refreshHistory(): Promise<ClipboardItem[]> {
    historyInFlight ??= fetchHistory().finally(() => {
        historyInFlight = null;
    });

    return historyInFlight;
}

/**
 * Rows are kept exactly as the host sends them, in order and with duplicates: copying the same text
 * twice is two entries in the history, not one.
 */
async function fetchHistory(): Promise<ClipboardItem[]> {
    const block = await readBlock("/history");

    return block.rows.map((row) => ({
        id: String(row.id),
        originDeviceId: String(row.originDeviceId),
        originDeviceName: String(row.originDeviceName),
        kind: String(row.kind) as ClipboardKind,
        content: String(row.content),
        meta: String(row.meta ?? "") || null,
        at: Number(row.at),
        direction: String(row.direction) as TransferDirection,
        status: String(row.status) as TransferStatus,
        reached: Number(row.reached),
        peers: Number(row.peers),
        files: toFiles(String(row.files ?? "")),
        totalBytes: Number(row.totalBytes),
        progress: Number(row.progress),
    }));
}

/** The files of a file item travel as one string: path, size and directory flag, unit separated. */
function toFiles(value: string): ClipboardFile[] | null {
    if (!value) return null;

    return value.split("\u001e").map((entry) => {
        const [path, size, directory] = entry.split("\u001f");

        return {
            path,
            size: Number(size ?? 0),
            directory: directory === "1",
        };
    });
}

async function fetchDevices(): Promise<DevicesState> {
    const block = await readBlock("/devices");
    const rows = block.rows;

    const self = rows.find((row) => row.scope === "self");
    const peers = rows.filter((row) => row.scope === "peer");
    const discovered = rows.filter((row) => row.scope === "discovered");

    return {
        self: self ? toDevice(self) : EMPTY_DEVICES.self,
        peers: peers.map(toDevice),
        discovered: discovered.map((row) => ({
            id: String(row.id),
            name: String(row.name),
            host: String(row.host),
            port: Number(row.port),
        })),
    };
}

function toDevice(row: Record<string, string | number>): DeviceInfo {
    return {
        id: String(row.id),
        name: String(row.name),
        self: Number(row.self) !== 0,
        online: Number(row.online) !== 0,
        itemCount: Number(row.items),
    };
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
