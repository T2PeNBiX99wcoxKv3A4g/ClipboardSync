import {useEffect, useState} from "react";
import styled, {css, keyframes} from "styled-components";
import {useAppTranslation} from "../i18n";
import {
    type ClipboardItem,
    type DevicesState,
    type DiscoveredDevice,
    getDevices,
    getHistory,
    type IncomingPairRequest,
    onDevicesChanged,
    onHistoryChanged,
    onPairCode,
    onPairRequest,
    onPairResult,
    type PairCode,
    removeDevice,
    respondToPairing,
    startDiscovery,
    startPairing,
    stopDiscovery,
} from "../api/peer";
import {ClipboardEntry} from "../components/ClipboardEntry";
import {CheckIcon, CopyIcon, MonitorIcon, PlusIcon, RefreshIcon, ShieldIcon, XIcon} from "../components/Icons";

interface DevicesPageProps {
    copiedId: string | null;
    onCopy: (item: ClipboardItem) => void;
}

const DEFAULT_PORT = 45456;

const pulse = keyframes`
    0%, 100% {
        box-shadow: 0 0 0 0 rgba(255, 138, 61, 0.4);
    }
    50% {
        box-shadow: 0 0 0 4px rgba(255, 138, 61, 0);
    }
`;

/** All paired devices and the clipboard board each of them holds. */
export function DevicesPage({copiedId, onCopy}: DevicesPageProps) {
    const t = useAppTranslation();
    const [devices, setDevices] = useState<DevicesState | null>(null);
    const [history, setHistory] = useState<ClipboardItem[]>([]);
    const [pairOpen, setPairOpen] = useState(false);
    const [pairCode, setPairCode] = useState<PairCode | null>(null);
    const [incoming, setIncoming] = useState<IncomingPairRequest | null>(null);
    const [pairError, setPairError] = useState<string | null>(null);
    const [pairing, setPairing] = useState(false);
    const [address, setAddress] = useState("");
    const [codeCopied, setCodeCopied] = useState(false);

    useEffect(() => {
        let cancelled = false;

        void getDevices().then((next) => {
            if (!cancelled) setDevices(next);
        }, () => undefined);

        void getHistory().then((next) => {
            if (!cancelled) setHistory(next);
        }, () => undefined);

        const offDevices = onDevicesChanged(setDevices);
        const offHistory = onHistoryChanged(setHistory);

        return () => {
            cancelled = true;
            offDevices();
            offHistory();
        };
    }, []);

    useEffect(() => {
        const offCode = onPairCode((code) => {
            setPairOpen(true);
            setPairing(true);
            setPairError(null);
            setPairCode(code);
        });

        const offRequest = onPairRequest((request) => {
            setPairOpen(true);
            setPairError(null);
            setIncoming(request);
        });

        const offResult = onPairResult((result) => {
            setPairing(false);

            if (result.ok) {
                closePairing();
                return;
            }

            setPairCode(null);
            setPairError(result.error ?? t("Pairing failed."));
        });

        return () => {
            offCode();
            offRequest();
            offResult();
        };
    }, [t]);

    const openPairing = () => {
        setPairOpen(true);
        setPairCode(null);
        setIncoming(null);
        setPairError(null);
        setPairing(false);
        void startDiscovery();
    };

    const closePairing = () => {
        setPairOpen(false);
        setPairCode(null);
        setIncoming(null);
        setPairError(null);
        setPairing(false);
        setAddress("");
        setCodeCopied(false);
        void stopDiscovery();
    };

    const pairWith = (host: string, port: number) => {
        setPairing(true);
        setPairError(null);
        setPairCode(null);
        void startPairing(host, port).catch(() => {
            setPairing(false);
            setPairError(t("Pairing failed."));
        });
    };

    const pairWithDiscovered = (device: DiscoveredDevice) => pairWith(device.host, device.port);

    const pairWithAddress = () => {
        const trimmed = address.trim();
        if (trimmed.length === 0) return;

        const separator = trimmed.lastIndexOf(':');
        const host = separator > 0 ? trimmed.slice(0, separator) : trimmed;
        const parsed = separator > 0 ? Number(trimmed.slice(separator + 1)) : DEFAULT_PORT;
        const port = Number.isFinite(parsed) && parsed > 0 && parsed <= 65535 ? parsed : DEFAULT_PORT;

        pairWith(host, port);
    };

    const answer = (accept: boolean) => {
        if (!incoming) return;

        void respondToPairing(incoming.requestId, accept).catch(() => undefined);
        setIncoming(null);
        closePairing();
    };

    const copyCode = () => {
        if (!pairCode) return;

        void navigator.clipboard?.writeText(pairCode.code).catch(() => undefined);
        setCodeCopied(true);
        window.setTimeout(() => setCodeCopied(false), 1600);
    };

    const board = devices ? [devices.self, ...devices.peers] : [];

    return (
        <Page>
            <Top>
                <PairButton onClick={openPairing} type="button">
                    <PlusIcon size={14}/>
                    {t("Pair a device")}
                </PairButton>
            </Top>

            <Board>
                {board.map((device) => {
                    const items = history
                        .filter((item) => item.originDeviceId === device.id)
                        .sort((left, right) => right.at - left.at);

                    return (
                        <Column key={device.id || "self"} $offline={!device.online}>
                            <ColumnHead>
                                <DeviceIcon $online={device.online}>
                                    <MonitorIcon size={18}/>
                                </DeviceIcon>
                                <DeviceInfo>
                                    <NameRow>
                                        <strong>{device.name}</strong>
                                        {device.self && <SelfBadge>{t("This device")}</SelfBadge>}
                                    </NameRow>
                                    <StateLine>
                                        <Dot $online={device.online}/>
                                        {device.online ? t("Online") : t("Offline")}
                                        <span>·</span>
                                        {t("{count} items", {count: items.length})}
                                    </StateLine>
                                </DeviceInfo>
                                {!device.self && (
                                    <RemoveButton
                                        onClick={() => void removeDevice(device.id).catch(() => undefined)}
                                        title={t("Unpair")}
                                        type="button"
                                    >
                                        <XIcon size={12}/>
                                    </RemoveButton>
                                )}
                            </ColumnHead>

                            <ColumnBody>
                                {items.length === 0 ? (
                                    <Empty>{t("No clipboard items from this device yet.")}</Empty>
                                ) : items.map((item) => (
                                    <ClipboardEntry
                                        copied={copiedId === item.id}
                                        item={item}
                                        key={item.id}
                                        onCopy={onCopy}
                                    />
                                ))}

                                {!device.online && (
                                    <OfflineNote>
                                        {t("This device is offline. New items wait here until it reconnects.")}
                                    </OfflineNote>
                                )}
                            </ColumnBody>
                        </Column>
                    );
                })}
            </Board>

            {devices && devices.peers.length === 0 && (
                <EmptyBoard>
                    <PlusIcon size={20}/>
                    <p>{t("No other devices are paired yet. Pair one to share the clipboard.")}</p>
                    <PairButton onClick={openPairing} type="button">
                        <PlusIcon size={14}/>
                        {t("Pair a device")}
                    </PairButton>
                </EmptyBoard>
            )}

            {pairOpen && (
                <Backdrop onMouseDown={closePairing}>
                    <Dialog
                        aria-label={t("Pair a device")}
                        aria-modal="true"
                        onMouseDown={(event) => event.stopPropagation()}
                        role="dialog"
                    >
                        <DialogHead>
                            <div>
                                <h3>{t("Pair a device")}</h3>
                                <p>{t("Both devices show the same code, so you can check the connection.")}</p>
                            </div>
                            <CloseButton onClick={closePairing} title={t("Close")} type="button">
                                <XIcon size={14}/>
                            </CloseButton>
                        </DialogHead>

                        {incoming ? (
                            <Section>
                                <Label>{t("Incoming pairing request")}</Label>
                                <PromptName>{incoming.deviceName}</PromptName>
                                <CodeBox>
                                    <Code>{incoming.code}</Code>
                                </CodeBox>
                                <Hint>{t("Compare this code with the other device.")}</Hint>
                                <Actions>
                                    <RejectButton onClick={() => answer(false)} type="button">
                                        {t("Reject")}
                                    </RejectButton>
                                    <AcceptButton onClick={() => answer(true)} type="button">
                                        {t("Accept")}
                                    </AcceptButton>
                                </Actions>
                            </Section>
                        ) : pairCode ? (
                            <Section>
                                <Label>{t("Pairing code")}</Label>
                                <PromptName>{pairCode.deviceName}</PromptName>
                                <CodeBox>
                                    <Code>{pairCode.code}</Code>
                                    <CodeButton onClick={copyCode} title={t("Copy code")} type="button">
                                        {codeCopied ? <CheckIcon size={14}/> : <CopyIcon size={14}/>}
                                    </CodeButton>
                                </CodeBox>
                                <Hint>{t("Compare this code with the other device, then accept there.")}</Hint>
                                <Waiting>{t("Waiting for the other device…")}</Waiting>
                            </Section>
                        ) : (
                            <>
                                {pairError && <Error>{pairError}</Error>}

                                <Section>
                                    <Label>{t("Discovered devices")}</Label>
                                    {devices && devices.discovered.length > 0 ? (
                                        <DeviceList>
                                            {devices.discovered.map((device) => (
                                                <DiscoveredRow key={device.id}>
                                                    <DiscoveredText>
                                                        <strong>{device.name}</strong>
                                                        <span>{device.host}:{device.port}</span>
                                                    </DiscoveredText>
                                                    <SmallPair
                                                        disabled={pairing}
                                                        onClick={() => pairWithDiscovered(device)}
                                                        type="button"
                                                    >
                                                        {t("Pair")}
                                                    </SmallPair>
                                                </DiscoveredRow>
                                            ))}
                                        </DeviceList>
                                    ) : (
                                        <EmptyDiscovered>
                                            <RefreshIcon size={14}/>
                                            {t("Scanning the local network…")}
                                        </EmptyDiscovered>
                                    )}
                                </Section>

                                <Section>
                                    <Label>{t("Enter an address")}</Label>
                                    <AddressRow>
                                        <AddressInput
                                            onChange={(event) => setAddress(event.target.value)}
                                            onKeyDown={(event) => {
                                                if (event.key === "Enter") pairWithAddress();
                                            }}
                                            placeholder="192.168.1.20:45456"
                                            value={address}
                                        />
                                        <SmallPair
                                            disabled={pairing || address.trim().length === 0}
                                            onClick={pairWithAddress}
                                            type="button"
                                        >
                                            {t("Pair")}
                                        </SmallPair>
                                    </AddressRow>
                                </Section>
                            </>
                        )}

                        <Hint>
                            <ShieldIcon size={14}/>
                            <span>{t("The pairing proves both devices own their keys; the code is never sent as a password.")}</span>
                        </Hint>
                    </Dialog>
                </Backdrop>
            )}
        </Page>
    );
}

