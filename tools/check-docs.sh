#!/usr/bin/env bash
# Fails when the published documentation disagrees with the repository's version or with itself.
#
# Catches the failure that actually happened: a release bumped README/site/status but left the
# roadmap quoting the previous test count. It cannot prove a number is true — only that every
# document quotes the same one, and that the version every document quotes is the one being built.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

fail=0
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
if [ -z "$version" ]; then
    echo "check-docs: no <Version> in Directory.Build.props"
    exit 1
fi
echo "check-docs: version $version"

# A partial bump shipped a 0.1.6 build whose FileVersion still said 0.1.5. Check the numeric fields
# against the display version before anything is built or published.
numeric="${version%%-*}"
expected_fields="${numeric}.0"
assembly_version="$(sed -n 's:.*<AssemblyVersion>\(.*\)</AssemblyVersion>.*:\1:p' Directory.Build.props | head -1)"
file_version="$(sed -n 's:.*<FileVersion>\(.*\)</FileVersion>.*:\1:p' Directory.Build.props | head -1)"
for pair in "AssemblyVersion:$assembly_version" "FileVersion:$file_version"; do
    if [ "${pair#*:}" != "$expected_fields" ]; then
        echo "check-docs: ${pair%%:*} is '${pair#*:}', expected '$expected_fields'"
        fail=1
    fi
done

for file in README.md docs/index.html docs/status.md docs/roadmap.md; do
    if ! grep -qF -- "$version" "$file"; then
        echo "check-docs: $file does not mention $version"
        fail=1
    fi
done

# Every "<N> tests" claim must agree. Historical numbers belong in docs/journal.md, which this
# script deliberately does not read.
counts="$(grep -rhoE '[0-9]{3,4} tests' README.md docs/index.html docs/status.md docs/roadmap.md \
    | awk '{print $1}' | sort -u)"
distinct="$(printf '%s\n' "$counts" | grep -c . || true)"
if [ "$distinct" -ne 1 ]; then
    echo "check-docs: test counts disagree: $(printf '%s' "$counts" | tr '\n' ' ')"
    fail=1
else
    echo "check-docs: test count $counts"
fi

# The site must show the version it is advertising, and a release-notes file should exist for it.
if ! grep -qF ">$version<" docs/index.html; then
    echo "check-docs: docs/index.html has no '$version' pill"
    fail=1
fi
if [ ! -f "docs/release-notes/$version.md" ]; then
    echo "check-docs: warning: docs/release-notes/$version.md is missing (release notes will be generic)"
fi

if [ "$fail" -ne 0 ]; then
    echo "check-docs: FAILED"
    exit 1
fi
echo "check-docs: ok"
