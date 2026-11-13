import {useEffect, useRef, useState} from "react";
import styled, {css, keyframes} from "styled-components";
import {useTranslation} from "@photino-net-utils/bridge";
import {
    type AppInfo,
    checkForUpdates,
    getUpdateStatus,
    openLogsFolder,
    openReleasePage,
    setStartup,
    startupStatus,
    type UpdateCheck
} from "../api/system";
import {getSettings, saveSettings, type Settings} from "../api/settings";
import {clearHistory} from "../api/peer";
import {Toggle} from "../components/Toggle";
import {Dropdown} from "../components/Dropdown";
import {CheckIcon, FolderIcon, InfoIcon} from "../components/Icons";

interface SettingsPageProps {
    info: AppInfo | null;
    /** A section the footer's update dot asked for: <c>updates</c>, <c>about</c>, etc. */
    focusSection?: string | null;
    onSectionFocused?: () => void;
    /** Downloads the installer and opens the progress dialog. */
    onInstallUpdate: () => void;
}

type SectionId = "general" | "sync" | "history" | "updates" | "about";

const sections: { id: SectionId; label: string }[] = [
    {id: "general", label: "General"},
    {id: "sync", label: "Sync"},
    {id: "history", label: "History"},
    {id: "updates", label: "Updates"},
    {id: "about", label: "About"},
];

const languages = [
    {id: "system", label: "System default"},
    {id: "en", label: "English"},
    {id: "ja", label: "日本語"},
    {id: "zh-CN", label: "简体中文"},
    {id: "zh-TW", label: "繁體中文"},
];

