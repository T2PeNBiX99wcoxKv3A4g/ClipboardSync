import type {SiteCopy, SiteLanguage} from "@photino-net-utils/site";
import en from "./i18n/en.json";
import ja from "./i18n/ja.json";
import zhCN from "./i18n/zh-CN.json";
import zhTW from "./i18n/zh-TW.json";

export const languages: SiteLanguage[] = [
    {id: "en", label: "English"},
    {id: "ja", label: "日本語"},
    {id: "zh-CN", label: "简体中文"},
    {id: "zh-TW", label: "繁體中文"},
];

/** The site's own text, one JSON file per language under `src/i18n/`. */
export const copy: Record<string, SiteCopy> = {
    en,
    ja,
    "zh-CN": zhCN,
    "zh-TW": zhTW,
};
