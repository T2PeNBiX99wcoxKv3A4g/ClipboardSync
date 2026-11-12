interface IconProps {
    size?: number;
}

const base = (size: number) => ({
    width: size,
    height: size,
    viewBox: "0 0 24 24",
    fill: "currentColor",
    stroke: "none",
    "aria-hidden": true,
});

/* Solid pictograms with cut-out details; the negative space follows the background. */

export function HomeIcon({size = 18}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd"
                  d="M12 2.4 2.6 10.2a1 1 0 0 0-.6.9V21a1 1 0 0 0 1 1h7v-7h4v7h7a1 1 0 0 0 1-1v-9.9a1 1 0 0 0-.6-.9zM11 15h2v7h-2z"/>
        </svg>
    );
}

export function ClipboardIcon({size = 18}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M9 2h6a1 1 0 0 1 1 1v2H8V3a1 1 0 0 1 1-1z"/>
            <path fillRule="evenodd"
                  d="M6 4h4v2h4V4h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zm2 8v2h8v-2zm0 4v2h5v-2z"/>
        </svg>
    );
}

export function DevicesIcon({size = 18}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M2 4h15a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z"/>
            <path d="M6 18h7v2H6zM9 15h1v3H9z"/>
            <path fillRule="evenodd"
                  d="M17 10h5a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2h-5a2 2 0 0 1-2-2V12a2 2 0 0 1 2-2zm1 2v7h3v-7z"/>
        </svg>
    );
}

export function SlidersIcon({size = 18}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd" d="M3 6h18v2H3zM6 3a3 3 0 1 0 0 6 3 3 0 0 0 0-6z"/>
            <path fillRule="evenodd" d="M3 16h18v2H3zM15 15a3 3 0 1 0 0 6 3 3 0 0 0 0-6z"/>
        </svg>
    );
}

export function SearchIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd"
                  d="M10.5 2a8.5 8.5 0 1 0 5.3 15.1l4.1 4.1 2.1-2.1-4.1-4.1A8.5 8.5 0 0 0 10.5 2zm0 3a5.5 5.5 0 1 1 0 11 5.5 5.5 0 0 1 0-11z"/>
        </svg>
    );
}

export function CopyIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M8 2h9a2 2 0 0 1 2 2v12h-3V5H8z"/>
            <path fillRule="evenodd"
                  d="M5 6h10a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2zm2 4v2h6v-2zm0 4v2h4v-2z"/>
        </svg>
    );
}

export function CheckIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd"
                  d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm4.7 7.3-5.7 5.7-3.3-3.3 1.4-1.4 1.9 1.9 4.3-4.3z"/>
        </svg>
    );
}

export function TrashIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M9 2h6v2H9z"/>
            <path fillRule="evenodd"
                  d="M4 5h16v2H4zM6 8h12l-1 13a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2zm4 3v8h2v-8zm4 0v8h2v-8z"/>
        </svg>
    );
}

export function PinIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M9 2h6l-1 6 4 4H6l4-4zM11 12h2v10h-2z"/>
        </svg>
    );
}

export function LinkIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path
                d="M9 6h3V4H9a5 5 0 0 0 0 10h3v-2H9a3 3 0 0 1 0-6zM12 10h3v2h-3zM12 6h3a5 5 0 0 1 0 10h-3v-2h3a3 3 0 0 0 0-6h-3z"/>
        </svg>
    );
}

export function ImageIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd"
                  d="M3 3h18a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zm2 4v10h14V7zm2 2h3v3H7zm2 7 3-4 2 2.5 2-2.5 3 4z"/>
        </svg>
    );
}

export function FileIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd"
                  d="M6 2h8l6 6v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2zm7 2v5h5zM8 12v2h8v-2zm0 4v2h5v-2z"/>
        </svg>
    );
}

export function TextIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd"
                  d="M4 3h16a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zm3 4v2h10V7zm0 4v2h10v-2zm0 4v2h6v-2z"/>
        </svg>
    );
}

export function PlusIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd" d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm-1 5h2v4h4v2h-4v4h-2v-4H7v-2h4z"/>
        </svg>
    );
}

export function RefreshIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M12 4a8 8 0 0 1 7.4 5H22l-4.2 5L14 9h2.1A6 6 0 0 0 6 12H4a8 8 0 0 1 8-8z"/>
            <path d="M12 20a8 8 0 0 1-7.4-5H2l4.2-5L10 15H7.9A6 6 0 0 0 18 12h2a8 8 0 0 1-8 8z"/>
        </svg>
    );
}

export function LaptopIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd"
                  d="M4 4h16a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zm1 3v7h14V7zM2 19h20v2H2z"/>
        </svg>
    );
}

export function MonitorIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd"
                  d="M3 3h18a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zm1 3v9h16V6zM8 20h8v2H8z"/>
        </svg>
    );
}

export function PhoneIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd"
                  d="M7 2h10a2 2 0 0 1 2 2v16a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2zm1 3v12h8V5zM10 19h4v2h-4z"/>
        </svg>
    );
}

export function ClockIcon({size = 14}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd" d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm-1 4h2v6l4 2-1 1.7-5-2.7z"/>
        </svg>
    );
}

export function XIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M5.6 4.2 12 10.6l6.4-6.4 1.6 1.6L13.6 12l6.4 6.4-1.6 1.6L12 13.6l-6.4 6.4-1.6-1.6L10.4 12 4 5.8z"/>
        </svg>
    );
}

export function DownloadIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M11 3h2v10l4-4 1.4 1.4L12 16.8 5.6 10.4 7 9l4 4z"/>
            <path d="M4 18h16v3H4z"/>
        </svg>
    );
}

export function UploadIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M11 21h2V11l4 4 1.4-1.4L12 7.2l-6.4 6.4L7 15l4-4z"/>
            <path d="M4 3h16v3H4z"/>
        </svg>
    );
}

export function LockIcon({size = 14}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M8 10V7a4 4 0 0 1 8 0v3h2v11H6V10zm2 0h4V7a2 2 0 0 0-4 0z"/>
            <path fillRule="evenodd" d="M12 13a2 2 0 0 1 1 3.7V19h-2v-2.3a2 2 0 0 1 1-3.7z"/>
        </svg>
    );
}

export function ShieldIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd"
                  d="M12 2 21 5v6c0 5-3.4 9-9 11C6.4 20 3 16 3 11V5zm-1 7-3.5 3.5 4.5 4.5 6-7-1.6-1.4-4.4 5.1-2.8-2.8z"/>
        </svg>
    );
}

export function FolderIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M3 5h6l2 2h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z"/>
        </svg>
    );
}

export function ExternalIcon({size = 14}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M13 3h8v8h-3V8.4l-6.3 6.3-2.1-2.1L15.9 6H13zM5 5h6v3H8v8h8v-3h3v6H5z"/>
        </svg>
    );
}

export function InfoIcon({size = 16}: IconProps) {
    return (
        <svg {...base(size)}>
            <path fillRule="evenodd" d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm-1 5h2v2h-2zm0 4h2v6h-2z"/>
        </svg>
    );
}

export function ChevronDownIcon({size = 14}: IconProps) {
    return (
        <svg {...base(size)}>
            <path d="M5.6 8.2 12 14.6l6.4-6.4 1.6 1.6L12 18.4 3.9 9.8z"/>
        </svg>
    );
}