export function SettingsPage({info, focusSection, onSectionFocused, onInstallUpdate}: SettingsPageProps) {
    const t = useTranslation();
    const languageOptions = languages.map((language) => ({
        ...language,
        label: language.id === "system" ? t("System default") : language.label,
    }));
    const [settings, setSettings] = useState<Settings | null>(null);
    const [saved, setSaved] = useState<Settings | null>(null);
    const [startup, setStartupState] = useState<{ supported: boolean; enabled: boolean } | null>(null);
    const [saving, setSaving] = useState(false);
    const [notice, setNotice] = useState<{ ok: boolean; text: string } | null>(null);
    const [updateState, setUpdateState] = useState<UpdateCheck | null>(null);
    const [updateBusy, setUpdateBusy] = useState(false);
    const [updateError, setUpdateError] = useState(false);
    const [confirmClear, setConfirmClear] = useState(false);
    const [clearing, setClearing] = useState(false);
    const [activeSection, setActiveSection] = useState<SectionId>("general");
    const pageRef = useRef<HTMLDivElement>(null);
    const sectionRefs = useRef<Record<SectionId, HTMLElement | null>>({
        general: null,
        sync: null,
        history: null,
        updates: null,
        about: null,
    });
    const sectionNavReady = settings !== null;

    useEffect(() => {
        if (!sectionNavReady) return;

        const scroller = pageRef.current?.parentElement;
        if (!scroller) return;

        const updateActive = () => {
            const scrollerTop = scroller.getBoundingClientRect().top;
            const canScroll = scroller.scrollHeight > scroller.clientHeight + 8;
            const atBottom = canScroll &&
                scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 8;

            if (atBottom) {
                setActiveSection(sections[sections.length - 1].id);
                return;
            }

            const threshold = scrollerTop + 120;
            let current: SectionId = "general";

            for (const {id} of sections) {
                const element = sectionRefs.current[id];
                if (element && element.getBoundingClientRect().top <= threshold)
                    current = id;
            }

            setActiveSection(current);
        };

        updateActive();
        scroller.addEventListener("scroll", updateActive, {passive: true});
        window.addEventListener("resize", updateActive);

        return () => {
            scroller.removeEventListener("scroll", updateActive);
            window.removeEventListener("resize", updateActive);
        };
    }, [sectionNavReady]);

    useEffect(() => {
        let cancelled = false;

        void getSettings().then((next) => {
            if (cancelled) return;
            setSettings(next);
            setSaved(next);
        }, () => undefined);

        void startupStatus().then((next) => {
            if (!cancelled) setStartupState(next);
        }, () => undefined);

        void getUpdateStatus().then((next) => {
            if (!cancelled) setUpdateState(next);
        }, () => undefined);

        return () => {
            cancelled = true;
        };
    }, []);

    const update = <K extends keyof Settings>(key: K, value: Settings[K]) =>
        setSettings((current) => current ? {...current, [key]: value} : current);

    // The language is applied by the backend immediately; the other fields still need Save.
    const changeLanguage = (language: string) => {
        update("language", language);

        if (!settings || !saved) return;

        void saveSettings({...saved, language}).then((next) => {
            setSaved(next);
            setSettings((current) => current ? {...current, language: next.language} : current);
        }, () => {
            setNotice({ok: false, text: t("The host did not answer.")});
        });
    };

    const dirty = settings !== null && saved !== null && JSON.stringify(settings) !== JSON.stringify(saved);

    const toggleStartup = (enabled: boolean) => {
        setStartupState((current) => current ? {...current, enabled} : current);

        void setStartup(enabled).then(setStartupState, () => {
            setNotice({ok: false, text: t("The host did not answer.")});
        });
    };

    const save = () => {
        if (!settings || saving) return;

        setSaving(true);
        void saveSettings(settings).then((next) => {
            setSettings(next);
            setSaved(next);
            setNotice({ok: true, text: t("Settings saved.")});
        }, (error) => {
            setNotice({ok: false, text: error instanceof Error ? error.message : t("The host did not answer.")});
        }).finally(() => {
            setSaving(false);
            window.setTimeout(() => setNotice(null), 4000);
        });
    };

    const restoreDefaults = () => {
        if (!settings) return;

        setSettings({
            // Keep the device name: it identifies this device to peers, not a preference.
            deviceName: settings.deviceName,
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
        });
    };

    const discard = () => {
        if (!saved) return;
        setSettings(saved);
        setNotice(null);
    };

    const jumpTo = (id: SectionId) => {
        const element = sectionRefs.current[id];
        if (!element) return;

        setActiveSection(id);
        element.scrollIntoView({behavior: "smooth", block: "start"});
    };

    // The footer's update dot opens Settings with a target section. Wait until the settings have
    // loaded, because the sections do not exist during the loading state.
    useEffect(() => {
        if (!focusSection || !settings) return;

        const target = sections.find((section) => section.id === focusSection);
        if (target) jumpTo(target.id);

        onSectionFocused?.();
    }, [focusSection, settings]);

    const checkUpdate = () => {
        setUpdateBusy(true);
        setUpdateError(false);
        void checkForUpdates().then((next) => {
            setUpdateState(next);
        }, () => {
            setUpdateError(true);
        }).finally(() => {
            setUpdateBusy(false);
        });
    };

    const clear = () => {
        setClearing(true);
        void clearHistory().then(() => {
            setConfirmClear(false);
            setNotice({ok: true, text: t("History cleared.")});
        }, (error) => {
            setNotice({ok: false, text: error instanceof Error ? error.message : t("The host did not answer.")});
        }).finally(() => {
            setClearing(false);
            window.setTimeout(() => setNotice(null), 4000);
        });
    };

    if (!settings) {
        return <Loading>{t("Loading settings…")}</Loading>;
    }

    return (
        <Page ref={pageRef}>
            <Main>
                <Section id="settings-general" ref={(node) => {
                    sectionRefs.current.general = node;
                }}>
                    <SectionHead>
                        <h2>{t("General")}</h2>
                        <p>{t("How ClipboardSync looks and starts.")}</p>
                    </SectionHead>
                    <Card>
                        <Field>
                            <FieldText>
                                <label htmlFor="device-name">{t("Device name")}</label>
                                <p>{t("The name the other devices see.")}</p>
                            </FieldText>
                            <TextInput
                                id="device-name"
                                maxLength={48}
                                onChange={(event) => update("deviceName", event.target.value)}
                                value={settings.deviceName}
                            />
                        </Field>

                        <Field>
                            <FieldText>
                                <label htmlFor="language">{t("Language")}</label>
                                <p>{t("Applies to the interface and the tray menu.")}</p>
                            </FieldText>
                            <Dropdown
                                label={t("Language")}
                                onChange={changeLanguage}
                                options={languageOptions}
                                value={settings.language}
                            />
                        </Field>

                        <Field>
                            <FieldText>
                                <label>{t("Start with the window hidden")}</label>
                                <p>{t("Keep ClipboardSync in the tray when it launches.")}</p>
                            </FieldText>
                            <Toggle
                                checked={settings.startHidden}
                                label={t("Start with the window hidden")}
                                onChange={(value) => update("startHidden", value)}
                            />
                        </Field>

                        <Field>
                            <FieldText>
                                <label>{t("Launch at startup")}</label>
                                <p>
                                    {startup?.supported === false
                                        ? t("This platform has no per-user startup entry.")
                                        : t("Start ClipboardSync when you sign in.")}
                                </p>
                            </FieldText>
                            <Toggle
                                checked={startup?.enabled ?? false}
                                disabled={!startup?.supported}
                                label={t("Launch at startup")}
                                onChange={toggleStartup}
                            />
                        </Field>
                    </Card>
                </Section>

                <Section id="settings-sync" ref={(node) => {
                    sectionRefs.current.sync = node;
                }}>
                    <SectionHead>
                        <h2>{t("Sync")}</h2>
                        <p>{t("What this device shares with its paired devices.")}</p>
                    </SectionHead>
                    <Card>
                        <Field>
                            <FieldText>
                                <label>{t("Enable syncing")}</label>
                                <p>{t("Turn off to stay paired but stop sending and receiving.")}</p>
                            </FieldText>
                            <Toggle
                                checked={settings.syncEnabled}
                                label={t("Enable syncing")}
                                onChange={(value) => update("syncEnabled", value)}
                            />
                        </Field>
                        <Field>
                            <FieldText>
                                <label>{t("Text and links")}</label>
                                <p>{t("Copying text or a URL sends it to the other devices.")}</p>
                            </FieldText>
                            <Toggle
                                checked={settings.syncText}
                                disabled={!settings.syncEnabled}
                                label={t("Text and links")}
                                onChange={(value) => update("syncText", value)}
                            />
                        </Field>
                        <Field>
                            <FieldText>
                                <label>{t("Images")}</label>
                                <p>{t("Copying an image sends it to the other devices.")}</p>
                            </FieldText>
                            <Toggle
                                checked={settings.syncImages}
                                disabled={!settings.syncEnabled}
                                label={t("Images")}
                                onChange={(value) => update("syncImages", value)}
                            />
                        </Field>
                        <Field>
                            <FieldText>
                                <label>{t("Files and folders")}</label>
                                <p>{t("Copying files or folders sends them to the other devices.")}</p>
                            </FieldText>
                            <Toggle
                                checked={settings.syncFiles}
                                disabled={!settings.syncEnabled}
                                label={t("Files and folders")}
                                onChange={(value) => update("syncFiles", value)}
                            />
                        </Field>
                        <Field>
                            <FieldText>
                                <label htmlFor="image-limit">{t("Image size limit")}</label>
                                <p>{t("Images larger than this are skipped.")}</p>
                            </FieldText>
                            <SizeRow>
                                <NumberInput
                                    id="image-limit"
                                    max={32}
                                    min={1}
                                    onChange={(event) => update("maxImageMb", Number(event.target.value) || 1)}
                                    type="number"
                                    value={settings.maxImageMb}
                                />
                                <Unit>MB</Unit>
                            </SizeRow>
                        </Field>
                        <Field>
                            <FieldText>
                                <label htmlFor="file-limit">{t("File size limit")}</label>
                                <p>{t("One copied selection larger than this is skipped.")}</p>
                            </FieldText>
                            <SizeRow>
                                <NumberInput
                                    id="file-limit"
                                    max={4096}
                                    min={1}
                                    onChange={(event) => update("maxFileMb", Number(event.target.value) || 1)}
                                    type="number"
                                    value={settings.maxFileMb}
                                />
                                <Unit>MB</Unit>
                            </SizeRow>
                        </Field>
                        <Field>
                            <FieldText>
                                <label htmlFor="blob-directory">{t("Blob storage folder")}</label>
                                <p>{t("Leave empty to use the app data folder. Changing it moves existing blobs.")}</p>
                            </FieldText>
                            <PathInput
                                id="blob-directory"
                                onChange={(event) => update("blobDirectory", event.target.value)}
                                placeholder={t("Default")}
                                value={settings.blobDirectory}
                            />
                        </Field>
                    </Card>
                    <Note>{t("Images are shared as PNG; files and folders are copied recursively.")}</Note>
                </Section>

                <Section id="settings-history" ref={(node) => {
                    sectionRefs.current.history = node;
                }}>
                    <SectionHead>
                        <h2>{t("History")}</h2>
                        <p>{t("The local record of clipboard items.")}</p>
                    </SectionHead>
                    <Card>
                        <Field>
                            <FieldText>
                                <label>{t("Clear history")}</label>
                                <p>{t("This only clears history; the system clipboard is not changed.")}</p>
                            </FieldText>
                            {!confirmClear ? (
                                <GhostButton disabled={clearing} onClick={() => setConfirmClear(true)} type="button">
                                    {t("Clear history")}
                                </GhostButton>
                            ) : (
                                <Actions>
                                    <GhostButton disabled={clearing} onClick={() => setConfirmClear(false)}
                                                 type="button">
                                        {t("Cancel")}
                                    </GhostButton>
                                    <DangerButton disabled={clearing} onClick={clear} type="button">
                                        {clearing ? t("Clearing…") : t("Clear")}
                                    </DangerButton>
                                </Actions>
                            )}
                        </Field>
                        {confirmClear && (
                            <ClearConfirm>
                                <ClearText>
                                    <strong>{t("Clear history on all paired devices?")}</strong>
                                </ClearText>
                            </ClearConfirm>
                        )}
                        <Field>
                            <FieldText>
                                <label htmlFor="history-limit">{t("Items to keep")}</label>
                                <p>{t("Older items are removed once the limit is reached.")}</p>
                            </FieldText>
                            <NumberInput
                                id="history-limit"
                                max={5000}
                                min={10}
                                onChange={(event) => update("historyLimit", Number(event.target.value) || 10)}
                                type="number"
                                value={settings.historyLimit}
                            />
                        </Field>
                    </Card>
                </Section>

                <Section id="settings-updates" ref={(node) => {
                    sectionRefs.current.updates = node;
                }}>
                    <SectionHead>
                        <h2>{t("Updates")}</h2>
                        <p>{t("Check for a newer release and open its download page.")}</p>
                    </SectionHead>
                    <UpdateCard $available={updateState?.available ?? false}>
                        <UpdateSummary>
                            <UpdateStatus $available={updateState?.available ?? false}>
                                <StatusDot $available={updateState?.available ?? false}/>
                                <span>
                                {updateError
                                    ? t("The update check failed.")
                                    : updateState
                                        ? updateState.available
                                            ? t("A newer version is available: v{version}", {version: updateState.latest})
                                            : t("You are running the latest version.")
                                        : t("Check the published version.")}
                            </span>
                            </UpdateStatus>
                            <UpdateCurrent>
                                {info ? `v${info.version} · ${info.platform}` : t("browser preview")}
                            </UpdateCurrent>
                        </UpdateSummary>
                        <UpdateActions>
                            <GhostButton disabled={updateBusy} onClick={checkUpdate} type="button">
                                {updateBusy ? t("Checking…") : t("Check for updates")}
                            </GhostButton>
                            {updateState?.available && info?.platform === "windows" && (
                                <ReleaseButton onClick={onInstallUpdate} type="button">
                                    {t("Download and install")}
                                </ReleaseButton>
                            )}
                            {updateState?.available && (
                                <GhostButton onClick={() => void openReleasePage().catch(() => undefined)}
                                             type="button">
                                    {t("Open release page")}
                                </GhostButton>
                            )}
                        </UpdateActions>
                    </UpdateCard>
                </Section>

                <Section id="settings-about" ref={(node) => {
                    sectionRefs.current.about = node;
                }}>
                    <SectionHead>
                        <h2>{t("About")}</h2>
                        <p>{t("The build running on this device.")}</p>
                    </SectionHead>
                    <Card>
                        <Field>
                            <FieldText>
                                <label>{t("Version")}</label>
                                <p>{info ? `${info.version} · ${info.platform}` : t("browser preview")}</p>
                            </FieldText>
                            <InfoIcon size={15}/>
                        </Field>
                        <Field>
                            <FieldText>
                                <label>{t("Logs")}</label>
                                <p>{info?.logDirectory || t("Only available in the desktop app.")}</p>
                            </FieldText>
                            <GhostButton onClick={() => void openLogsFolder().catch(() => undefined)} type="button">
                                <FolderIcon size={13}/>
                                {t("Open logs")}
                            </GhostButton>
                        </Field>
                    </Card>
                </Section>

                <SaveBar>
                    <Status>
                        {notice && (
                            <Notice $ok={notice.ok}>
                                {notice.ok && <CheckIcon size={12}/>}
                                {notice.text}
                            </Notice>
                        )}
                        {!notice && (dirty ? t("Unsaved changes") : t("All changes saved"))}
                    </Status>
                    <Actions>
                        <GhostAction disabled={saving} onClick={restoreDefaults} type="button">
                            {t("Restore defaults")}
                        </GhostAction>
                        {dirty && (
                            <DiscardAction disabled={saving} onClick={discard} type="button">
                                {t("Discard changes")}
                            </DiscardAction>
                        )}
                        <SaveButton disabled={!dirty || saving} onClick={save} type="button">
                            {saving ? t("Saving…") : t("Save changes")}
                        </SaveButton>
                    </Actions>
                </SaveBar>
            </Main>

            <Toc aria-label={t("Sections")}>
                <TocLabel>{t("Sections")}</TocLabel>
                <TocList>
                    {sections.map(({id, label}) => (
                        <TocItem
                            $active={activeSection === id}
                            key={id}
                            onClick={() => jumpTo(id)}
                            type="button"
                        >
                            <span>{t(label)}</span>
                        </TocItem>
                    ))}
                </TocList>
            </Toc>
        </Page>
    );
}

