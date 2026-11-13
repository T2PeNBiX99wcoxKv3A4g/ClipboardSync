import {useEffect, useState} from "react";
import styled, {keyframes} from "styled-components";
import {Native, ResizeHandles, useTranslation, useWindowCorners} from "@photino-net-utils/bridge";
import {type AppInfo, downloadUpdate, getAppInfo, getChangelog, getUpdateStatus, type UpdateCheck, type UpdateProgress} from "./api/system";
import {type ClipboardItem, copyFiles, copyImage} from "./api/peer";
import {Sidebar} from "./components/Sidebar";
import {TitleBar} from "./components/TitleBar";
import {UpdateDialog} from "./components/UpdateDialog";
import {DevicesPage} from "./pages/DevicesPage";
import {SettingsPage} from "./pages/SettingsPage";
import type {Page} from "./pages";

/** Pages fade in from a little below, which makes a switch feel like a change of view. */
const pageIn = keyframes`
    from {
        opacity: 0;
        transform: translateY(8px) scale(0.995);
    }
    to {
        opacity: 1;
        transform: translateY(0) scale(1);
    }
`;

const pageMeta: Record<Page, { title: string; subtitle: string }> = {
    devices: {
        title: "Devices",
        subtitle: "Every device on the mesh, with the clipboard history it holds.",
    },
    settings: {
        title: "Settings",
        subtitle: "How ClipboardSync looks, starts, and shares.",
    },
};

interface UpdateDialogState {
    progress: UpdateProgress | null;
    changelog: string | null;
    loadingChangelog: boolean;
    error: string | null;
}

export default function App() {
    const t = useTranslation();
    const corners = useWindowCorners();
    const [page, setPage] = useState<Page>("devices");
    const [info, setInfo] = useState<AppInfo | null>(null);
    const [update, setUpdate] = useState<UpdateCheck | null>(null);
    const [updateDialog, setUpdateDialog] = useState<UpdateDialogState | null>(null);
    const [settingsSection, setSettingsSection] = useState<string | null>(null);
    const [copiedId, setCopiedId] = useState<string | null>(null);

    useEffect(() => {
        void getAppInfo().then(setInfo, () => setInfo(null));
    }, []);

    // The backend caches the published version, so this normally answers at once.
    useEffect(() => {
        void getUpdateStatus().then(setUpdate, () => undefined);
    }, []);

    // The host waits for this before pushing events; sending to a web view that is still loading
    // would abort it.
    useEffect(() => {
        const timer = window.setTimeout(() => Native.send({type: "app.ready"}), 200);
        return () => window.clearTimeout(timer);
    }, []);

    // The tray can ask for a page while the window is hidden.
    useEffect(() => {
        return Native.on((message) => {
            if (message.type === "app.update") {
                setUpdate((message.data as UpdateCheck | undefined) ?? null);
                return;
            }

            if (message.type === "app.updateProgress") {
                const progress = message.data as UpdateProgress;
                setUpdateDialog((current) => current
                    ? {...current, progress, error: progress.error ?? current.error}
                    : current);
                return;
            }

            if (message.type !== "app.navigate")
                return;

            const next = (message.data as { page?: Page } | undefined)?.page;
            if (next) setPage(next);
        });
    }, []);

    const copy = (item: ClipboardItem) => {
        if (item.kind === "image")
            void copyImage(item.id).catch(() => undefined);
        else if (item.kind === "file")
            void copyFiles(item.id).catch(() => undefined);
        else
            void navigator.clipboard?.writeText(item.content).catch(() => undefined);

        setCopiedId(item.id);
        window.setTimeout(() => setCopiedId((current) => current === item.id ? null : current), 1600);
    };

    const startUpdate = () => {
        setUpdateDialog({
            progress: {received: 0, total: 0, done: false, installing: false},
            changelog: null,
            loadingChangelog: true,
            error: null
        });

        void getChangelog().then(
            (changelog) => setUpdateDialog((current) => current
                ? {...current, changelog, loadingChangelog: false}
                : current),
            () => setUpdateDialog((current) => current
                ? {...current, loadingChangelog: false}
                : current)
        );

        void downloadUpdate().catch((error) => {
            setUpdateDialog((current) => current
                ? {...current, error: error instanceof Error ? error.message : String(error)}
                : current);
        });
    };

    const {title, subtitle} = pageMeta[page];

    return (
        <Root $rounded={corners}>
            <TitleBar/>

            <Shell>
                <Sidebar onNavigate={setPage} page={page}/>

                <Main>
                    <Header>
                        <HeaderText key={page}>
                            <h1>{t(title)}</h1>
                            <p>{t(subtitle)}</p>
                        </HeaderText>
                    </Header>

                    <Content key={page}>
                        {page === "devices"
                            ? <DevicesPage copiedId={copiedId} onCopy={copy}/>
                            : (
                                <SettingsPage
                                    focusSection={settingsSection}
                                    info={info}
                                    onInstallUpdate={startUpdate}
                                    onSectionFocused={() => setSettingsSection(null)}
                                />
                            )}
                    </Content>

                    <Footer>
                        <Version>
                            <VersionText>
                                {info
                                    ? `v${info.version} · ${info.platform}${info.debug ? " · debug" : ""}`
                                    : t("browser preview")}
                            </VersionText>
                            {update?.available && (
                                <UpdateDot
                                    aria-label={t("Version {version} is available.", {version: update.latest})}
                                    onClick={() => {
                                        setPage("settings");
                                        setSettingsSection("updates");
                                    }}
                                    title={t("Version {version} is available.", {version: update.latest})}
                                    type="button"
                                />
                            )}
                        </Version>
                    </Footer>
                </Main>
            </Shell>

            {updateDialog && (
                <UpdateDialog
                    changelog={updateDialog.changelog}
                    error={updateDialog.error}
                    loadingChangelog={updateDialog.loadingChangelog}
                    onClose={() => setUpdateDialog(null)}
                    progress={updateDialog.progress}
                />
            )}

            <ResizeHandles/>
        </Root>
    );
}

