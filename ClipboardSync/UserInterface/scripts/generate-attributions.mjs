// Collects the open-source projects this app ships and writes them to `src/data/attributions.ts`,
// which the Open Source Attribution page renders.
//
// npm packages carry their licence as a file, so it is read straight out of `node_modules`. NuGet
// packages usually only declare an SPDX expression, so the canonical text for that expression is
// fetched from spdx.org once and cached under `scripts/licenses/` (committed, so later runs and
// other machines work offline).
//
// Run with `npm run attributions`.

import {existsSync} from "node:fs";
import {mkdir, readFile, readdir, writeFile} from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import {fileURLToPath} from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const uiRoot = path.resolve(here, "..");
const csprojPath = path.resolve(uiRoot, "../ClipboardSync.csproj");
const licenseCache = path.join(here, "licenses");
const outputPath = path.join(uiRoot, "src/data/attributions.ts");
const nugetRoot = path.join(os.homedir(), ".nuget", "packages");

/** The first licence-looking file in a directory, or null. */
async function findLicenseFile(directory) {
    const names = await readdir(directory).catch(() => []);
    const match = names.find((name) => /^(licen[cs]e|copying|ofl)(\.|$)/i.test(name));
    return match ? path.join(directory, match) : null;
}

/** Downloads a licence text once and caches it in the repository. */
async function remoteText(url, cacheName) {
    const cached = path.join(licenseCache, `${cacheName}.txt`);
    if (existsSync(cached)) return (await readFile(cached, "utf8")).trim();

    const response = await fetch(url);
    if (!response.ok) throw new Error(`Cannot download ${url}: HTTP ${response.status}`);

    const text = (await response.text()).trim();
    await mkdir(licenseCache, {recursive: true});
    await writeFile(cached, `${text}\n`, "utf8");
    console.log(`cached scripts/licenses/${cacheName}.txt`);
    return text;
}

/** Canonical text for an SPDX id, fetched once from spdx.org and cached in the repository. */
async function spdxText(id) {
    const cached = path.join(licenseCache, `${id}.txt`);
    if (existsSync(cached)) return (await readFile(cached, "utf8")).trim();

    const response = await fetch(`https://spdx.org/licenses/${encodeURIComponent(id)}.json`);
    if (!response.ok) throw new Error(`Cannot resolve ${id}: HTTP ${response.status}`);

    const {licenseText} = await response.json();
    if (!licenseText) throw new Error(`spdx.org has no text for ${id}`);

    await mkdir(licenseCache, {recursive: true});
    await writeFile(cached, `${licenseText.trim()}\n`, "utf8");
    console.log(`cached scripts/licenses/${id}.txt`);
    return licenseText.trim();
}

/** Guesses the SPDX id of a licence text, for packages that only link to one. */
const KNOWN_LICENSES = [
    [/MIT License/i, "MIT"],
    [/Apache License\s+Version 2\.0/i, "Apache-2.0"],
    [/SIL OPEN FONT LICENSE Version 1\.1/i, "OFL-1.1"],
    [/BSD 3-Clause/i, "BSD-3-Clause"],
    [/ISC License/i, "ISC"],
];

const detectLicense = (text) => KNOWN_LICENSES.find(([pattern]) => pattern.test(text))?.[1] ?? "";

/** Every npm package the app builds or ships with. */
async function npmEntries() {
    const manifest = JSON.parse(await readFile(path.join(uiRoot, "package.json"), "utf8"));
    const names = [...Object.keys(manifest.dependencies ?? {}), ...Object.keys(manifest.devDependencies ?? {})];
    const entries = [];

    for (const name of names.sort()) {
        const directory = path.join(uiRoot, "node_modules", name);
        const file = await findLicenseFile(directory);
        if (!file) {
            console.warn(`no licence file for ${name}, skipped`);
            continue;
        }

        const own = JSON.parse(await readFile(path.join(directory, "package.json"), "utf8"));
        entries.push({
            name,
            version: own.version ?? "",
            license: typeof own.license === "string" ? own.license : "",
            text: (await readFile(file, "utf8")).trim(),
        });
    }

    return entries;
}

