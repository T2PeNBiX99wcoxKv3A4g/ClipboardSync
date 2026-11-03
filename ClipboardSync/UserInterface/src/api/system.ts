import {Native} from "@photino-net-utils/bridge";

export interface AppInfo {
    version: string;
    platform: string;
    /** The folder the app writes its own log files to. */
    logDirectory: string;
    /** True when the interface has to paint the window's rounded corners itself (Linux). */
    rounded: boolean;
    /** True in a debug build. */
    debug: boolean;
}

/** Reads the app version declared in the csproj, plus the host platform id and where logs live. */
export async function getAppInfo(): Promise<AppInfo> {
    if (!Native.available)
        return {version: "0.1.0", platform: "web", logDirectory: "", rounded: false, debug: false};

    return await Native.request<AppInfo>("app.info");
}

export interface StartupStatus {
    /** False on platforms with no per-user Startup folder. */
    supported: boolean;
    enabled: boolean;
}

/** Reports whether ClipboardSync starts with the user's session. */
export async function startupStatus(): Promise<StartupStatus> {
    if (!Native.available) return {supported: false, enabled: false};

    return await Native.request<StartupStatus>("app.startup");
}

/** Adds or removes the per-user autostart entry. */
export async function setStartup(enabled: boolean): Promise<StartupStatus> {
    if (!Native.available) return {supported: false, enabled};
    return await Native.request<StartupStatus>("app.setStartup", {enabled});
}

/** Opens the folder ClipboardSync writes its own log files to. */
export async function openLogsFolder(): Promise<void> {
    if (!Native.available) return;

    await Native.request("app.openLogs");
}
