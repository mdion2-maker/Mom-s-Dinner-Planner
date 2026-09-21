# Mom's Dinner Planner

A dinner picker for a mom who wants easy weeknight meals that are good for her
bones. It picks a week of dinners from **3,818 recipes that take 45 minutes or
less**, keeps summer and winter meals separate, and favors foods rich in calcium,
vitamin D, vitamin K, magnesium and protein. It also builds a shopping list
grouped by supermarket aisle.

It comes in three forms, all built from the same recipes:

| Form | Where | Good for |
| --- | --- | --- |
| Desktop app | `MealPlanner.html` (or `dist_exe\Mom's Dinner Planner.exe`) | A computer; printing plans and recipes |
| Phone app | `docs\` published as a web page | An iPhone, installed on the home screen |
| Single-file phone layout | `MealPlanner.mobile.html` | Trying the phone layout on a PC |

The recipes came from `archive.zip` (62,126 recipes). Everything that isn't a
quick, ordinary dinner was filtered out: desserts and baking, anything over 45
minutes, hard-to-find ingredients, and the foods on the never-show list
(cottage cheese, liver, eggplant, curry). Cooking times are recalculated from
the instructions rather than copied, and lean high so "45 minutes" really means
45. The rules live in `build\Builder.cs`.

## How to use it

**On a computer:** double-click `MealPlanner.html` (no install, no internet
needed) or run `dist_exe\Mom's Dinner Planner.exe`.

**On an iPhone:** open the app's web address in **Safari**, tap the *Share*
button, then **Add to Home Screen**. It then opens from the home screen like any
other app and keeps working with no signal. (The address is the GitHub Pages
link for this repository, usually
`https://<github-username>.github.io/<repository-name>/`; GitHub shows the exact
one under *Settings → Pages*.) The home-screen copy keeps its own saved hearts and
settings, separate from Safari's.

**Picking dinners:**

1. **Pick a season.** Summer shows summer and year-round dishes; winter shows
   winter and year-round ones. Tick *Only dishes made for this season* to drop the
   year-round ones.
2. **Choose how many dinners** (1 to 14).
3. **Set any filters:** hot or cold, kind of dish, how long it can take, main
   food, bone-strength priority, few steps, one pan, and things to skip (meat-free,
   no dairy, no wheat, no nuts, no pork, no shellfish).
4. **Press "Pick my dinners."**

Each meal shows its **time and how many it serves** side by side (so both are easy
to find on a printout), hot or cold, the kind of dish, and a bone-strength meter
with the reason for the score. From there:

- **Recipe** shows the ingredients and steps. Use **− / +** under *Servings* to
  change how many people it's for; the card, the ingredient amounts and the
  shopping list all follow. The recipes don't say how many they serve, so the
  number is estimated from portion-sized ingredients like "4 chicken breasts" and
  otherwise assumed to be 4 (the recipe says which).
- **Swap** replaces that one meal with another. **Save** (the heart) keeps it under
  *Saved*.
- **Shopping list** gathers every ingredient in the plan by aisle, with copy and
  print buttons, and a cart button beside each item that searches for it on
  Instacart.
- **Browse all** lists every dinner matching the filters.
- **Print** prints the plan.
- The desktop app can also **add** a recipe of your own (paste a web address or fill
  in a form) and **delete** ones you never want to see again.

Filters, saved meals and the current plan are remembered, so the app opens where
it was left.

**Never showing a food.** In the app, type the food into *Foods to never show* and
press **Add**; it takes effect at once. To remove it for good, add the food on its
own line in `build\exclusions.txt` and rebuild (below). Matching is whole-word and
handles plurals, so `liver` catches "chicken livers" but not "slivered almonds",
and a recipe's blurb is checked as well as its ingredients.

## Rebuilding

**The one rule: never edit the generated files.** `MealPlanner.html`,
`MealPlanner.mobile.html` and `docs\index.html` are overwritten on every build, so
any change made to them by hand is lost. (This has happened before: servings and
add-recipe work done directly in `MealPlanner.html` vanished at the next rebuild.)
Edit the sources in `app\` and `build\`, then run the matching build:

| I changed... | Then run | Result |
| --- | --- | --- |
| `app\template.html` (desktop look or behavior), `build\exclusions.txt`, or the rules in `build\Builder.cs` | `powershell -ExecutionPolicy Bypass -File build\Rebuild.ps1` | New `data\recipes.json` and `MealPlanner.html` (about a minute; add `-Explain` to see what each rule dropped) |
| `app\template_mobile.html`, or anything above | `python build\build_mobile.py` | New `MealPlanner.mobile.html` and `docs\` |
| Any of the above, and the `.exe` should match | `python build\build_exe.py` | New `dist_exe\Mom's Dinner Planner.exe` |
| The app icon (`build\make_icon.py`) | `python build\make_icon.py`, then the mobile and exe builds | New `app\icon.ico` and the phone icons in `app\pwa\` |

So after changing the recipes or the never-show list, run `Rebuild.ps1` first and
then `build_mobile.py`, because the phone version reads the recipes that
`Rebuild.ps1` just wrote. `Rebuild.ps1` needs `archive.zip` and Windows
PowerShell. The Python scripts need Python 3; the exe build also needs
`pyinstaller` and `pywebview`, and the icon script needs `Pillow`.

**Publishing a phone update:** commit and push. GitHub Pages serves the `docs`
folder from `main` and redeploys within a minute or two, and installed phones pick
up the new version the next time they're online. (First-time setup: repository →
*Settings → Pages → Deploy from a branch → `main` → `/docs`*.)

**Checking a change:** open `MealPlanner.html` or `MealPlanner.mobile.html` in a
browser after building. To try the phone version the way a phone gets it, run
`python -m http.server` inside `docs` and open `http://localhost:8000`.

## What's where

| Path | What it is |
| --- | --- |
| `app\template.html` | Desktop app without the recipes. Edit this. |
| `app\template_mobile.html` | Phone app without the recipes. Edit this. |
| `app\pwa\` | What makes the phone version installable: icons, manifest, offline support |
| `app\desktop_app.py`, `app\icon.ico` | The window and icon for the `.exe` |
| `build\` | The build scripts, the filtering rules (`Builder.cs`, `Csv.cs`) and `exclusions.txt` |
| `data\recipes.json` | The 3,818 finished recipes |
| `archive.zip` | The original 62,126 recipes |
| `MealPlanner.html`, `MealPlanner.mobile.html`, `docs\`, `dist_exe\` | Built outputs. Don't edit. |
