import {useEffect, useRef, useState} from "react";
import styled, {keyframes} from "styled-components";
import {CheckIcon, ChevronDownIcon} from "./Icons";

export interface DropdownOption {
    id: string;
    label: string;
}

interface DropdownProps {
    value: string;
    options: DropdownOption[];
    onChange: (id: string) => void;
    label: string;
}

const dropIn = keyframes`
    from {
        opacity: 0;
        transform: translateY(-4px) scale(0.985);
    }
    to {
        opacity: 1;
        transform: translateY(0) scale(1);
    }
`;

/** A compact custom listbox; native selects cannot be styled consistently in the web view. */
export function Dropdown({value, options, onChange, label}: DropdownProps) {
    const [open, setOpen] = useState(false);
    const root = useRef<HTMLDivElement>(null);
    const selected = options.find((option) => option.id === value) ?? options[0];

    useEffect(() => {
        if (!open) return;

        const onPointerDown = (event: MouseEvent) => {
            if (root.current && !root.current.contains(event.target as Node))
                setOpen(false);
        };

        const onKeyDown = (event: KeyboardEvent) => {
            if (event.key === "Escape") setOpen(false);
        };

        document.addEventListener("mousedown", onPointerDown);
        document.addEventListener("keydown", onKeyDown);

        return () => {
            document.removeEventListener("mousedown", onPointerDown);
            document.removeEventListener("keydown", onKeyDown);
        };
    }, [open]);

    const choose = (id: string) => {
        onChange(id);
        setOpen(false);
    };

    return (
        <Root ref={root}>
            <Trigger
                aria-expanded={open}
                aria-haspopup="listbox"
                aria-label={label}
                onClick={() => setOpen((current) => !current)}
                onKeyDown={(event) => {
                    if (event.key === "ArrowDown") {
                        event.preventDefault();
                        setOpen(true);
                    }
                }}
                type="button"
            >
                <span>{selected?.label}</span>
                <Chevron $open={open}>
                    <ChevronDownIcon size={14}/>
                </Chevron>
            </Trigger>

            {open && (
                <Panel aria-label={label} role="listbox">
                    {options.map((option) => {
                        const active = option.id === value;

                        return (
                            <Option
                                $active={active}
                                aria-selected={active}
                                key={option.id}
                                onClick={() => choose(option.id)}
                                role="option"
                                type="button"
                            >
                                <span>{option.label}</span>
                                {active && <CheckIcon size={13}/>}
                            </Option>
                        );
                    })}
                </Panel>
            )}
        </Root>
    );
}

const Root = styled.div`
    position: relative;
    width: 150px;
`;

const Trigger = styled.button`
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    width: 100%;
    padding: 6px 9px;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    background: var(--bg-input);
    color: var(--text-primary);
    font: inherit;
    font-size: 11px;
    text-align: left;
    cursor: pointer;
    transition: border-color 120ms ease, background 120ms ease;

    &:hover,
    &[aria-expanded="true"] {
        border-color: var(--border-color-hover);
        background: var(--bg-card-hover);
    }

    &:focus-visible {
        outline: 2px solid var(--border-color-focus);
        outline-offset: 1px;
    }
`;

const Chevron = styled.span<{ $open: boolean }>`
    display: grid;
    place-items: center;
    color: var(--text-muted);
    transform: rotate(${({$open}) => $open ? 180 : 0}deg);
    transition: transform 160ms cubic-bezier(0.22, 1, 0.36, 1);
`;

const Panel = styled.div`
    position: absolute;
    top: calc(100% + 5px);
    right: 0;
    z-index: 50;
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 150px;
    width: max(100%, 150px);
    padding: 4px;
    border: 1px solid var(--border-color-hover);
    border-radius: var(--radius-md);
    background: var(--bg-secondary);
    box-shadow: 0 16px 45px rgba(0, 0, 0, 0.55);
    animation: ${dropIn} 140ms cubic-bezier(0.22, 1, 0.36, 1);
`;

const Option = styled.button<{ $active: boolean }>`
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
    padding: 6px 8px;
    border: 0;
    border-radius: var(--radius-sm);
    background: ${({$active}) => $active ? "rgba(255, 138, 61, 0.14)" : "transparent"};
    color: ${({$active}) => $active ? "var(--accent-primary)" : "var(--text-secondary)"};
    font: inherit;
    font-size: 11px;
    text-align: left;
    cursor: pointer;
    transition: background 100ms ease, color 100ms ease;

    &:hover {
        background: var(--bg-card-hover);
        color: var(--text-primary);
    }

    &:focus-visible {
        outline: 2px solid var(--border-color-focus);
        outline-offset: -1px;
    }
`;
