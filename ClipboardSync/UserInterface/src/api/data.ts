import {Native} from "@photino-net-utils/bridge";

/**
 * The lists the window draws come from the host's local server as binary blocks, not from the
 * message bridge: a block is a header, the column names, the rows and one UTF-8 pool, so the page
 * copies the bytes once and decodes only the strings it shows.
 */

/** "YABS" */
const Magic = 0x53424159;
const Version = 1;
const HeaderSize = 32;

const Utf8 = new TextDecoder();

let base: Promise<string> | null = null;

/** Where the host's data routes live. Asked once, then kept for the life of the page. */
export function dataBase(): Promise<string> {
    if (!Native.available) return Promise.resolve("");

    base ??= Native.request<{ base: string }>("app.data").then(
        (payload) => payload.base ?? "",
        () => {
            // Ask again next time instead of remembering a failure forever.
            base = null;
            return "";
        },
    );

    return base;
}

export interface DataBlock {
    kind: number;
    meta: Record<string, string>;
    /** One object per row: the text columns as strings, the number columns as numbers. */
    rows: Record<string, string | number>[];
}

/** Fetches and decodes one block from the host. */
export async function readBlock(path: string): Promise<DataBlock> {
    const root = await dataBase();

    if (!root) throw new Error("The data server is not running.");

    const response = await fetch(`${root}${path}`, {cache: "no-cache"});

    if (!response.ok) throw new Error(`The data request failed (${response.status}).`);

    return decodeBlock(await response.arrayBuffer());
}

/** Reads the block itself, for a caller that already has the bytes. */
export function decodeBlock(buffer: ArrayBuffer): DataBlock {
    const view = new DataView(buffer);

    if (buffer.byteLength < HeaderSize || view.getUint32(0, true) !== Magic)
        throw new Error("Not a data block.");

    if (view.getUint16(4, true) !== Version)
        throw new Error("This build cannot read that data block.");

    const kind = view.getUint16(6, true);
    const count = view.getUint32(8, true);
    const textColumns = view.getUint32(12, true);
    const numberColumns = view.getUint32(16, true);
    const metaCount = view.getUint32(20, true);
    const poolBytes = view.getUint32(24, true);

    let at = HeaderSize;

    const read = () => {
        const length = view.getUint16(at, true);
        at += 2;
        const value = Utf8.decode(new Uint8Array(buffer, at, length));
        at += length;
        return value;
    };

    const names: string[] = [];
    for (let index = 0; index < textColumns + numberColumns; index++) names.push(read());

    const meta: Record<string, string> = {};
    for (let index = 0; index < metaCount; index++) {
        const key = read();
        meta[key] = read();
    }

    const pool = new Uint8Array(buffer, buffer.byteLength - poolBytes, poolBytes);
    const string = (offset: number, length: number) =>
        Utf8.decode(pool.subarray(offset, offset + length));

    const rows = new Array<Record<string, string | number>>(count);

    for (let row = 0; row < count; row++) {
        const values: Record<string, string | number> = {};

        for (let column = 0; column < textColumns; column++) {
            values[names[column]] = string(view.getUint32(at, true), view.getUint16(at + 4, true));
            at += 6;
        }

        for (let column = 0; column < numberColumns; column++) {
            values[names[textColumns + column]] = view.getFloat64(at, true);
            at += 8;
        }

        rows[row] = values;
    }

    return {kind, meta, rows};
}
