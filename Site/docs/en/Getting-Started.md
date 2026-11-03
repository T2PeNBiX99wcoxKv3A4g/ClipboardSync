# Getting started

ClipboardSync keeps the clipboard of the devices you pair on the same page. Copy text, a link or an image on one
machine, and the others get it as well. Everything travels directly between the devices over an encrypted session.

## What you need

- Windows or Linux, x64. The build is self-contained, so the .NET runtime does not need to be installed.
- Two devices that can reach each other. TCP 45456 carries pairing and clipboard traffic; UDP 45457 is only the
  local-network discovery hint.
- On Linux, WebKitGTK 4.1 and an AppIndicator library. Without the AppIndicator library the app still runs; only the
  tray icon is missing.

## Run it

Start `ClipboardSync.exe` (or the Linux binary). The window opens; closing it leaves the app in the tray. Only one copy
runs at a time, so starting it again brings the existing window back.

## Pair the first device

Open **Devices → Pair a device**. Pick a device that discovery found, or type an address, then compare the code shown on
both screens. See [Pairing](Pairing).

## Copy something

Copy text, a URL or an image in any application. The item appears on the board under this device, is sent to the paired
devices, and is applied to their clipboard. See [Syncing](Syncing) for what is shared and what happens when a device is
offline.

## Where the data lives

| What                               | Windows                        | Linux                          |
|------------------------------------|--------------------------------|--------------------------------|
| Settings, identity, paired devices | `%APPDATA%\ClipboardSync`      | `~/.config/ClipboardSync`      |
| History and image cache            | `%LOCALAPPDATA%\ClipboardSync` | `~/.local/share/ClipboardSync` |
| Logs                               | `%APPDATA%\ClipboardSync\logs` | `~/.config/ClipboardSync/logs` |

The identity key is what other devices pin when they pair, so keep that folder private to your account.

## Next

- [Pairing](Pairing) — discovery, addresses and the code.
- [Syncing](Syncing) — the board, transfer states and retries.
- [Security](Security) — what the encryption covers.
