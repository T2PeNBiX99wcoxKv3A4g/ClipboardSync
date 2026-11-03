import styled from "styled-components";

interface ToggleProps {
    checked: boolean;
    onChange: (checked: boolean) => void;
    disabled?: boolean;
    label: string;
}

/** A small switch used by the settings and device lists. */
export function Toggle({checked, onChange, disabled = false, label}: ToggleProps) {
    return (
        <Switch
            $checked={checked}
            aria-checked={checked}
            aria-label={label}
            disabled={disabled}
            onClick={() => onChange(!checked)}
            role="switch"
            type="button"
        >
            <Knob/>
        </Switch>
    );
}

const Switch = styled.button<{ $checked: boolean }>`
    position: relative;
    width: 36px;
    height: 20px;
    flex-shrink: 0;
    padding: 0;
    border: 1px solid ${({$checked}) => $checked ? "transparent" : "var(--border-color-hover)"};
    border-radius: 999px;
    background: ${({$checked}) => $checked ? "var(--accent-gradient)" : "rgba(16, 12, 9, 0.9)"};
    cursor: pointer;
    transition: background 160ms ease, border-color 160ms ease, opacity 160ms ease;

    &:disabled {
        opacity: 0.4;
        cursor: default;
    }

    &:focus-visible {
        outline: 2px solid var(--border-color-focus);
        outline-offset: 2px;
    }
`;

const Knob = styled.span`
    position: absolute;
    top: 2px;
    left: 2px;
    width: 14px;
    height: 14px;
    border-radius: 50%;
    background: #f8fafc;
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.4);
    transition: transform 160ms cubic-bezier(0.22, 1, 0.36, 1);

    ${Switch}[aria-checked="true"] & {
        transform: translateX(16px);
    }
`;
