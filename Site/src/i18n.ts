/**
 * The site's own text, one JSON file per language under `src/i18n/`.
 */
import en from "./i18n/en.json";
import ja from "./i18n/ja.json";
import zhCN from "./i18n/zh-CN.json";
import zhTW from "./i18n/zh-TW.json";

export const LANGUAGES = [
    {id: "en", label: "English"},
    {id: "ja", label: "日本語"},
    {id: "zh-CN", label: "简体中文"},
    {id: "zh-TW", label: "繁體中文"}
] as const;

export type Language = (typeof LANGUAGES)[number]["id"];

export const DEFAULT_LANGUAGE: Language = "en";

export interface Copy {
    nav: { home: string; docs: string };
    /** Label for the release badge, read out with the version number. */
    version: string;
    footer: string;
    home: {
        tagline: string;
        noteTitle: string;
        noteText: string;
        download: string;
        documentation: string;
        whatTitle: string;
        whatText: string;
        featuresTitle: string;
        features: { name: string; text: string }[];
        howTitle: string;
        how: string[];
        installTitle: string;
        /** Text before the README link, the link label, and the text after it. */
        installBefore: string;
        readme: string;
        installAfter: string;
        platforms: { name: string; note: string }[];
    };
    docs: { setup: string; reference: string };
}

export const COPY: Record<Language, Copy> = {
    en,
    ja,
    "zh-CN": zhCN,
    "zh-TW": zhTW
};
