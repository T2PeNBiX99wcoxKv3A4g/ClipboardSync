# Settings

The Settings page is split into four sections. The section list on the right follows the page as you scroll.

## General

- **Device name** — the name other devices see. It is also used in discovery announcements.
- **Language** — English, Japanese, Simplified Chinese or Traditional Chinese. It applies immediately, even before the
  rest of the form is saved.
- **Start with the window hidden** — launch into the tray.
- **Launch at startup** — add a per-user autostart entry. The toggle is hidden or disabled on platforms without one.

## Sync

- **Enable syncing** — the master switch. Off keeps the pairing but stops the clipboard watcher, the listener and
  discovery.
- **Text and links** — share copied text, including URLs and code.
- **Images** — share image copies as PNG.
- **Files and folders** — share copied files and folders. Folders are walked recursively.
- **Image size limit** — 1 to 32 MB, 10 MB by default. Larger images are ignored.
- **File size limit** — 1 to 4096 MB, 512 MB by default. One selection larger than this is skipped.
- **Blob storage folder** — where file blobs live. Leave it empty for the app data folder; changing it moves
  the existing blobs.

## History

- **Items to keep** — the local history limit, 200 by default. Older items are removed once the limit is reached.
- **Clear history** — clears the board on every paired device. The system clipboard is not changed.

## About

- **Version** — the build and platform.
- **Updates** — compare this build with the published `.version` file and open the release page.
- **Logs** — the log folder, with a button to open it.

## Saving

- **Save changes** applies the form.
- **Discard changes** restores the last saved values. It only appears when something has changed.
- **Restore defaults** fills the form with the defaults; it does not save until **Save changes** is pressed.

The language dropdown is the exception: it saves at once, so the interface and tray menu switch without waiting.
