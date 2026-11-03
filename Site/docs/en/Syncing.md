# Syncing

## What is shared

| Clipboard content | Status         |
|-------------------|----------------|
| Text              | Shared         |
| URLs              | Shared as text |
| Images            | Shared as PNG  |
| Files and folders | Shared as blobs |

Images are capped by the **Image size limit** in Settings (1–32 MB, 10 MB by default). Files and folders are walked
recursively; one selection is capped by the **File size limit** (512 MB by default) and 1,000 entries.

## The board

The **Devices** page is the shared board. Every device appears with the items that came from it, including this device.
Each item shows a transfer state:

| State     | Meaning                                        |
|-----------|------------------------------------------------|
| Synced    | Every paired device has acknowledged the item. |
| Sending   | This device is still delivering it.            |
| Receiving | The item is arriving from another device.      |
| Waiting   | A peer is offline; delivery will be retried.   |
| Failed    | The last attempt did not complete.             |

The line under an item counts the devices that hold it: `3 of 4 devices have this item.`

## What happens on a copy

1. The clipboard watcher stores the item with this device as its origin.
2. It is sent to every paired device over an encrypted session.
3. Each device stores it, applies it to its clipboard and relays it to peers that still need it.
4. A periodic reconciliation resends anything a peer has not acknowledged.

A file is stored once by its SHA-256, so copies with the same contents share one blob. The receiver asks only for the
ranges it does not have; if the connection drops, the next attempt continues from the partial blob. The view that is put
back on the clipboard is hardlinked to the blobs where the filesystem allows it.

The watcher ignores the clipboard changes ClipboardSync itself makes, so applying an incoming item does not send it
back.

## Offline devices

A device that is offline keeps its own copy and shows **Waiting** for items it has not received. When it reconnects, the
reconciliation and the catch-up after pairing fill in the missing items; a partial file transfer resumes from the bytes
already on disk. Nothing needs to be copied again.

## Turning sync off

**Settings → Sync → Enable syncing** stops sending and receiving but keeps the pairing. Turn on **Text and links**,
**Images** or **Files and folders** to choose what is shared.