const Page = styled.div`
    display: flex;
    flex-direction: column;
    gap: 14px;
    min-height: 100%;
`;

const Top = styled.div`
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 16px;
`;

const PairButton = styled.button`
    display: inline-flex;
    align-items: center;
    gap: 6px;
    flex-shrink: 0;
    padding: 7px 12px;
    border: 1px solid rgba(255, 138, 61, 0.4);
    border-radius: var(--radius-sm);
    background: rgba(255, 138, 61, 0.1);
    color: var(--accent-primary);
    font: inherit;
    font-size: 10.5px;
    cursor: pointer;
    transition: background 140ms ease, border-color 140ms ease, transform 140ms ease;

    &:hover {
        border-color: rgba(255, 138, 61, 0.7);
        background: rgba(255, 138, 61, 0.18);
        transform: translateY(-1px);
    }

    &:active {
        transform: translateY(0);
    }
`;

const Board = styled.div`
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(285px, 1fr));
    gap: 12px;
    align-items: start;
`;

const Column = styled.section<{ $offline: boolean }>`
    display: flex;
    flex-direction: column;
    gap: 10px;
    padding: 12px;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-md);
    background: var(--bg-card);
    opacity: ${({$offline}) => $offline ? 0.82 : 1};
`;

const ColumnHead = styled.header`
    display: flex;
    align-items: center;
    gap: 9px;
    padding-bottom: 10px;
    border-bottom: 1px solid var(--border-color);
`;

