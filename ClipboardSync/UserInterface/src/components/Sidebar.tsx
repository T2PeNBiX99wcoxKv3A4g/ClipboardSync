import styled, {css} from "styled-components";
import {useTranslation} from "@photino-net-utils/bridge";
import type {Page} from "../pages";
import {DevicesIcon, SlidersIcon} from "./Icons";

interface SidebarProps {
    page: Page;
    onNavigate: (page: Page) => void;
}

const items: { id: Page; label: string; icon: typeof DevicesIcon }[] = [
    {id: "devices", label: "Devices", icon: DevicesIcon},
    {id: "settings", label: "Settings", icon: SlidersIcon},
];

export function Sidebar({page, onNavigate}: SidebarProps) {
    const t = useTranslation();

    return (
        <Aside>
            <Nav>
                {items.map(({id, label, icon: Icon}) => (
                    <Item key={id} $active={page === id} onClick={() => onNavigate(id)} type="button">
                        <Icon size={18}/>
                        <span>{t(label)}</span>
                    </Item>
                ))}
            </Nav>
        </Aside>
    );
}

const Aside = styled.aside`
    display: flex;
    flex-direction: column;
    gap: 22px;
    width: 218px;
    flex-shrink: 0;
    padding: 16px 14px 18px;
    background: rgba(12, 10, 8, 0.9);
    border-right: 1px solid var(--border-color);
`;

const Nav = styled.nav`
    display: flex;
    flex-direction: column;
    gap: 3px;
`;

const Item = styled.button<{ $active: boolean }>`
    position: relative;
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 9px 10px;
    border: 1px solid transparent;
    border-radius: var(--radius-sm);
    background: ${({$active}) => $active ? "rgba(255, 138, 61, 0.12)" : "transparent"};
    color: ${({$active}) => $active ? "var(--text-primary)" : "var(--text-secondary)"};
    font: inherit;
    font-size: 13px;
    text-align: left;
    cursor: pointer;
    transition: background 120ms ease, border-color 120ms ease, color 120ms ease;

    &::before {
        content: "";
        position: absolute;
        left: 0;
        top: 50%;
        width: 2px;
        height: 0;
        border-radius: 2px;
        background: var(--accent-primary);
        transform: translateY(-50%);
        transition: height 160ms cubic-bezier(0.22, 1, 0.36, 1);
    }

    ${({$active}) => $active && css`
        &::before {
            height: 55%;
        }
    `}

    svg {
        color: ${({$active}) => $active ? "var(--accent-primary)" : "var(--text-muted)"};
        transition: color 120ms ease, transform 160ms cubic-bezier(0.22, 1, 0.36, 1);
        transform: scale(${({$active}) => $active ? 1.08 : 1});
    }

    &:hover {
        background: var(--bg-card-hover);
        border-color: var(--border-color);
        color: var(--text-primary);

        svg {
            transform: scale(1.08);
        }
    }

    &:active {
        transform: scale(0.99);
    }

    &:focus-visible {
        outline: 2px solid var(--border-color-focus);
        outline-offset: 1px;
    }
`;
