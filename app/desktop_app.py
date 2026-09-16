"""Opens Mom's Dinner Planner in its own window instead of a browser tab.

Run directly during development:
    python app\\desktop_app.py

Packaged into a standalone .exe with PyInstaller (see build\\build_exe.py).
"""
import os
import sys
from pathlib import Path

# PyInstaller's --windowed build has no console, so sys.stdout/stderr are
# None; anything that tries to print (pywebview's own logging, its bottled-in
# web server) then crashes with "'NoneType' object has no attribute 'write'".
if sys.stdout is None:
    sys.stdout = open(os.devnull, "w")
if sys.stderr is None:
    sys.stderr = open(os.devnull, "w")

import webview

APP_TITLE = "Mom's Dinner Planner"


def html_path() -> Path:
    """Find MealPlanner.html next to this script, or bundled by PyInstaller."""
    base = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent.parent))
    return base / "MealPlanner.html"


def main():
    path = html_path()
    if not path.exists():
        raise SystemExit(f"Could not find {path} - run build\\Rebuild.ps1 first.")
    webview.create_window(
        APP_TITLE,
        path.as_uri(),
        width=1280,
        height=860,
        min_size=(760, 560),
    )
    webview.start()


if __name__ == "__main__":
    main()