/**
 * Packages whose nuspec declares no licence at all. Their text is fetched from the project instead;
 * the list stays short and explicit because it should only hold packages that give us no choice.
 */
const LICENSE_OVERRIDES = {
    "securifybv.ShellLink": "https://raw.githubusercontent.com/securifybv/ShellLink/master/LICENSE.txt",
};

/** Every NuGet package referenced by the desktop project. */
async function nugetEntries() {
    const project = await readFile(csprojPath, "utf8");
    const entries = [];

    for (const match of project.matchAll(/<PackageReference\s+Include="([^"]+)"\s+Version="([^"]+)"/g)) {
        const [, id, version] = match;
        const directory = path.join(nugetRoot, id.toLowerCase(), version);
        const nuspec = (await readdir(directory).catch(() => [])).find((name) => name.endsWith(".nuspec"));
        if (!nuspec) {
            console.warn(`no cached package for ${id} ${version}, skipped`);
            continue;
        }

        const spec = await readFile(path.join(directory, nuspec), "utf8");
        const authors = spec.match(/<authors>([^<]*)<\/authors>/)?.[1]?.trim() ?? "";
        const projectUrl = spec.match(/<projectUrl>([^<]*)<\/projectUrl>/)?.[1]?.trim() ?? "";
        const expression = spec.match(/<license type="expression">([^<]*)<\/license>/)?.[1]?.trim();
        const fileName = spec.match(/<license type="file">([^<]*)<\/license>/)?.[1]?.trim();

        let license = expression ?? "";
        let text;

        if (LICENSE_OVERRIDES[id]) {
            text = await remoteText(LICENSE_OVERRIDES[id], id);
            license = detectLicense(text);
        } else if (fileName) {
            const file = path.join(directory, fileName.replace(/\\/g, path.sep));
            text = existsSync(file) ? (await readFile(file, "utf8")).trim() : "";
        } else if (expression) {
            text = await spdxText(expression);
        } else {
            // Older packages only link their licence, so download it and name it from the text.
            const url = spec.match(/<licenseUrl>([^<]*)<\/licenseUrl>/)?.[1]?.trim();
            text = url ? await remoteText(url, id) : "";
            license = detectLicense(text);
        }

        const header = [
            license && `Licensed under ${license}.`,
            authors && `Authors: ${authors}`,
            projectUrl && `Project: ${projectUrl}`,
        ]
            .filter(Boolean)
            .join("\n");

        entries.push({name: id, version, license, text: `${header}\n\n${text}`.trim()});
    }

    return entries;
}

/** Emits a template literal that survives every character a licence text can contain. */
const literal = (value) => `\`${value.replace(/\\/g, "\\\\").replace(/`/g, "\\`").replace(/\$\{/g, "\\${")}\``;

const entries = [...(await npmEntries()), ...(await nugetEntries())].sort((a, b) =>
    a.name.localeCompare(b.name, "en", {sensitivity: "base"})
);

const body = entries
    .map(
        (entry) =>
            `    {\n        name: ${JSON.stringify(entry.name)},\n        version: ${JSON.stringify(entry.version)},\n` +
            `        license: ${JSON.stringify(entry.license)},\n        text: ${literal(entry.text)},\n    },`
    )
    .join("\n");

await writeFile(
    outputPath,
    `/**
 * Generated by \`npm run attributions\` — do not edit by hand.
 *
 * The open-source projects this app builds on, with the full licence text of each. npm packages are
 * read from their \`LICENSE\` file; NuGet packages declare an SPDX expression, whose canonical text
 * lives in \`scripts/licenses/\`.
 */
export interface Attribution {
    name: string;
    version: string;
    /** SPDX id, or a description when the package does not declare one. */
    license: string;
    /** The complete declaration shown when the entry is expanded. */
    text: string;
}

export const ATTRIBUTIONS: Attribution[] = [
${body}
];
`,
    "utf8"
);

console.log(`Wrote ${entries.length} attributions to ${path.relative(uiRoot, outputPath)}`);
