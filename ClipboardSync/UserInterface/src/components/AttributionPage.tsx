import {useEffect} from "react";
import styled from "styled-components";
import {useTranslation} from "@photino-net-utils/bridge";
import {ATTRIBUTIONS} from "../data/attributions";
import {XIcon} from "./Icons";
import {AttributionBrowser} from "./AttributionBrowser";

/** The open-source notices, shown as a floating panel over the settings. */
export function AttributionPage({onClose}: { onClose: () => void }) {
    const t = useTranslation();

    // Escape closes the panel, like the other overlays.
    useEffect(() => {
        const close = (event: KeyboardEvent) => {
            if (event.key === "Escape") onClose();
        };

        window.addEventListener("keydown", close);
        return () => window.removeEventListener("keydown", close);
    }, [onClose]);

    return (
        <Backdrop onClick={onClose}>
            <Panel
                aria-label={t("open_source_attribution")}
                aria-modal="true"
                onClick={(event) => event.stopPropagation()}
                role="dialog"
            >
                <Header>
                    <HeaderText>
                        <TitleRow>
                            <h2>{t("open_source_attribution")}</h2>
                            <Count>{t("count_open_source_projects", {count: ATTRIBUTIONS.length})}</Count>
                        </TitleRow>
                        <p>{t("open_source_licenses_for_the_libraries_clipboard_sync_uses")}</p>
                    </HeaderText>
                    <CloseButton onClick={onClose} title={t("close")} type="button">
                        <XIcon size={14}/>
                    </CloseButton>
                </Header>

                <AttributionBrowser/>
            </Panel>
        </Backdrop>
    );
}

const Backdrop = styled.div`
    position: fixed;
    inset: 0;
    z-index: 200;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 24px;
    background: var(--overlay);
    backdrop-filter: blur(3px);
`;

const Panel = styled.div`
    display: flex;
    flex-direction: column;
    gap: 14px;
    width: min(1040px, 100%);
    height: min(82vh, 780px);
    max-height: 100%;
    padding: 18px;
    border: 1px solid var(--border-color-hover);
    border-radius: var(--radius-lg);
    background: var(--bg-secondary);
    box-shadow: 0 24px 70px rgba(0, 0, 0, 0.55);
`;

const Header = styled.header`
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 16px;
    flex-shrink: 0;
`;

const HeaderText = styled.div`
    display: flex;
    flex-direction: column;
    gap: 3px;

    p {
        font-size: 10.5px;
        line-height: 1.55;
        color: var(--text-muted);
    }
`;

const TitleRow = styled.div`
    display: flex;
    align-items: baseline;
    gap: 8px;

    h2 {
        font-size: 14px;
        font-weight: 600;
    }
`;

const Count = styled.span`
    font-family: var(--font-mono);
    font-size: 9px;
    color: var(--text-muted);
`;

const CloseButton = styled.button`
    display: grid;
    place-items: center;
    width: 26px;
    height: 26px;
    flex-shrink: 0;
    padding: 0;
    border: 1px solid transparent;
    border-radius: var(--radius-sm);
    background: transparent;
    color: var(--text-muted);
    cursor: pointer;

    &:hover {
        border-color: var(--border-color);
        background: var(--bg-card-hover);
        color: var(--text-primary);
    }
`;