const Page = styled.div`
    display: flex;
    align-items: flex-start;
    gap: 24px;
    padding-bottom: 72px;
`;

const Main = styled.div`
    display: flex;
    flex: 1;
    flex-direction: column;
    gap: 16px;
    min-width: 0;
`;

const Section = styled.section`
    display: flex;
    flex-direction: column;
    gap: 8px;
    scroll-margin-top: 12px;
`;

const Toc = styled.nav`
    position: sticky;
    top: 4px;
    flex-shrink: 0;
    width: 132px;
    padding: 4px 0 8px;
`;

const TocLabel = styled.span`
    display: block;
    margin-bottom: 8px;
    padding-left: 14px;
    font-size: 8.5px;
    font-weight: 600;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: var(--text-muted);
`;

const TocList = styled.div`
    position: relative;
    display: flex;
    flex-direction: column;
    gap: 2px;

    &::before {
        content: "";
        position: absolute;
        top: 12px;
        bottom: 12px;
        left: 5px;
        width: 1px;
        background: var(--border-color);
    }
`;

const TocItem = styled.button<{ $active: boolean }>`
    position: relative;
    display: flex;
    align-items: center;
    padding: 6px 6px 6px 16px;
    border: 0;
    border-radius: var(--radius-sm);
    background: ${({$active}) => $active ? "rgba(255, 138, 61, 0.1)" : "transparent"};
    color: ${({$active}) => $active ? "var(--accent-primary)" : "var(--text-muted)"};
    font: inherit;
    font-size: 10.5px;
    text-align: left;
    white-space: nowrap;
    cursor: pointer;
    transition: color 120ms ease, background 120ms ease;

    &::before {
        content: "";
        position: absolute;
        left: 2px;
        top: 50%;
        width: 7px;
        height: 7px;
        border: 1px solid var(--border-color-hover);
        border-radius: 50%;
        background: var(--bg-secondary);
        transform: translateY(-50%);
        transition: background 120ms ease, border-color 120ms ease, box-shadow 120ms ease;
    }

    ${({$active}) => $active && css`
        &::before {
            border-color: var(--accent-primary);
            background: var(--accent-primary);
            box-shadow: 0 0 0 3px rgba(255, 138, 61, 0.16);
        }
    `}
    &:hover {
        background: var(--bg-card-hover);
        color: var(--text-primary);
    }

    &:focus-visible {
        outline: 2px solid var(--border-color-focus);
        outline-offset: 1px;
    }
`;

