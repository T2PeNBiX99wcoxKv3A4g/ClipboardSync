/** The app mark, drawn from the same geometry as `icon.svg`, so the UI never needs a bitmap. */
export function BrandMark({size = 20}: { size?: number }) {
    return (
        <svg aria-hidden="true" height={size} viewBox="0 0 256 256" width={size}>
            <defs>
                <linearGradient id="brand-mark" gradientUnits="userSpaceOnUse" x1="8" x2="248" y1="8" y2="248">
                    <stop offset="0" stopColor="#ff3d6e"/>
                    <stop offset="1" stopColor="#ff9a3d"/>
                </linearGradient>
            </defs>
            <rect fill="url(#brand-mark)" height="240" rx="54" width="240" x="8" y="8"/>
            <rect fill="#fff8f0" height="156" rx="22" width="124" x="66" y="52"/>
            <rect fill="#ffd166" height="30" rx="12" width="52" x="102" y="36"/>
            <path d="M82 112h78" stroke="#ff6b3d" strokeLinecap="round" strokeWidth="14"/>
            <path d="M158 88l28 24-28 24z" fill="#ff6b3d"/>
            <path d="M174 152H96" stroke="#ff6b3d" strokeLinecap="round" strokeWidth="14"/>
            <path d="M98 128 70 152l28 24z" fill="#ff6b3d"/>
        </svg>
    );
}
