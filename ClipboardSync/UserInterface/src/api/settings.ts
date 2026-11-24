import {loadSettings, Native, saveSettings as save} from "@photino-net-utils/bridge";
import type {AppSettingsGenerated} from "./settings.generated";

/** Mirrors `ClipboardSync.Models.AppSettings`; generated from the C# model. */
export type Settings = AppSettingsGenerated;

/**
 * Reads the settings. Every default belongs to the host, so nothing here repeats one; a browser
 * preview has no settings file and gets the page's own "could not read" state instead.
 */
export async function getSettings(): Promise<Settings> {
    return await loadSettings<Settings>();
}

export async function saveSettings(settings: Settings): Promise<Settings> {
    return await save<Settings>(settings);
}

/** Fires when the settings were saved, so every open view can pick the new values up. */
export function onSettingsChanged(handler: (settings: Settings) => void): () => void {
    if (!Native.available) return () => undefined;

    return Native.on((message) => {
        if (message.type === "settings.changed" && message.data) handler(message.data as Settings);
    });
}
