# Troubleshooting

## The other device is not discovered

- Both devices are on the same subnet. Discovery does not cross routers.
- UDP 45457 is allowed through the firewall.
- Syncing is enabled on both devices.
- If broadcast is blocked, type the address under **Pair a device → Enter an address**.

## Pairing fails or the codes differ

- Both devices run the same version and both are running.
- TCP 45456 is open on the receiving device.
- The code is derived from the handshake, so a mismatch means the handshake did not complete. Reject and start again.
- A device whose key changed must be paired again; the old pin no longer matches.

## Items stay on Waiting or Failed

- The peer is offline or cannot be reached. Check the address and the firewall.
- Delivery is retried automatically, and pairing again sends the current board as a catch-up.
- **Failed** only describes the last attempt; a later retry can still succeed.

## Nothing is sent

- **Settings → Sync → Enable syncing** is on.
- **Text and links** or **Images** is on for the content you copied.
- The item is under the **Image size limit**.

## Images are not synced

- Images are sent as PNG. A copy that only exposes a file list is treated as files, which are not supported yet.
- Check the size limit when a large screenshot is skipped.

## The history is shorter than expected

- **Settings → History → Items to keep** caps the local history.
- A device that was offline fills in missing items after it reconnects; give it a moment.

## Logs

Open **Settings → About → Open logs**. The folder contains the current log file and any earlier ones.
