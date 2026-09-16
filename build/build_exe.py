"""Packages app\\desktop_app.py into a standalone Mom's Dinner Planner.exe
that bundles MealPlanner.html, so nothing else needs to be installed to run it.

    python build\\build_exe.py

Writes the .exe to dist_exe\\Mom's Dinner Planner.exe. PyInstaller's own
scratch files are kept in a temp directory, well away from this build\\
folder (which holds the real project source - never point PyInstaller's
--workpath/--distpath/--specpath at a folder that already has files in it).
"""
import subprocess
import sys
import tempfile
from pathlib import Path

APP_NAME = "Mom's Dinner Planner"


def main():
    root = Path(__file__).resolve().parent.parent
    html = root / "MealPlanner.html"
    icon = root / "app" / "icon.ico"
    entry = root / "app" / "desktop_app.py"
    dist = root / "dist_exe"

    if not html.exists():
        sys.exit(f"Missing {html} - build it first (see README).")

    with tempfile.TemporaryDirectory(prefix="pyinstaller_") as scratch:
        cmd = [
            sys.executable, "-m", "PyInstaller",
            "--onefile", "--windowed",
            "--name", APP_NAME,
            "--add-data", f"{html}{';' if sys.platform.startswith('win') else ':'}.",
            "--distpath", str(dist),
            "--workpath", scratch,
            "--specpath", scratch,
        ]
        if icon.exists():
            cmd += ["--icon", str(icon)]
        cmd.append(str(entry))
        subprocess.run(cmd, cwd=root, check=True)

    exe = dist / f"{APP_NAME}.exe"
    print(f"\nBuilt {exe}")


if __name__ == "__main__":
    main()
