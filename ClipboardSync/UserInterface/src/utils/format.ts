/** "just now", "3 minutes ago" … in the user's language, without a per-language string table. */
export function relativeTime(at: number, locale: string): string {
    const seconds = Math.round((at - Date.now()) / 1000);
    const format = new Intl.RelativeTimeFormat(locale, {numeric: "auto"});

    if (Math.abs(seconds) < 60) return format.format(seconds, "second");

    const minutes = Math.round(seconds / 60);
    if (Math.abs(minutes) < 60) return format.format(minutes, "minute");

    const hours = Math.round(minutes / 60);
    if (Math.abs(hours) < 24) return format.format(hours, "hour");

    return format.format(Math.round(hours / 24), "day");
}

export function formatSize(bytes: number): string {
    if (bytes <= 0) return "0 KB";
    if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(1)} GB`;
    if (bytes >= 1024 ** 2) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
    return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}
