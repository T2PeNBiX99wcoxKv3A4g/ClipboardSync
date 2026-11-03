import {useEffect, useState} from "react";
import styled, {keyframes} from "styled-components";
import {Native, ResizeHandles, useTranslation, useWindowCorners} from "@photino-net-utils/bridge";
import {type AppInfo, getAppInfo} from "./api/system";
import {type ClipboardItem, copyFiles, copyImage} from "./api/peer";
import {Sidebar} from "./components/Sidebar";
import {TitleBar} from "./components/TitleBar";
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

export default function App() {
    const t = useTranslation();
    const corners = useWindowCorners();
    const [page, setPage] = useState<Page>("devices");
    const [info, setInfo] = useState<AppInfo | null>(null);
    const [copiedId, setCopiedId] = useState<string | null>(null);

    useEffect(() => {
        void getAppInfo().then(setInfo, () => setInfo(null));
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
                            : <SettingsPage info={info}/>}
                    </Content>

                    <Footer>
                        <Version>
                            {info ? `v${info.version} · ${info.platform}` : t("browser preview")}
                        </Version>
                    </Footer>
                </Main>
            </Shell>

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
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
`;