const Root = styled.div<{ $rounded: boolean }>`
    display: flex;
    flex-direction: column;
    height: 100%;
    overflow: hidden;
    background: var(--bg-primary);

    /* An undecorated window gets no frame from the compositor, so the corners are drawn here. */
    border-radius: ${({$rounded}) => ($rounded ? "10px" : "0")};
    box-shadow: ${({$rounded}) => ($rounded ? "inset 0 0 0 1px var(--border-color)" : "none")};
`;

const Shell = styled.div`
    display: flex;
    flex: 1;
    min-height: 0;
    background: radial-gradient(1100px 620px at 18% -10%, rgba(255, 138, 61, 0.1), transparent 60%),
    radial-gradient(900px 500px at 110% 15%, rgba(255, 107, 107, 0.08), transparent 55%),
    var(--bg-primary);
`;

const Main = styled.main`
    display: flex;
    flex-direction: column;
    flex: 1;
    min-width: 0;
`;

const Header = styled.header`
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 20px;
    padding: 22px 26px 16px;
    border-bottom: 1px solid var(--border-color);
`;

const HeaderText = styled.div`
    display: flex;
    flex-direction: column;
    gap: 3px;
    animation: ${pageIn} 0.18s cubic-bezier(0.22, 1, 0.36, 1);

    h1 {
        font-size: 20px;
        font-weight: 600;
        letter-spacing: -0.02em;
    }

    p {
        font-size: 12px;
        color: var(--text-secondary);
    }
`;

const Content = styled.div`
    display: flex;
    flex: 1;
    flex-direction: column;
    min-height: 0;
    padding: 18px 26px 22px;
    overflow: auto;
    animation: ${pageIn} 0.18s cubic-bezier(0.22, 1, 0.36, 1);
`;

const Footer = styled.footer`
    display: flex;
    justify-content: flex-end;
    gap: 16px;
    padding: 7px 26px;
    border-top: 1px solid var(--border-color);
    font-family: var(--font-mono);
    font-size: 10px;
    color: var(--text-muted);
`;

const Version = styled.span`
    display: inline-flex;
    align-items: center;
    gap: 7px;
    min-width: 0;
`;

const VersionText = styled.span`
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
`;

/** Shown next to the version only when the published build is newer than this one. */
const UpdateDot = styled.button`
    width: 7px;
    height: 7px;
    flex-shrink: 0;
    padding: 0;
    border: 0;
    border-radius: 50%;
    background: var(--accent-primary);
    box-shadow: 0 0 0 3px rgba(255, 138, 61, 0.14), 0 0 10px rgba(255, 138, 61, 0.55);
    cursor: pointer;
    transition: transform 120ms ease, box-shadow 120ms ease;

    &:hover {
        transform: scale(1.25);
        box-shadow: 0 0 0 4px rgba(255, 138, 61, 0.2), 0 0 14px rgba(255, 138, 61, 0.7);
    }

    &:focus-visible {
        outline: 2px solid var(--border-color-focus);
        outline-offset: 3px;
    }
`;