const SectionHead = styled.div`
    h2 {
        font-size: 13px;
        font-weight: 600;
    }

    p {
        margin-top: 1px;
        font-size: 10px;
        color: var(--text-muted);
    }
`;

const Card = styled.div`
    border: 1px solid var(--border-color);
    border-radius: var(--radius-md);
    background: var(--bg-card);
`;

const Note = styled.p`
    font-size: 9.5px;
    color: var(--text-muted);
`;

const Field = styled.div`
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 24px;
    padding: 11px 16px;
    border-bottom: 1px solid var(--border-color);

    &:last-child {
        border-bottom: 0;
    }
`;

const FieldText = styled.div`
    display: flex;
    flex-direction: column;
    gap: 1px;
    min-width: 0;

    label {
        font-size: 11.5px;
        color: var(--text-primary);
    }

    p {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font-size: 9.5px;
        color: var(--text-muted);
    }
`;

const TextInput = styled.input`
    width: 220px;
    padding: 6px 9px;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    outline: 0;
    background: var(--bg-input);
    color: var(--text-primary);
    font-size: 11px;

    &:focus {
        border-color: var(--border-color-focus);
    }
`;

const NumberInput = styled(TextInput)`
    width: 92px;
    font-family: var(--font-mono);
`;

const PathInput = styled(TextInput)`
    width: 280px;
    font-family: var(--font-mono);
    font-size: 10px;
`;

