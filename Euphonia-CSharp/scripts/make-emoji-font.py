"""Builds src/Euphonia.Android/Assets/Fonts/EuphoniaEmoji.ttf: Noto Color Emoji (SIL OFL 1.1)
cut down to the emoji the UI actually uses, so Android can draw them (Avalonia can't reach the
system emoji font by name there) without shipping the full ~10 MB font.

    python scripts/make-emoji-font.py <NotoColorEmoji.ttf>

Get the source font e.g. from an Android device/emulator (adb pull /system/fonts/NotoColorEmoji.ttf)
or https://github.com/googlefonts/noto-emoji. Needs fonttools (pip/apt: fonttools). Re-run after
adding emoji to the UI.
"""
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCES = ["src/Euphonia", "src/Euphonia.Core"]
OUT = ROOT / "src/Euphonia.Android/Assets/Fonts/EuphoniaEmoji.ttf"


def used_codepoints() -> set[int]:
    cps: set[int] = set()
    for base in SOURCES:
        for path in (ROOT / base).rglob("*"):
            if path.suffix not in (".cs", ".axaml") or {"bin", "obj"} & set(path.parts):
                continue
            for ch in path.read_text(encoding="utf-8"):
                o = ord(ch)
                # emoji and symbol blocks (arrows, dingbats, pictographs) plus joiners/selectors
                if (o >= 0x2190 and not 0x2500 <= o <= 0x257F) or o in (0x200D, 0xFE0F, 0x20E3):
                    cps.add(o)
    return cps


def main() -> None:
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    cps = sorted(used_codepoints())
    print(f"{len(cps)} codepoints:", "".join(chr(c) for c in cps))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        [sys.executable, "-m", "fontTools.subset", sys.argv[1],
         "--unicodes=" + ",".join(f"U+{c:04X}" for c in cps),
         "--layout-features=*", "--output-file=" + str(OUT)],
        check=True)
    print(f"wrote {OUT} ({OUT.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main()