const DeviceIcon = styled.span<{ $online: boolean }>`
    display: grid;
    place-items: center;
    width: 34px;
    height: 34px;
    flex-shrink: 0;
    border-radius: 9px;
    background: ${({$online}) => $online ? "rgba(255, 138, 61, 0.12)" : "rgba(93, 123, 135, 0.1)"};
    color: ${({$online}) => $online ? "var(--accent-primary)" : "var(--text-muted)"};
`;

const DeviceInfo = styled.div`
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
`;

const NameRow = styled.div`
    display: flex;
    align-items: center;
    gap: 6px;
    min-width: 0;

    strong {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font-size: 12.5px;
        font-weight: 600;
    }
`;

const SelfBadge = styled.span`
    flex-shrink: 0;
    padding: 1px 6px;
    border: 1px solid var(--border-color);
    border-radius: 999px;
    font-family: var(--font-mono);
    font-size: 8px;
    color: var(--text-secondary);
`;

const StateLine = styled.div`
    display: flex;
    align-items: center;
    gap: 5px;
    overflow: hidden;
    font-family: var(--font-mono);
    font-size: 8.5px;
    color: var(--text-muted);
    white-space: nowrap;

    span {
        opacity: 0.65;
    }
`;

const Dot = styled.span<{ $online: boolean }>`
    width: 6px;
    height: 6px;
    flex-shrink: 0;
    border-radius: 50%;
    background: ${({$online}) => $online ? "var(--accent-primary)" : "var(--text-muted)"};
    animation: ${({$online}) => $online ? css`${pulse} 2.4s ease-in-out infinite` : "none"};
`;

