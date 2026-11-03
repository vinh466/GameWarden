#!/usr/bin/env bash
# Cuts a development release tag: v<major.minor.patch>-dev.<n>. The tag push starts .github/workflows/release.yml.
# docs/10-build-test-release.md §4, docs/releases/README.md.
set -euo pipefail

cd "$(dirname "$0")/.."

die() { echo "release-dev: $*" >&2; exit 1; }

[[ "$(git branch --show-current)" == "main" ]] || die "switch to main first"
[[ -z "$(git status --porcelain)" ]] || die "the working tree is not clean"
git fetch origin main --tags --quiet
[[ "$(git rev-parse HEAD)" == "$(git rev-parse origin/main)" ]] || die "main is not at origin/main; pull first"

prefix="$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' Directory.Build.props | head -1)"
[[ -n "$prefix" ]] || die "no VersionPrefix in Directory.Build.props"

base="${1:-$prefix}"
base="${base#v}"
[[ "$base" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "expected a version like 0.2.0 or 0.3.0, got '$base'"

last="$(git tag --list "v${base}-dev.*" | sed "s/^v${base}-dev\.//" | sort -n | tail -1)"
next="$(( ${last:-0} + 1 ))"
tag="v${base}-dev.${next}"

notes="docs/releases/${base}-dev.${next}.md"
if [[ ! -f "$notes" ]]; then
    cp docs/releases/TEMPLATE.md "$notes"
    sed -i "s/{{version}}/${base}-dev.${next}/" "$notes"
    echo "release-dev: created $notes" >&2
    echo "release-dev: fill in Data impact / What changed, commit it, then run this script again" >&2
    exit 1
fi

git tag -a "$tag" -m "GameWarden ${base}-dev.${next}"
git push origin "$tag"
echo "release-dev: pushed $tag; watch: gh run watch"
