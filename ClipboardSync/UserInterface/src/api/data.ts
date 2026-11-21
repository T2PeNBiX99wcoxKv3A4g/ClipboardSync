import {
    type DataBlock as SharedDataBlock,
    DataClient,
    decodeDataBlock,
} from "@photino-net-utils/bridge";

/** The host's local data plane: binary list blocks and raw files. */
export const data = new DataClient({baseMessage: "app.data"});

export type DataBlock = SharedDataBlock;

/** Where the host's data routes live. Asked once, then kept for the life of the page. */
export function dataBase(): Promise<string> {
    return data.base();
}

/** Fetches and decodes one block from the host. */
export function readBlock(path: string): Promise<DataBlock> {
    return data.readBlock(path);
}

/** Reads the block itself, for a caller that already has the bytes. */
export const decodeBlock = decodeDataBlock;

/** A loopback URL for a stored file or image. */
export function dataUrl(path: string): Promise<string> {
    return data.url(path);
}

/** Fetches raw bytes from the host. */
export function readBytes(path: string): Promise<Uint8Array> {
    return data.bytes(path);
}
