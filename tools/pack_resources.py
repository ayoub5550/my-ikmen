#!/usr/bin/env python3
"""Copy the screenpack files the runtime needs into unity/Assets/IK/Resources.

Unity's Resources folder is the only asset store that survives inside the APK as
readable bytes, and `ResourcesSource` (Scripts/Core/ResourceSources.cs) flattens any
MUGEN path to its bare file name with the dot replaced by an underscore, e.g.

    ikemen1/fonts/Menu2.def  ->  Resources/data/menu2_def.bytes
    stages/kfm.sff           ->  Resources/stages/kfm_sff.bytes

so every packed file must have a unique lower-case base name inside its group.
Run from the repo root:  python3 tools/pack_resources.py [--check]
"""

from __future__ import annotations

import argparse
import hashlib
import pathlib
import shutil
import sys

REPO = pathlib.Path(__file__).resolve().parents[1]
SRC = REPO / "assets" / "screenpack"
DST = REPO / "unity" / "Assets" / "IK" / "Resources"

# group -> list of source paths relative to assets/screenpack
GROUPS: dict[str, list[str]] = {
    "chars/kfm": [
        "chars/kfm/kfm.def",
        "chars/kfm/kfm.cns",
        "chars/kfm/kfm.cmd",
        "chars/kfm/kfm.air",
        "chars/kfm/kfm.sff",
        "chars/kfm/kfm.snd",
    ],
    "stages": [
        "stages/kfm.def",
        "stages/kfm.sff",
    ],
    "data": [
        "data/fight.def",
        "data/fight.sff",
        "data/fight.snd",
        "data/fightfx.air",
        "data/fightfx.sff",
        "data/glyphs.sff",
        "data/common.snd",
        "data/ikemen1/fonts/Action.def",
        "data/ikemen1/fonts/Action.sff",
        "data/ikemen1/fonts/ComboCounter.def",
        "data/ikemen1/fonts/ComboCounter.sff",
        "data/ikemen1/fonts/HitNum.def",
        "data/ikemen1/fonts/HitNum.sff",
        "data/ikemen1/fonts/Menu2.def",
        "data/ikemen1/fonts/Menu2.sff",
        "data/ikemen1/fonts/Menu2Small.def",
        "data/ikemen1/fonts/Menu2Small.sff",
        "data/ikemen1/fonts/Pixel.def",
        "data/ikemen1/fonts/Pixel.sff",
        "data/ikemen1/fonts/PowerbarNum.def",
        "data/ikemen1/fonts/PowerbarNum.sff",
        "data/ikemen1/fonts/Round.def",
        "data/ikemen1/fonts/Round.sff",
        "data/ikemen1/fonts/Timer.def",
        "data/ikemen1/fonts/Timer.sff",
    ],
}


def asset_name(path: str) -> str:
    """Mirror of ResourcesSource.AssetName."""
    return pathlib.PurePosixPath(path).name.replace(".", "_").lower()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="verify the packed copies match the sources, change nothing")
    args = ap.parse_args()

    total = 0
    stale: list[str] = []
    for group, files in GROUPS.items():
        out_dir = DST / group
        seen: dict[str, str] = {}
        if not args.check:
            out_dir.mkdir(parents=True, exist_ok=True)
        for rel in files:
            src = SRC / rel
            if not src.is_file():
                print(f"MISSING SOURCE {rel}", file=sys.stderr)
                return 2
            name = asset_name(rel)
            if name in seen:
                print(f"NAME CLASH in {group}: {rel} and {seen[name]} -> {name}", file=sys.stderr)
                return 2
            seen[name] = rel
            dst = out_dir / (name + ".bytes")
            src_digest = hashlib.sha256(src.read_bytes()).hexdigest()
            dst_digest = (hashlib.sha256(dst.read_bytes()).hexdigest()
                          if dst.is_file() else "")
            if src_digest != dst_digest:
                stale.append(f"{group}/{dst.name}")
                if not args.check:
                    shutil.copyfile(src, dst)
            total += src.stat().st_size
            print(f"{'=' if src_digest == dst_digest else '+'} {group}/{dst.name:28s} "
                  f"{src.stat().st_size / 1024:9.1f} KiB  <- {rel}")

    print(f"\n{sum(len(v) for v in GROUPS.values())} files, {total / 1048576:.1f} MiB total")
    if args.check and stale:
        print("OUT OF DATE: " + ", ".join(stale), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
