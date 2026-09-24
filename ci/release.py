#!/usr/bin/env python3
"""Steps of the Release pipeline (.buddy/release.fixed.yml).

  release.py check          validate the input and print the new version, change nothing
  release.py update         add the new version to CHANGELOG.md and gradle.properties
  release.py check-artifact the downloaded artifact is the release build, get its version

check / update read (environment):
  INCREMENT     Major | Minor | Patch, applied to the newest version in CHANGELOG.md
  NEW_FEATURES  one entry per line (a leading "- " is optional)
  IMPROVEMENTS  one entry per line
  BUGS          one entry per line
At least one of NEW_FEATURES, IMPROVEMENTS and BUGS must have an entry.

check-artifact reads RELEASE_VERSION (MAJOR.MINOR.PATCH) and expects the zip and nupkg of one build
MAJOR.MINOR.PATCH.<build run id> in release/.

update and check-artifact append their results (RELEASE_VERSION, RELEASE_NOTES, ARTIFACT_VERSION)
to the file in $BUDDY_VAR, which passes them to the next actions, one KEY=VALUE per line.
"""
import html
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CHANGELOG = ROOT / "CHANGELOG.md"
GRADLE_PROPERTIES = ROOT / "gradle.properties"
# Where the Release pipeline downloads the artifact version
RELEASE_DIR = ROOT / "release"
GROUPS = [("New Features", "NEW_FEATURES"), ("Improvements", "IMPROVEMENTS"), ("Bugs", "BUGS")]


def fail(message):
    print(f"error: {message}", file=sys.stderr)
    sys.exit(1)


def entries(text):
    """Lines of a multi-line input as changelog entries, without list markers and blank lines."""
    lines = (re.sub(r"^[-*]\s*", "", line.strip()) for line in (text or "").splitlines())
    return [line for line in lines if line]


def changes():
    groups = [(title, entries(os.environ.get(key))) for title, key in GROUPS]
    groups = [(title, items) for title, items in groups if items]
    if not groups:
        fail("fill in at least one of " + ", ".join(key for _, key in GROUPS))
    return groups


def next_version(changelog):
    newest = re.search(r"^## (\d+)\.(\d+)\.(\d+)\s*$", changelog, re.MULTILINE)
    if not newest:
        fail(f"no '## MAJOR.MINOR.PATCH' section in {CHANGELOG.name}")
    major, minor, patch = map(int, newest.groups())
    increment = os.environ.get("INCREMENT", "")
    bumped = {
        "Major": (major + 1, 0, 0),
        "Minor": (major, minor + 1, 0),
        "Patch": (major, minor, patch + 1),
    }.get(increment)
    if not bumped:
        fail(f"INCREMENT must be Major, Minor or Patch, got '{increment}'")
    current = f"{major}.{minor}.{patch}"
    version = ".".join(map(str, bumped))
    print(f"{current} -> {version}")
    return version, newest.start()


def notes_html(groups):
    """Release notes as one line of HTML (a $BUDDY_VAR value can't span lines); `code` becomes <code>."""
    def item(text):
        return re.sub(r"`([^`]+)`", r"<code>\1</code>", html.escape(text, quote=False))

    return "".join(
        f"<h3>{html.escape(title)}</h3><ul>" + "".join(f"<li>{item(i)}</li>" for i in items) + "</ul>"
        for title, items in groups
    )


def export(**values):
    buddy_var = os.environ.get("BUDDY_VAR")
    if buddy_var:
        with open(buddy_var, "a", encoding="utf-8") as f:
            for key, value in values.items():
                f.write(f"{key}={value}\n")


def check():
    groups = changes()
    next_version(CHANGELOG.read_text(encoding="utf-8"))
    for title, items in groups:
        print(f"{title}: {len(items)}")


def update():
    groups = changes()
    changelog = CHANGELOG.read_text(encoding="utf-8")
    version, newest_at = next_version(changelog)

    section = [f"## {version}"]
    for title, items in groups:
        section += [f"### {title}"] + [f"- {i}" for i in items]
    # The new section goes right before the newest one
    CHANGELOG.write_text(changelog[:newest_at] + "\n".join(section) + "\n\n" + changelog[newest_at:], encoding="utf-8")

    properties = GRADLE_PROPERTIES.read_text(encoding="utf-8")
    properties, found = re.subn(r"^PluginVersion=.*$", f"PluginVersion={version}", properties, flags=re.MULTILINE)
    if not found:
        fail(f"no PluginVersion in {GRADLE_PROPERTIES.name}")
    GRADLE_PROPERTIES.write_text(properties, encoding="utf-8")

    print("\n".join(section))
    export(RELEASE_VERSION=version, RELEASE_NOTES=notes_html(groups))


def check_artifact():
    version = os.environ.get("RELEASE_VERSION", "")
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        fail(f"RELEASE_VERSION must be MAJOR.MINOR.PATCH, got '{version}'")
    build = re.escape(version) + r"\.\d+"
    patterns = [rf"ReSharperPlugin\.RiderTestsSupportPlus-({build})\.zip", rf"ReSharperPlugin\.RiderTestsSupportPlus\.({build})\.nupkg"]
    names = sorted(p.name for p in RELEASE_DIR.glob("*")) if RELEASE_DIR.is_dir() else []
    print(f"{RELEASE_DIR.name}/: {', '.join(names) or 'nothing'}")

    versions = set()
    for pattern in patterns:
        matches = [m.group(1) for m in map(re.compile(pattern).fullmatch, names) if m]
        if len(matches) != 1:
            fail(f"expected one file matching {pattern}, found {len(matches)}")
        versions.update(matches)
    if len(versions) != 1:
        fail(f"the zip and the nupkg come from different builds: {', '.join(sorted(versions))}")
    found = versions.pop()
    print(f"Artifact version {found}")
    export(ARTIFACT_VERSION=found)


if __name__ == "__main__":
    steps = {"check": check, "update": update, "check-artifact": check_artifact}
    if len(sys.argv) != 2 or sys.argv[1] not in steps:
        fail("usage: release.py " + " | ".join(steps))
    steps[sys.argv[1]]()
