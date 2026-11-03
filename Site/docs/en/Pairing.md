# Pairing

Pairing is the one-time step that lets two devices trust each other. It proves that both sides own their key, and it
pins the other device's public key. Only pinned devices exchange clipboard items.

## Before you start

- ClipboardSync is running on both devices, and syncing is on.
- Both devices are on the same local network, or can reach each other by address.
- TCP 45456 is allowed through the firewall on both sides. UDP 45457 is used only to announce presence.

## Discover a device

Open **Devices → Pair a device**. The **Discovered devices** list fills from the local network within a few seconds.
Pick the other device and press **Pair**.

Discovery uses a UDP broadcast, so it does not cross routers. If the list stays empty, use an address.

## Enter an address

Type the other device's host name or IP address. The default port is 45456; add `:port` if you changed it.

```
192.168.1.20
fedora-desktop:45456
```

The address field works in both directions: either device can start the pairing.

## Compare the code

Both screens show the same short **Pairing code**, derived from the handshake transcript. Check that the two codes
match, then press **Accept** on the device that is waiting for confirmation. The code is never sent as a password; it
only lets you verify that no one sat in the middle.

If the codes differ, press **Reject** and try again. A mismatch means the two sides did not complete the same handshake.

## Incoming requests

When the other device starts pairing, the request opens on this device with the same code. Compare it and press
**Accept** or **Reject**.

## Unpair

Open **Devices**, find the device and press **Unpair**. The other device stops receiving new items. Items already on the
board stay there, because the board is local history, not a shared account.

Pairing again later pins the key again and sends the current board as a catch-up.
