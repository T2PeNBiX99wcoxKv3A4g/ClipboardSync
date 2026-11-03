# Syncing

## What is shared

| Clipboard content | Status |
| --- | --- |
| Text | Shared |
| URLs | Shared as text |
| Images | Shared as PNG |
| Files | Not yet |

Files copied in a file manager are ignored for now. Images are capped by the **Image size limit** in Settings (1–32 MB, 10 MB by default); a larger image is skipped and stays out of the history.

## The board

The **Devices** page is the shared board. Every device appears with the items that came from it, including this device. Each item shows a transfer state:

| State | Meaning |
| --- | --- |
| Synced | Every paired device has acknowledged the item. |
| Sending | This device is still delivering it. |
| Receiving | The item is arriving from another device. |
| Waiting | A peer is offline; delivery will be retried. |
| Failed | The last attempt did not complete. |

The line under an item counts the devices that hold it: `3 of 4 devices have this item.`

## What happens on a copy

1. The clipboard watcher stores the item with this device as its origin.
2. It is sent to every paired device over an encrypted session.
3. Each device stores it, applies it to its clipboard and relays it to peers that still need it.
4. A periodic reconciliation resends anything a peer has not acknowledged.

The watcher ignores the clipboard changes ClipboardSync itself makes, so applying an incoming item does not send it back.

## Offline devices

A device that is offline keeps its own copy and shows **Waiting** for items it has not received. When it reconnects, the reconciliation and the catch-up after pairing fill in the missing items. Nothing needs to be copied again.

## Turning sync off

**Settings → Sync → Enable syncing** stops sending and receiving but keeps the pairing. Turn on **Text and links** or **Images** to choose what is shared.
