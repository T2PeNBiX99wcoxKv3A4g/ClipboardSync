import {cp, rm} from "node:fs/promises";
import path from "node:path";
import {fileURLToPath} from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const source = path.resolve(__dirname, "../build");
const target = path.resolve(__dirname, "../../Resources/wwwroot");

await rm(target, {recursive: true, force: true});
await cp(source, target, {recursive: true});

console.log(`Copied ${source} -> ${target}`);
