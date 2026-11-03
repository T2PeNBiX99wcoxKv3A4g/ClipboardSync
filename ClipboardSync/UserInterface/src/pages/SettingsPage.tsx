import {useEffect, useState} from "react";
import styled, {keyframes} from "styled-components";
import {useTranslation} from "@photino-net-utils/bridge";
import {type AppInfo, openLogsFolder, setStartup, startupStatus} from "../api/system";
import {getSettings, type Settings, saveSettings} from "../api/settings";
import {Toggle} from "../components/Toggle";
import {Dropdown} from "../components/Dropdown";
import {CheckIcon, FolderIcon, InfoIcon} from "../components/Icons";

interface SettingsPageProps {
    info: AppInfo | null;
}

const languages = [
    {id: "en", label: "English"},
    {id: "ja", label: "日本語"},
    {id: "zh-CN", label: "简体中文"},
    {id: "zh-TW", label: "繁體中文"},
];

export function SettingsPage({info}: SettingsPageProps) {
    const t = useTranslation();
    const [settings, setSettings] = useState<Settings | null>(null);
    const [saved, setSaved] = useState<Settings | null>(null);
    const [startup, setStartupState] = useState<{ supported: boolean; enabled: boolean } | null>(null);
    const [saving, setSaving] = useState(false);
    const [notice, setNotice] = useState<{ ok: boolean; text: string } | null>(null);

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
        }, () => {
            setNotice({ok: false, text: t("The host did not answer.")});
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
            language: "en",
            startHidden: false,
            syncEnabled: true,
            syncText: true,
            syncImages: true,
            syncFiles: false,
            historyLimit: 200,
            maxImageMb: 10,
        });
    };

    const discard = () => {
        if (!saved) return;
        setSettings(saved);
        setNotice(null);
    };

    if (!settings) {
        return <Loading>{t("Loading settings…")}</Loading>;
    }

    return (
        <Page>
            <Section>
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
                            options={languages}
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

            <Section>
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
                </Card>
                <Note>{t("Images are shared as PNG; files come later.")}</Note>
            </Section>

            <Section>
                <SectionHead>
                    <h2>{t("History")}</h2>
                    <p>{t("The local record of clipboard items.")}</p>
                </SectionHead>
                <Card>
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

            <Section>
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
        </Page>
    );
}

const Page = styled.div`
    display: flex;
    flex-direction: column;
    gap: 16px;
    padding-bottom: 8px;
`;

const Section = styled.section`
    display: flex;
    flex-direction: column;
    gap: 8px;
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
    animation:
        ${discardIn} 260ms cubic-bezier(0.34, 1.56, 0.64, 1),
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
