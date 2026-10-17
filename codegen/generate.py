#!/usr/bin/env python3
"""Generates the C# layer of the Didit SDK from openapi/ (DESIGN.md sections 3 to 5).

    python3 codegen/generate.py           regenerate src/Didit/Generated and tests/Didit.Tests/Generated
    python3 codegen/generate.py --check   fail when the committed generated code is stale

Reads only openapi/verification.json, openapi/account.json and openapi/overlay.json. Standard library only.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from didit_codegen import csharp, model, spec, tests  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
GENERATED_DIRS = ("src/Didit/Generated", "tests/Didit.Tests/Generated")


def render() -> dict[str, str]:
    loaded = spec.load(ROOT)
    built = model.build(loaded)
    files = csharp.emit(built)
    files.update(tests.emit(built))
    if loaded.stale_overlay:
        print("warning: overlay entries without a matching operation (remove them):", file=sys.stderr)
        for key in loaded.stale_overlay:
            print(f"  - {key}", file=sys.stderr)
    return files


def existing_files() -> dict[str, Path]:
    found: dict[str, Path] = {}
    for directory in GENERATED_DIRS:
        base = ROOT / directory
        if base.exists():
            for path in base.rglob("*.cs"):
                found[path.relative_to(ROOT).as_posix()] = path
    return found


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="exit 1 when the generated code differs from the committed code")
    args = parser.parse_args()

    try:
        files = render()
    except spec.SpecError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2

    current = existing_files()
    if args.check:
        stale = sorted(path for path, content in files.items() if path not in current or current[path].read_text("utf-8") != content)
        extra = sorted(set(current) - set(files))
        if stale or extra:
            print("Generated code is stale. Run `python3 codegen/generate.py` and commit the result.", file=sys.stderr)
            for path in stale[:50]:
                print(f"  changed: {path}", file=sys.stderr)
            for path in extra[:50]:
                print(f"  removed: {path}", file=sys.stderr)
            return 1
        print(f"Generated code is up to date ({len(files)} files).")
        return 0

    for path in set(current) - set(files):
        current[path].unlink()
    written = 0
    for path, content in sorted(files.items()):
        target = ROOT / path
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or target.read_text("utf-8") != content:
            target.write_text(content, "utf-8", newline="\n")
            written += 1
    print(f"Generated {len(files)} files ({written} changed).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
