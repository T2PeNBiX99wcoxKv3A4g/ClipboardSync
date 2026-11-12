import styled from "styled-components";
import {useEffect, useState} from "react";
import {useLocale, useTranslation} from "@photino-net-utils/bridge";
import {type ClipboardItem, type ClipboardKind, getImagePreview, type TransferStatus} from "../api/peer";
import {formatSize, relativeTime} from "../utils/format";
import {
    CheckIcon,
    CopyIcon,
    DownloadIcon,
    FileIcon,
    FolderIcon,
    ImageIcon,
    LinkIcon,
    TextIcon,
    UploadIcon,
} from "./Icons";

interface ClipboardEntryProps {
    item: ClipboardItem;
    copied: boolean;
    onCopy: (item: ClipboardItem) => void;
}

/** One item in a device's clipboard column, with how far it has travelled through the mesh. */
export function ClipboardEntry({item, copied, onCopy}: ClipboardEntryProps) {
    const t = useTranslation();
    const locale = useLocale();
    const Icon = kindIcon(item.kind);
    const status = statusLabel(item, t);
    const count = `${item.reached}/${item.peers}`;
    const showCount = item.status !== "received" && item.peers > 0;
    const [preview, setPreview] = useState<string | null | undefined>(undefined);
    const fileCount = item.files?.filter((file) => !file.directory).length ?? 0;
    const percent = item.kind === "file" && item.totalBytes > 0
        ? Math.min(100, Math.round(item.progress / item.totalBytes * 100))
        : null;
    const title = item.kind === "file"
        ? item.files?.length === 1
            ? item.files[0].path
            : t("{count} items", {count: item.files?.length ?? 0})
        : item.content;
    const meta = item.kind === "file"
        ? fileCount === 0
            ? t("Empty folder")
            : `${fileCount === 1 ? t("1 file") : t("{count} files", {count: fileCount})} · ${formatSize(item.totalBytes)}`
        : item.meta;

    useEffect(() => {
        if (item.kind !== "image") return;

        let cancelled = false;
        setPreview(undefined);

        void getImagePreview(item.id).then((dataUrl) => {
            if (!cancelled) setPreview(dataUrl);
        }, () => {
            if (!cancelled) setPreview(null);
        });

        return () => {
            cancelled = true;
        };
    }, [item.id, item.kind]);

    return (
        <Entry $status={item.status}>
            <Top>
                <Kind $kind={item.kind}>
                    <Icon size={14}/>
                </Kind>

                <Text>
                    <Content title={title}>{title}</Content>
                    <Meta>
                        {meta && <span>{meta}</span>}
                        <span>{relativeTime(item.at, locale)}</span>
                    </Meta>
                </Text>

                <CopyButton
                    $active={copied}
                    onClick={() => onCopy(item)}
                    title={copied ? t("Copied") : t("Copy")}
                    type="button"
                >
                    {copied ? <CheckIcon size={12}/> : <CopyIcon size={12}/>}
                </CopyButton>
            </Top>

            {item.kind === "image" && (
                <Preview>
                    {preview ? (
                        <img alt={item.content} src={preview}/>
                    ) : (
                        <PreviewPlaceholder>
                            <ImageIcon size={18}/>
                        </PreviewPlaceholder>
                    )}
                </Preview>
            )}

            {item.kind === "file" && item.files && item.files.length > 0 && (
                <FileList>
                    {item.files.slice(0, 4).map((file) => (
                        <FileRow key={file.path}>
                            {file.directory ? <FolderIcon size={11}/> : <FileIcon size={11}/>}
                            <FileName title={file.path}>{file.path}</FileName>
                            {!file.directory && <FileSize>{formatSize(file.size)}</FileSize>}
                        </FileRow>
                    ))}
                    {item.files.length > 4 && <More>+{item.files.length - 4}</More>}
                </FileList>
            )}

            <Bottom>
                <Status
                    $status={item.status}
                    title={showCount
                        ? t("{reached} of {peers} devices have this item.", {
                            reached: item.reached,
                            peers: item.peers,
                        })
                        : undefined}
                >
                    <Dot/>
                    {t(status)}
                    {percent !== null && item.status === "transferring" && <Percent>{percent}%</Percent>}
                    {showCount && <Count>{count}</Count>}
                </Status>

                <Direction>
                    {item.direction === "out" ? <UploadIcon size={11}/> : <DownloadIcon size={11}/>}
                    {item.direction === "out" ? t("Outgoing") : t("Incoming")}
                </Direction>
            </Bottom>
        </Entry>
    );
}

function kindIcon(kind: ClipboardKind) {
    switch (kind) {
        case "link":
            return LinkIcon;
        case "image":
            return ImageIcon;
        case "file":
            return FileIcon;
        default:
            return TextIcon;
    }
}

function kindColor(kind: ClipboardKind): string {
    switch (kind) {
        case "link":
            return "#ff8a3d";
        case "image":
            return "#60a5fa";
        case "file":
            return "#ffd166";
        default:
            return "#c6b6a6";
    }
}

function statusColor(status: TransferStatus): string {
    switch (status) {
        case "synced":
            return "#6ee7b7";
        case "transferring":
            return "#ff8a3d";
        case "waiting":
            return "#ffd166";
        case "received":
            return "#60a5fa";
        default:
            return "#ff6b6b";
    }
}

