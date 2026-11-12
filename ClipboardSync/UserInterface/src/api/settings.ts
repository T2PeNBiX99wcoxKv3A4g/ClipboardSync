import {Native} from "@photino-net-utils/bridge";
import type {AppSettingsGenerated} from "./settings.generated";

/** Mirrors `ClipboardSync.Models.AppSettings`; generated from the C# model. */
export type Settings = AppSettingsGenerated;

/** Used in a browser preview, where there is no settings file to read. */
export const PREVIEW_SETTINGS: Settings = {
    deviceName: "This device",
    language: "system",
    startHidden: false,
    syncEnabled: true,
    syncText: true,
    syncImages: true,
    syncFiles: true,
    historyLimit: 200,
    maxImageMb: 10,
    maxFileMb: 512,
    blobDirectory: "",
};

export async function getSettings(): Promise<Settings> {
    if (!Native.available) return PREVIEW_SETTINGS;

    return await Native.request<Settings>("settings.get");
}

export async function saveSettings(settings: Settings): Promise<Settings> {
    if (!Native.available) return settings;

    return await Native.request<Settings>("settings.save", settings);
}

/** Fires when the settings were saved, so every open view can pick the new values up. */
export function onSettingsChanged(handler: (settings: Settings) => void): () => void {
    if (!Native.available) return () => undefined;

    return Native.on((message) => {
        if (message.type === "settings.changed" && message.data) handler(message.data as Settings);
    });
}
