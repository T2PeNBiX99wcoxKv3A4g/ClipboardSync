#!/usr/bin/env bash
#
# Builds the Fedora RPM from a published linux-x64 build.
#
# Run it on Fedora, or in the container the release workflow uses:
#   docker run --rm -v "$PWD:/src" -w /src fedora:latest \
#     bash -c "dnf -y install rpm-build tar gzip && installer/linux/build-rpm.sh"
#
# The version, publish directory and output directory come from the environment, so the same script
# works locally and in CI.
set -euo pipefail

version="${VERSION:?VERSION must be set, e.g. 0.1.0}"
publish_dir="${PUBLISH_DIR:-publish/linux-x64}"
output_dir="${OUTPUT_DIR:-release}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"

# Paths may be absolute (a local run) or relative to the repository (what CI passes).
[[ "$publish_dir" = /* ]] || publish_dir="$repo/$publish_dir"
[[ "$output_dir" = /* ]] || output_dir="$repo/$output_dir"

if [[ ! -f "$publish_dir/ClipboardSync" ]]; then
    echo "No ClipboardSync in $publish_dir; publish for linux-x64 first." >&2
    exit 1
fi

stage="$(mktemp -d)"
topdir="$(mktemp -d)"
trap 'rm -rf "$stage" "$topdir"' EXIT

# rpmbuild wants the payload as a source archive named after the package and version.
mkdir -p "$stage/clipboardsync-$version"
install -m 0755 "$publish_dir/ClipboardSync" "$stage/clipboardsync-$version/ClipboardSync"
install -m 0644 "$here/clipboardsync.desktop" "$here/clipboardsync.png" "$stage/clipboardsync-$version/"
tar -C "$stage" -czf "$stage/clipboardsync-$version.tar.gz" "clipboardsync-$version"

mkdir -p "$topdir"/{BUILD,BUILDROOT,RPMS,SOURCES,SPECS,SRPMS}
cp "$stage/clipboardsync-$version.tar.gz" "$topdir/SOURCES/"
sed "s/@VERSION@/$version/g" "$here/clipboardsync.spec" > "$topdir/SPECS/clipboardsync.spec"

rpmbuild --define "_topdir $topdir" -bb "$topdir/SPECS/clipboardsync.spec"

mkdir -p "$output_dir"
find "$topdir/RPMS" -name '*.rpm' -exec cp {} "$output_dir/" \;
ls -l "$output_dir"/*.rpm