const UpdateActions = styled.div`
    display: flex;
    align-items: center;
    gap: 6px;
    flex-shrink: 0;
`;

/** The dot breathes while a newer build is published, so the card reads as actionable. */
const updatePulse = keyframes`
    0%, 100% {
        box-shadow: 0 0 0 3px rgba(255, 138, 61, 0.14), 0 0 8px rgba(255, 138, 61, 0.45);
    }
    50% {
        box-shadow: 0 0 0 4px rgba(255, 138, 61, 0.22), 0 0 14px rgba(255, 138, 61, 0.7);
    }
`;

/** The Updates card lifts out of the page when the published build is newer than this one. */
const UpdateCard = styled.div<{ $available: boolean }>`
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 24px;
    padding: 16px;
    border: 1px solid ${({$available}) => $available ? "rgba(255, 138, 61, 0.55)" : "var(--border-color)"};
    border-radius: var(--radius-md);
    background: ${({$available}) => $available
            ? "linear-gradient(135deg, rgba(255, 138, 61, 0.12), rgba(255, 107, 107, 0.05))"
            : "var(--bg-card)"};
    box-shadow: ${({$available}) => $available
            ? "0 0 0 1px rgba(255, 138, 61, 0.08), 0 8px 24px rgba(255, 138, 61, 0.08)"
            : "none"};
`;

