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
