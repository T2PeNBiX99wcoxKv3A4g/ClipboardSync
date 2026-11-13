import {useTranslation, type Translate} from "@photino-net-utils/bridge";
import type {LocalizationKey} from "./api/i18n.generated";

export type {LocalizationKey};

/** The shared translator narrowed to the keys generated from en.jsonc. */
export const useAppTranslation = (): Translate<LocalizationKey> => useTranslation<LocalizationKey>();