const UpdateSummary = styled.div`
    display: flex;
    flex-direction: column;
    gap: 3px;
    min-width: 0;
`;

const UpdateStatus = styled.div<{ $available: boolean }>`
    display: flex;
    align-items: center;
    gap: 9px;
    font-size: 12.5px;
    font-weight: ${({$available}) => $available ? "600" : "500"};
    color: ${({$available}) => $available ? "var(--text-primary)" : "var(--text-secondary)"};
`;

const StatusDot = styled.span<{ $available: boolean }>`
    width: 8px;
    height: 8px;
    flex-shrink: 0;
    border-radius: 50%;
    background: ${({$available}) => $available ? "var(--accent-primary)" : "var(--text-muted)"};
    animation: ${({$available}) => $available ? updatePulse : "none"} 2s ease-in-out infinite;
`;

const UpdateCurrent = styled.span`
    padding-left: 17px;
    font-family: var(--font-mono);
    font-size: 9.5px;
    color: var(--text-muted);
`;

const SizeRow = styled.div`
    display: flex;
    align-items: center;
    gap: 6px;
`;

const Unit = styled.span`
    font-family: var(--font-mono);
    font-size: 9.5px;
    color: var(--text-muted);
`;

const GhostButton = styled.button`
    display: inline-flex;
    align-items: center;
    gap: 6px;
    flex-shrink: 0;
    padding: 5px 9px;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    background: transparent;
    color: var(--text-secondary);
    font: inherit;
    font-size: 9.5px;
    cursor: pointer;

    &:hover {
        border-color: var(--border-color-hover);
        color: var(--text-primary);
    }
`;

