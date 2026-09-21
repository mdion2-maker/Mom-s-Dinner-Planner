#!/usr/bin/env python3
"""Builds the mobile planner from data/recipes.json and app/template_mobile.html.

Writes two things:

  MealPlanner.mobile.html   one self-contained file (double-click to try it)
  docs/                     the same app plus what an iPhone needs to install it
                            (icon, manifest, offline support), ready to host

Run it after build/Rebuild.ps1 (or anytime data/recipes.json already exists and
only a template changed):

    python build/build_mobile.py

Hosting the docs/ folder (GitHub Pages: Settings > Pages > Deploy from a
branch > main > /docs) gives a web address that opens on any iPhone; see the
README for the Add-to-Home-Screen steps.
"""
import shutil
import sys
import time
from pathlib import Path

MARKER = "/*__RECIPE_DATA__*/null"


def main():
    root = Path(__file__).resolve().parent.parent
    data_path = root / "data" / "recipes.json"
    template_path = root / "app" / "template_mobile.html"
    pwa = root / "app" / "pwa"
    out_path = root / "MealPlanner.mobile.html"
    docs = root / "docs"

    if not data_path.exists():
        sys.exit(f"Missing {data_path} - run build/Rebuild.ps1 first.")
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

    if not pwa.exists():
        print(f"No {pwa} folder, so docs/ was not built (run build/make_icon.py).")
        return

    docs.mkdir(exist_ok=True)
    (docs / "index.html").write_text(html, encoding="utf-8")
    for f in pwa.iterdir():
        if f.name == "sw.js":
            # A new version string makes phones drop the old cached copy.
            text = f.read_text(encoding="utf-8").replace("__VERSION__", time.strftime("%Y%m%d%H%M%S"))
            (docs / f.name).write_text(text, encoding="utf-8")
        else:
            shutil.copy2(f, docs / f.name)
    (docs / ".nojekyll").write_text("", encoding="utf-8")  # serve files exactly as they are
    print(f"Wrote {docs} - host this folder to get an iPhone-installable web address.")


if __name__ == "__main__":
    main()
