export {
    cancelUpdateDownload,
    checkForUpdates,
    getAppInfo,
    getUpdateStatus,
    installUpdate,
    onUpdateChangelog,
    onUpdateProgress,
    openLogsFolder,
    openReleasesPage,
    setStartup,
    startupStatus,
    startUpdateDownload,
} from "@photino-net-utils/bridge";

export type {
    AppInfo,
    StartupStatus,
    UpdateAck,
    UpdateChangelog,
    UpdateCheck,
    UpdatePhase,
    UpdateProgress,
} from "@photino-net-utils/bridge";

/** Used only in a browser preview; the desktop host owns the real release URL. */
export const RELEASES_URL = "https://github.com/T2PeNBiX99wcoxKv3A4g/ClipboardSync/releases/latest";
