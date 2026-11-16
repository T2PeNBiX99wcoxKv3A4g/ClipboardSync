import {useState} from "react";
import styled from "styled-components";
import {ATTRIBUTIONS} from "../data/attributions";

/** The open-source notices: a scrollable index on the left and the selected licence on the right. */
export function AttributionBrowser() {
    const [selected, setSelected] = useState(ATTRIBUTIONS[0]);

    return (
        <Browser>
            <Index>
                {ATTRIBUTIONS.map((entry) => (
                    <IndexItem
                        $active={entry.name === selected.name}
                        key={`${entry.name}@${entry.version}`}
                        onClick={() => setSelected(entry)}
                        type="button"
                    >
                        <strong>{entry.name}</strong>
                        <span>{meta(entry)}</span>
                    </IndexItem>
                ))}
            </Index>

            <License>
                <LicenseHead>
                    <strong>{selected.name}</strong>
                    <span>{meta(selected)}</span>
                </LicenseHead>
                <Text>{selected.text}</Text>
            </License>
        </Browser>
    );
}

const meta = (entry: { version: string; license: string }) =>
    [entry.version, entry.license].filter(Boolean).join(" · ");

const Browser = styled.div`
    display: grid;
    grid-template-columns: minmax(190px, 0.75fr) minmax(0, 1.7fr);
    gap: 12px;
    flex: 1;
    min-height: 0;
`;

const Index = styled.div`
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-height: 0;
    overflow-y: auto;
    padding: 4px;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    background: var(--bg-input);
`;

const IndexItem = styled.button<{ $active: boolean }>`
    display: flex;
    flex-direction: column;
    gap: 1px;
    padding: 6px 8px;
    border: 1px solid transparent;
    border-radius: 6px;
    background: ${({$active}) => $active ? "rgba(255, 138, 61, 0.14)" : "transparent"};
    color: var(--text-primary);
    font: inherit;
    text-align: left;
    cursor: pointer;

    strong {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font-size: 11px;
        font-weight: 600;
    }

    span {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font-family: var(--font-mono);
        font-size: 9px;
        color: var(--text-muted);
    }

    &:hover {
        background: var(--bg-card-hover);
    }
`;

const License = styled.div`
    display: flex;
    flex-direction: column;
    min-height: 0;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    background: var(--bg-input);
    overflow: hidden;
`;

const LicenseHead = styled.div`
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 10px;
    padding: 8px 10px;
    border-bottom: 1px solid var(--border-color);

    strong {
        font-size: 11px;
    }

    span {
        font-family: var(--font-mono);
        font-size: 9px;
        color: var(--text-muted);
    }
`;

const Text = styled.pre`
    flex: 1;
    min-height: 0;
    margin: 0;
    overflow: auto;
    padding: 10px;
    color: var(--text-secondary);
    font-family: var(--font-mono);
    font-size: 9.5px;
    line-height: 1.6;
    white-space: pre-wrap;
    overflow-wrap: anywhere;
    user-select: text;
`;
