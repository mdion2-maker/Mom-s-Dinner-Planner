#!/usr/bin/env python3
"""Builds MealPlanner.mobile.html by embedding data\\recipes.json into
app\\template_mobile.html, the same way Rebuild.ps1 does for the desktop app.

Run it after build\\Rebuild.ps1 (or anytime data\\recipes.json already exists
and only app\\template_mobile.html changed):

    python build\\build_mobile.py
"""
import sys
from pathlib import Path

MARKER = "/*__RECIPE_DATA__*/null"


def main():
    root = Path(__file__).resolve().parent.parent
    data_path = root / "data" / "recipes.json"
    template_path = root / "app" / "template_mobile.html"
    out_path = root / "MealPlanner.mobile.html"

    if not data_path.exists():
        sys.exit(f"Missing {data_path} - run build\\Rebuild.ps1 first.")
    if not template_path.exists():
        sys.exit(f"Missing {template_path}")

    json_text = data_path.read_text(encoding="utf-8")
    html = template_path.read_text(encoding="utf-8")

    if MARKER not in html:
        sys.exit(f"Marker {MARKER} not found in {template_path}")

    html = html.replace(MARKER, json_text)
    out_path.write_text(html, encoding="utf-8")
    print(f"Wrote {out_path} ({out_path.stat().st_size / 1_000_000:.1f} MB) "
          "- double-click it to use the mobile planner.")


if __name__ == "__main__":
    main()
