import styled, {keyframes} from "styled-components";
import {useTranslation} from "@photino-net-utils/bridge";
import type {UpdateProgress} from "../api/system";

interface UpdateDialogProps {
    progress: UpdateProgress | null;
    changelog: string | null;
    error: string | null;
    onCancel: () => void;
    onClose: () => void;
}

/** Formats a byte count for the progress line. */
function formatBytes(value: number) {
    if (value >= 1024 * 1024) return `${(value / 1024 / 1024).toFixed(1)} MB`;
    return `${Math.max(0, Math.round(value / 1024))} KB`;
}

/** The floating download / install dialog, with the changelog of the newest release. */
export function UpdateDialog({progress, changelog, error, onCancel, onClose}: UpdateDialogProps) {
    const t = useTranslation();
    const phase = progress?.phase ?? "changelog";
    const failed = error ?? (phase === "error" ? progress?.error ?? null : null);
    const installing = phase === "ready";
    const downloading = !failed && !installing;
    const percent = phase === "ready"
        ? 100
        : phase === "downloading" && progress && progress.percent >= 0
            ? progress.percent
            : null;
    const indeterminate = downloading && percent === null;

    return (
        <Backdrop role="dialog" aria-modal="true">
            <Card>
                <Header>
                    <h2>{t("Updating ClipboardSync")}</h2>
                    {failed && (
                        <CloseButton aria-label={t("Close")} onClick={onClose} type="button">
                            ×
                        </CloseButton>
                    )}
                </Header>

                <Status $failed={Boolean(failed)} $installing={installing}>
                    {failed
                        ? t("Update failed")
                        : installing
                            ? t("Installing...")
                            : phase === "downloading"
                                ? t("Downloading...")
                                : t("Loading changes...")}
                </Status>

                {!failed && (
                    <>
                        <Track
                            aria-label={t("Downloading...")}
                            aria-valuemax={100}
                            aria-valuemin={0}
                            aria-valuenow={percent ?? undefined}
                            role="progressbar"
                        >
                            <Fill
                                $indeterminate={indeterminate}
                                style={percent === null ? undefined : {width: `${percent}%`}}
                            />
                        </Track>
                        <Meta>
                            <Percent>{percent === null ? "…" : `${percent}%`}</Percent>
                            {progress && progress.total > 0 && (
                                <Bytes>
                                    {formatBytes(progress.downloaded)} / {formatBytes(progress.total)}
                                </Bytes>
                            )}
                        </Meta>
                    </>
                )}

                {failed && <Failure>{failed}</Failure>}

                <ChangesHeader>{t("Changes")}</ChangesHeader>
                <Changelog>
                    {changelog?.trim()
                        ? changelog
                        : phase === "changelog" && !failed
                            ? t("Loading changes...")
                            : t("No changelog was published.")}
                </Changelog>

                {downloading && (
                    <Actions>
                        <CancelButton onClick={onCancel} type="button">
                            {t("Cancel")}
                        </CancelButton>
                    </Actions>
                )}
            </Card>
        </Backdrop>
    );
}

const slide = keyframes`
    from {
        transform: translateX(-100%);
    }
    to {
        transform: translateX(420%);
    }
`;

const Backdrop = styled.div`
    position: fixed;
    inset: 0;
    z-index: 300;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 24px;
    background: rgba(0, 0, 0, 0.55);
    backdrop-filter: blur(2px);
`;

const Card = styled.div`
    display: flex;
    flex-direction: column;
    gap: 12px;
    width: min(560px, calc(100vw - 48px));
    max-height: min(72vh, 660px);
    padding: 18px;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-md);
    background: var(--bg-card);
    box-shadow: 0 18px 60px rgba(0, 0, 0, 0.45);
`;

const Header = styled.div`
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;

    h2 {
        font-size: 14px;
        font-weight: 600;
    }
`;

const CloseButton = styled.button`
    width: 24px;
    height: 24px;
    padding: 0;
    border: 0;
    border-radius: 50%;
    background: transparent;
    color: var(--text-muted);
    font-size: 18px;
    line-height: 1;
    cursor: pointer;

    &:hover {
        background: var(--bg-card-hover);
        color: var(--text-primary);
    }
`;

const Status = styled.span<{ $failed: boolean; $installing: boolean }>`
    font-size: 11.5px;
    color: ${({$failed, $installing}) =>
        $failed ? "var(--danger)" : $installing ? "var(--accent-primary)" : "var(--text-secondary)"};
`;

const Track = styled.div`
    position: relative;
    height: 12px;
    overflow: hidden;
    border: 1px solid var(--border-color);
    border-radius: 999px;
    background: var(--bg-input-strong);
    box-shadow: inset 0 1px 3px rgba(0, 0, 0, 0.2);
`;

const Fill = styled.div<{ $indeterminate: boolean }>`
    height: 100%;
    min-width: 10px;
    width: ${({$indeterminate}) => $indeterminate ? "32%" : "0"};
    border-radius: inherit;
    background: var(--accent-gradient);
    box-shadow: var(--accent-glow);
    transition: width 160ms ease-out;
    animation: ${({$indeterminate}) => $indeterminate ? slide : "none"} 1.1s ease-in-out infinite;
`;

const Meta = styled.div`
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 12px;
`;

const Percent = styled.span`
    font-family: var(--font-mono);
    font-size: 14px;
    font-weight: 600;
    color: var(--accent-primary);
`;

const Bytes = styled.span`
    font-family: var(--font-mono);
    font-size: 10px;
    color: var(--text-muted);
`;

const Failure = styled.p`
    font-size: 10.5px;
    color: var(--danger);
`;

const ChangesHeader = styled.span`
    margin-top: 2px;
    font-size: 10px;
    font-weight: 600;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--text-muted);
`;

const Changelog = styled.pre`
    flex: 1;
    min-height: 120px;
    margin: 0;
    overflow: auto;
    padding: 10px 12px;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    background: var(--bg-secondary);
    color: var(--text-secondary);
    font-family: var(--font-mono);
    font-size: 10px;
    line-height: 1.55;
    white-space: pre-wrap;
    user-select: text;
`;

const Actions = styled.div`
    display: flex;
    justify-content: flex-end;
`;

const CancelButton = styled.button`
    padding: 5px 10px;
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
