#!/usr/bin/env python3
"""Collect Uno's published static root and record the version actually being built."""
import argparse
import json
import os
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("publish", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    candidates = sorted(args.publish.rglob("index.html"), key=lambda path: (len(path.parts), str(path)))
    if not candidates:
        parser.error(f"No index.html found below {args.publish}")
    source = candidates[0].parent.resolve()
    output = args.output.resolve()
    if source == output or source in output.parents or output in source.parents:
        parser.error("Publish and collected output directories must not contain each other.")
    # Tagged release builds may intentionally override the source version with VERSION.
    # Ordinary PR/main builds must use Directory.Build.props, never an unrelated old default.
    version = os.environ.get("VERSION", "").strip()
    if not version:
        props = Path(__file__).resolve().parents[1] / "Directory.Build.props"
        version = (ET.parse(props).getroot().findtext(".//Version") or "").strip()
    if not version:
        parser.error("No project version found in VERSION or Directory.Build.props.")
    output.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source, output, dirs_exist_ok=True)
    (output / ".nojekyll").touch()
    (output / "build-info.json").write_text(json.dumps({
        "application": "ArtSpace",
        "host": "Uno WebAssembly",
        "version": version,
        "commit": os.environ.get("GITHUB_SHA", "local")
    }, indent=2) + "\n", encoding="utf-8")
    print(f"Collected {source} into {output}; version {version}")


if __name__ == "__main__":
    main()