const RemoveButton = styled.button`
    display: grid;
    place-items: center;
    width: 24px;
    height: 24px;
    margin-left: auto;
    flex-shrink: 0;
    padding: 0;
    border: 1px solid transparent;
    border-radius: var(--radius-sm);
    background: transparent;
    color: var(--text-muted);
    cursor: pointer;

    &:hover {
        border-color: rgba(255, 107, 107, 0.4);
        background: rgba(255, 107, 107, 0.08);
        color: var(--danger);
    }
`;

const ColumnBody = styled.div`
    display: flex;
    flex-direction: column;
    gap: 8px;
`;

const Empty = styled.p`
    padding: 24px 10px;
    text-align: center;
    font-size: 10px;
    color: var(--text-muted);
`;

const OfflineNote = styled.p`
    padding: 7px 9px;
    border: 1px dashed var(--border-color);
    border-radius: var(--radius-sm);
    font-size: 9px;
    line-height: 1.55;
    color: var(--text-muted);
`;

const EmptyBoard = styled.div`
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 10px;
    padding: 26px 16px;
    border: 1px dashed var(--border-color-hover);
    border-radius: var(--radius-md);
    color: var(--text-muted);
    text-align: center;

    p {
        max-width: 360px;
        font-size: 10.5px;
    }
`;

const Backdrop = styled.div`
    position: fixed;
    inset: 0;
    z-index: 100;
    display: grid;
    place-items: center;
    padding: 24px;
    background: rgba(10, 8, 6, 0.72);
    backdrop-filter: blur(3px);
`;

const Dialog = styled.div`
    width: min(420px, 100%);
    display: flex;
    flex-direction: column;
    gap: 14px;
    padding: 18px;
    border: 1px solid var(--border-color-hover);
    border-radius: var(--radius-lg);
    background: var(--bg-secondary);
    box-shadow: 0 24px 70px rgba(0, 0, 0, 0.55);
`;

const DialogHead = styled.div`
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 12px;

    h3 {
        font-size: 14px;
        font-weight: 600;
    }

    p {
        margin-top: 3px;
        font-size: 10px;
        line-height: 1.5;
        color: var(--text-muted);
    }
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

const Section = styled.section`
    display: flex;
    flex-direction: column;
    gap: 8px;
`;

const Label = styled.span`
    font-size: 9px;
    font-weight: 600;
    letter-spacing: 0.1em;
    color: var(--text-muted);
    text-transform: uppercase;