const ReleaseButton = styled(GhostButton)`
    border-color: rgba(255, 138, 61, 0.5);
    background: rgba(255, 138, 61, 0.14);
    color: var(--text-primary);

    &:hover:not(:disabled) {
        border-color: var(--accent-primary);
        background: rgba(255, 138, 61, 0.22);
    }
`;

const ClearConfirm = styled.div`
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 16px;
    padding: 10px 12px;
    border: 1px solid rgba(255, 107, 107, 0.24);
    border-radius: var(--radius-sm);
    background: rgba(255, 107, 107, 0.06);
`;

const ClearText = styled.div`
    display: flex;
    flex-direction: column;
    gap: 2px;

    strong {
        font-size: 10.5px;
        color: var(--text-primary);
    }

    span {
        font-size: 9.5px;
        color: var(--text-muted);
    }
`;

const DangerButton = styled.button`
    padding: 5px 9px;
    border: 1px solid rgba(255, 107, 107, 0.45);
    border-radius: var(--radius-sm);
    background: rgba(255, 107, 107, 0.12);
    color: #ff9b9b;
    font: inherit;
    font-size: 9.5px;
    cursor: pointer;

    &:hover:not(:disabled) {
        background: rgba(255, 107, 107, 0.2);
        color: var(--text-primary);
    }

    &:disabled {
        opacity: 0.4;
        cursor: default;
    }
`;

const SaveBar = styled.div`
    position: sticky;
    bottom: -8px;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 16px;
    padding: 10px 0 8px;
    background: linear-gradient(to top, var(--bg-primary) 70%, transparent);
`;

const Status = styled.div`
    display: flex;
    align-items: center;
    font-family: var(--font-mono);
    font-size: 9.5px;
    color: var(--text-muted);
`;

const Notice = styled.span<{ $ok: boolean }>`
    display: inline-flex;
    align-items: center;
    gap: 5px;
    color: ${({$ok}) => $ok ? "var(--success)" : "var(--danger)"};
`;

const Actions = styled.div`
    display: flex;
    align-items: center;
    gap: 8px;
`;

const discardIn = keyframes`
    0% {
        opacity: 0;
        transform: translateX(12px) scale(0.9);
        filter: blur(2px);
    }
    60% {
        opacity: 1;
        transform: translateX(-2px) scale(1.03);
        filter: blur(0);
    }
    100% {
        opacity: 1;
        transform: translateX(0) scale(1);
        filter: blur(0);
    }
`;

const discardGlow = keyframes`
    from {
        box-shadow: 0 0 0 0 rgba(255, 107, 107, 0.4);
    }
    to {
        box-shadow: 0 0 0 9px rgba(255, 107, 107, 0);
    }
`;

const GhostAction = styled.button`
    padding: 6px 11px;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    background: transparent;
    color: var(--text-secondary);
    font: inherit;
    font-size: 10px;
    white-space: nowrap;
    cursor: pointer;
    transition: border-color 120ms ease, color 120ms ease, opacity 120ms ease;

    &:hover:not(:disabled) {
        border-color: var(--border-color-hover);
        color: var(--text-primary);
    }

    &:disabled {
        opacity: 0.35;
        cursor: default;
    }
`;

const DiscardAction = styled(GhostAction)`
    border-color: rgba(255, 107, 107, 0.35);
    color: var(--danger);
    transform-origin: right center;
    will-change: transform, opacity;
    animation: ${discardIn} 260ms cubic-bezier(0.34, 1.56, 0.64, 1),
    ${discardGlow} 650ms ease-out 80ms;

    &:hover:not(:disabled) {
        border-color: rgba(255, 107, 107, 0.6);
        color: var(--danger);
    }
`;

const SaveButton = styled.button`
    padding: 7px 14px;
    border: 1px solid transparent;
    border-radius: var(--radius-sm);
    background: var(--accent-gradient);
    color: #241206;
    font: inherit;
    font-size: 11px;
    font-weight: 600;
    cursor: pointer;
    transition: filter 140ms ease, opacity 140ms ease;

    &:hover:not(:disabled) {
        filter: brightness(1.08);
    }

    &:disabled {
        opacity: 0.35;
        cursor: default;
    }
`;

const Loading = styled.div`
    display: grid;
    flex: 1;
    place-items: center;
    font-family: var(--font-mono);
    font-size: 10px;
    color: var(--text-muted);
`;
