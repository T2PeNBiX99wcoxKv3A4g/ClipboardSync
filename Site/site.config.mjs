/**
 * Everything about this project that is not text: where it lives, how it looks, and which pages the
 * documentation holds. Plain JavaScript, because the generators read it with node.
 */
export const site = {
    name: "ClipboardSync",
    tagline: "Copy once, paste on every device you own.",
    description: "ClipboardSync shares text, links, images, files and folders between your own devices over an encrypted peer-to-peer connection.",
    url: "https://t2penbix99wcoxkv3a4g.github.io/ClipboardSync/",
    icon: "./icon.svg",
    repository: "https://github.com/T2PeNBiX99wcoxKv3A4g/ClipboardSync",
    languages: ["en", "ja", "zh-CN", "zh-TW"],
    docs: [
        {
            "slug": "Getting-Started",
            "group": "setup"
        },
        {
            "slug": "Pairing",
            "group": "setup"
        },
        {
            "slug": "Syncing",
            "group": "setup"
        },
        {
            "slug": "Settings",
            "group": "reference"
        },
        {
            "slug": "Security",
            "group": "reference"
        },
        {
            "slug": "Troubleshooting",
            "group": "reference"
        }
    ],
    theme: {
        "accent": "#ff9a5c",
        "accentStrong": "#ff8a3d",
        "background": "#0a0908",
        "card": "rgba(28, 24, 20, 0.78)",
        "topBar": "rgba(10, 9, 8, 0.82)",
        "textPrimary": "#fff8f0",
        "textSecondary": "#c6b6a6",
        "textMuted": "#806e5d",
        "body": "#d9cdc1",
        "border": "rgba(255, 138, 61, 0.14)",
        "borderHover": "rgba(255, 138, 61, 0.42)"
    },
};
