import React, {createContext, useContext, useEffect, useState} from "react";
import {COPY, DEFAULT_LANGUAGE, LANGUAGES, type Copy, type Language} from "./i18n";

const STORAGE_KEY = "clipboardsync.site.language";

/** The language picked last time, or English when nothing (valid) is stored. */
function stored(): Language {
    const value = window.localStorage.getItem(STORAGE_KEY);

    return LANGUAGES.some((language) => language.id === value) ? (value as Language) : DEFAULT_LANGUAGE;
}

interface LanguageValue {
    language: Language;
    setLanguage: (language: Language) => void;
}

const LanguageContext = createContext<LanguageValue>({language: DEFAULT_LANGUAGE, setLanguage: () => {}});

export const LanguageProvider: React.FC<{ children: React.ReactNode }> = ({children}) => {
    const [language, setLanguage] = useState<Language>(stored);

    useEffect(() => {
        window.localStorage.setItem(STORAGE_KEY, language);
        document.documentElement.lang = language;
    }, [language]);

    return <LanguageContext.Provider value={{language, setLanguage}}>{children}</LanguageContext.Provider>;
};

export const useLanguage = () => useContext(LanguageContext);

export const useCopy = (): Copy => COPY[useLanguage().language];
