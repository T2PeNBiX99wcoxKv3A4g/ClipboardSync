# Security

## Identity

Each installation generates a long-term Ed25519 key pair. The public key is the device's identity. When two devices
pair, each pins the other's public key and uses it from then on.

The private key stays in the app's settings folder. Anyone who can read that folder can act as the device, so protect
your user account.

## Pairing

The pairing handshake proves that both sides own the key they claim. The short code shown on both screens is derived
from the same transcript, so comparing the two codes catches a mismatch or a device in the middle.

The code is not a password and is never sent over the network.

## Sessions

Each connection generates fresh X25519 ephemeral keys. The shared secret goes through HKDF-SHA256 to derive a
per-session key, and every frame is encrypted with ChaCha20-Poly1305. The handshake transcript is used as associated
data, which ties each frame to that session.

A new session is created for every connection; compromising one session does not expose earlier or later ones.

## What is protected

- Clipboard items — text, URLs, images, files and folders — are encrypted on the wire. File contents are split into
  blob ranges before they are framed.
- Pairing proofs and session frames are authenticated, so tampering is detected.

The blob store on each device is plaintext on disk. Encryption protects the transfer, not the local copies; protect the
user account and the data folder if the files are sensitive.

Discovery is not secret: a UDP announcement carries the device name, id and port so other devices can find it. The
network can see that two addresses are talking and how much data they exchange, but not the clipboard contents.

## Trust on first use

Pairing is the moment of trust. If a device's key changes later, the pinned key no longer matches and the device must be
paired again. Compare the pairing code every time; do not accept a request you did not expect.