/** "Sending" and "Receiving" share the transferring state; the direction decides the label. */
function statusLabel(item: ClipboardItem, t: (key: string) => string): string {
    switch (item.status) {
        case "synced":
            return t("Synced");
        case "transferring":
            return item.direction === "out" ? t("Sending") : t("Receiving");
        case "waiting":
            return t("Waiting");
        case "received":
            return t("Received");
        default:
            return t("Failed");
    }
}

const Entry = styled.article<{ $status: TransferStatus }>`
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 10px 11px;
    border: 1px solid var(--border-color);
    border-left: 2px solid ${({$status}) => statusColor($status)};
    border-radius: var(--radius-sm);
    background: rgba(20, 16, 12, 0.5);
    transition: background 120ms ease, border-color 120ms ease;

    &:hover {
        background: rgba(35, 29, 24, 0.8);
        border-color: var(--border-color-hover);
        border-left-color: ${({$status}) => statusColor($status)};
    }
`;

const Top = styled.div`
    display: grid;
    grid-template-columns: 26px minmax(0, 1fr) 24px;
    gap: 8px;
    align-items: start;
`;

const Preview = styled.div`
    display: grid;
    place-items: center;
    height: 112px;
    overflow: hidden;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    background: rgba(10, 8, 6, 0.55);

    img {
        display: block;
        width: 100%;
        height: 100%;
        object-fit: contain;
    }
`;

const PreviewPlaceholder = styled.div`
    display: grid;
    place-items: center;
    width: 100%;
    height: 100%;
    color: var(--text-muted);
    background: linear-gradient(135deg, rgba(96, 165, 250, 0.08), rgba(255, 138, 61, 0.04));
`;

const FileList = styled.div`
    display: flex;
    flex-direction: column;
    gap: 3px;
    padding: 7px 8px;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    background: rgba(10, 8, 6, 0.45);
`;

const FileRow = styled.div`
    display: flex;
    align-items: center;
    gap: 6px;
    min-width: 0;
    color: var(--text-secondary);
    font-family: var(--font-mono);
    font-size: 8.5px;

    svg {
        flex-shrink: 0;
        color: #ffd166;
    }
`;

const FileName = styled.span`
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
`;

const FileSize = styled.span`
    margin-left: auto;
    color: var(--text-muted);
`;

const More = styled.span`
    padding-left: 17px;
    color: var(--text-muted);
    font-family: var(--font-mono);
    font-size: 8.5px;
`;

const Kind = styled.span<{ $kind: ClipboardKind }>`    display: grid;
    place-items: center;
    width: 26px;
    height: 26px;
    border-radius: 7px;
    background: ${({$kind}) => `${kindColor($kind)}1a`};
    color: ${({$kind}) => kindColor($kind)};
`;

const Text = styled.div`
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
`;

const Content = styled.span`
    display: -webkit-box;
    overflow: hidden;
    -webkit-box-orient: vertical;
    -webkit-line-clamp: 2;
    font-size: 11px;
    line-height: 1.45;
    color: var(--text-primary);
    overflow-wrap: anywhere;
`;

const Meta = styled.div`
    display: flex;
    align-items: center;
    gap: 7px;
    overflow: hidden;
    font-family: var(--font-mono);
    font-size: 8.5px;
    color: var(--text-muted);
    white-space: nowrap;

    span + span::before {
        content: "·";
        margin-right: 7px;
        opacity: 0.6;
    }
`;

const CopyButton = styled.button<{ $active: boolean }>`
    display: grid;
    place-items: center;
    width: 24px;
    height: 24px;
    padding: 0;
    border: 1px solid transparent;
    border-radius: var(--radius-sm);
    background: transparent;
    color: ${({$active}) => $active ? "var(--accent-primary)" : "var(--text-muted)"};
    cursor: pointer;
    transition: color 120ms ease, background 120ms ease, border-color 120ms ease;

    &:hover {
        border-color: var(--border-color);
        background: var(--bg-card-hover);
        color: ${({$active}) => $active ? "var(--accent-primary)" : "var(--text-primary)"};
    }
`;

const Bottom = styled.div`
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    padding-top: 7px;
    border-top: 1px solid var(--border-color);
`;

const Status = styled.span<{ $status: TransferStatus }>`
    display: inline-flex;
    align-items: center;
    gap: 5px;
    font-family: var(--font-mono);
    font-size: 8.5px;
    color: ${({$status}) => statusColor($status)};
`;

const Dot = styled.span`
    width: 5px;
    height: 5px;
    border-radius: 50%;
    background: currentColor;
`;

const Count = styled.span`
    padding: 1px 4px;
    border-radius: 3px;
    background: rgba(255, 255, 255, 0.05);
    color: var(--text-secondary);
`;

const Percent = styled.span`
    padding: 1px 4px;
    border-radius: 3px;
    background: rgba(255, 138, 61, 0.12);
    color: var(--accent-primary);
`;

const Direction = styled.span`
    display: inline-flex;
    align-items: center;
    gap: 4px;
    font-family: var(--font-mono);
    font-size: 8px;
    color: var(--text-muted);
    white-space: nowrap;
`;
