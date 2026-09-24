#!/usr/bin/env python3
"""Steps of the Release pipeline (.buddy/release.fixed.yml).

  release.py check          validate the input and print the new version, change nothing
  release.py update         add the new version to CHANGELOG.md and gradle.properties
  release.py find-artifact  find the artifact version built from the release commit

check / update read (environment):
  INCREMENT     Major | Minor | Patch, applied to the newest version in CHANGELOG.md
  NEW_FEATURES  one entry per line (a leading "- " is optional)
  IMPROVEMENTS  one entry per line
  BUGS          one entry per line
At least one of NEW_FEATURES, IMPROVEMENTS and BUGS must have an entry.

find-artifact reads (environment):
  RELEASE_VERSION         MAJOR.MINOR.PATCH being released
  BUDDY_API_TOKEN         Buddy personal access token with ARTIFACT_READ (the workspace needs the Developer API enabled)
  RELEASE_ARTIFACT_ID     hash ID of the artifact (the ID button on the artifact page), not its name
  BUDDY_WORKSPACE_DOMAIN  set by Buddy
  BUDDY_API_URL           optional, default https://api.eu.buddy.works
Master builds publish MAJOR.MINOR.PATCH.<build run id>. It fails while there is none yet,
so the action's retries wait for the Build pipeline.

update and find-artifact append their results (RELEASE_VERSION, RELEASE_NOTES, ARTIFACT_VERSION)
to the file in $BUDDY_VAR, which passes them to the next actions, one KEY=VALUE per line.
"""
import html
import json
import os
import re
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CHANGELOG = ROOT / "CHANGELOG.md"
GRADLE_PROPERTIES = ROOT / "gradle.properties"
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


def find_artifact():
    version = os.environ.get("RELEASE_VERSION", "")
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        fail(f"RELEASE_VERSION must be MAJOR.MINOR.PATCH, got '{version}'")
    try:
        token = os.environ["BUDDY_API_TOKEN"]
        artifact = os.environ["RELEASE_ARTIFACT_ID"]
        workspace = os.environ["BUDDY_WORKSPACE_DOMAIN"]
    except KeyError as e:
        fail(f"{e.args[0]} is not set")
    api = os.environ.get("BUDDY_API_URL", "https://api.eu.buddy.works")

    # Newest first; the release build is one of the latest versions
    url = f"{api}/workspaces/{workspace}/artifacts/{artifact}/versions?sort_by=created_date&sort_direction=DESC&per_page=50"
    request = urllib.request.Request(url, headers={"Authorization": f"Bearer {token}"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            versions = json.load(response).get("versions", [])
    except Exception as e:
        fail(f"listing artifact versions failed: {e}")

    release_build = re.compile(re.escape(version) + r"\.\d+")
    found = next((v["version"] for v in versions if release_build.fullmatch(v.get("version", ""))), None)
    if not found:
        fail(f"no artifact version {version}.<build> yet")
    print(f"Found {found}")
    export(ARTIFACT_VERSION=found)


if __name__ == "__main__":
    steps = {"check": check, "update": update, "find-artifact": find_artifact}
    if len(sys.argv) != 2 or sys.argv[1] not in steps:
        fail("usage: release.py " + " | ".join(steps))
    steps[sys.argv[1]]()