`;

const PromptName = styled.strong`
    font-size: 12px;
    font-weight: 600;
`;

const CodeBox = styled.div`
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    padding: 13px 14px;
    border: 1px dashed var(--border-color-hover);
    border-radius: var(--radius-md);
    background: rgba(16, 12, 9, 0.6);
`;

const Code = styled.strong`
    font-family: var(--font-mono);
    font-size: 21px;
    font-weight: 500;
    letter-spacing: 0.13em;
    color: var(--accent-primary);
`;

const CodeButton = styled.button`
    display: grid;
    place-items: center;
    width: 28px;
    height: 28px;
    padding: 0;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    background: transparent;
    color: var(--text-secondary);
    cursor: pointer;

    &:hover {
        border-color: var(--border-color-hover);
        background: var(--bg-card-hover);
        color: var(--accent-primary);
    }
`;

const Hint = styled.div`
    display: flex;
    align-items: flex-start;
    gap: 7px;
    font-size: 9.5px;
    line-height: 1.6;
    color: var(--text-muted);

    svg {
        flex-shrink: 0;
        margin-top: 1px;
    }
`;

const Actions = styled.div`
    display: flex;
    justify-content: flex-end;
    gap: 8px;
`;

const RejectButton = styled.button`
    padding: 6px 12px;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    background: transparent;
    color: var(--text-secondary);
    font: inherit;
    font-size: 10.5px;
    cursor: pointer;

    &:hover {
        border-color: rgba(255, 107, 107, 0.5);
        color: var(--danger);
    }
`;

const AcceptButton = styled.button`
    padding: 6px 12px;
    border: 1px solid transparent;
    border-radius: var(--radius-sm);
    background: var(--accent-gradient);
    color: #241206;
    font: inherit;
    font-size: 10.5px;
    font-weight: 600;
    cursor: pointer;
`;

const Waiting = styled.p`
    font-family: var(--font-mono);
    font-size: 9px;
    color: var(--text-muted);
`;

const Error = styled.p`
    padding: 8px 10px;
    border: 1px solid rgba(255, 107, 107, 0.45);
    border-radius: var(--radius-sm);
    background: rgba(255, 107, 107, 0.08);
    color: var(--danger);
    font-size: 10px;
`;

const DeviceList = styled.div`
    display: flex;
    flex-direction: column;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    overflow: hidden;
`;

const DiscoveredRow = styled.div`
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
    padding: 8px 10px;
    border-bottom: 1px solid var(--border-color);

    &:last-child {
        border-bottom: 0;
    }
`;

const DiscoveredText = styled.div`
    display: flex;
    flex-direction: column;
    gap: 1px;
    min-width: 0;

    strong {
        font-size: 11px;
        font-weight: 500;
    }

    span {
        font-family: var(--font-mono);
        font-size: 8.5px;
        color: var(--text-muted);
    }
`;

const SmallPair = styled.button`
    flex-shrink: 0;
    padding: 4px 9px;
    border: 1px solid rgba(255, 138, 61, 0.4);
    border-radius: var(--radius-sm);
    background: rgba(255, 138, 61, 0.08);
    color: var(--accent-primary);
    font: inherit;
    font-size: 9.5px;
    cursor: pointer;

    &:hover:not(:disabled) {
        background: rgba(255, 138, 61, 0.16);
    }

    &:disabled {
        opacity: 0.4;
        cursor: default;
    }
`;

const EmptyDiscovered = styled.div`
    display: flex;
    align-items: center;
    gap: 7px;
    padding: 12px 10px;
    border: 1px dashed var(--border-color);
    border-radius: var(--radius-sm);
    font-size: 9.5px;
    color: var(--text-muted);
`;

const AddressRow = styled.div`
    display: flex;
    gap: 6px;
`;

const AddressInput = styled.input`
    flex: 1;
    min-width: 0;
    padding: 6px 9px;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    outline: 0;
    background: var(--bg-input);
    color: var(--text-primary);
    font-family: var(--font-mono);
    font-size: 10px;

    &:focus {
        border-color: var(--border-color-focus);
    }
`;
