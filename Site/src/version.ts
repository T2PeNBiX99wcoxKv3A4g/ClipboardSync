import {useEffect, useState} from "react";
import {LATEST_RELEASE} from "./constants";

/** A plain `x.y.z` (or `x.y`); anything else is not a version worth showing. */
const PATTERN = /^\d+\.\d+/;

interface Release {
    tag_name?: string;
}

/**
 * The latest release tag, read from the GitHub API at runtime, so a new release shows up without
 * waiting for the site to be rebuilt. Null until it arrives, or when GitHub cannot be read.
 */
export function useVersion(): string | null {
    const [version, setVersion] = useState<string | null>(null);

    useEffect(() => {
        const controller = new AbortController();

        fetch(LATEST_RELEASE, {
            cache: "no-store",
            headers: {Accept: "application/vnd.github+json"},
            signal: controller.signal
        })
            .then(async (response) => (response.ok ? (await response.json() as Release).tag_name ?? "" : ""))
            .then((tag) => {
                const value = tag.replace(/^v/, "").trim();
                if (PATTERN.test(value)) setVersion(value);
            })
            .catch(() => {
                // Offline or rate limited: the badge simply stays hidden.
            });

        return () => controller.abort();
    }, []);

    return version;
}
