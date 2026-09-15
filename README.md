# Mom's Dinner Planner

A dinner picker built from the 62,126 recipes in `archive.zip`, narrowed down to
**3,818 dinners that take 45 minutes or less**, split into summer and winter, and
weighted toward foods that help with osteoporosis.

## Using it

Double-click **`MealPlanner.html`**. That one file is the whole app — no install,
no internet needed (it only reaches out for fonts, and falls back gracefully
without them). It also lives online at
<https://claude.ai/artifact/MdwLE3soWX5wqBhysZyiMN>.

How it works, in order:

1. **Pick a season.** Summer and winter are kept separate. Summer shows summer
   and year-round dishes; winter shows winter and year-round ones. Tick *Only
   dishes made for this season* to drop the year-round ones.
2. **Choose how many dinners** you want (1 to 14).
3. **Set any filters you like** — hot or cold, kind of dish, how long it can
   take, main food, bone-strength priority, few-steps-only, one-pan-only, and
   things to skip (meat-free, no dairy, no wheat, no nuts, no pork, no shellfish).
4. **Press "Pick my dinners."**

Each meal card shows the total time, whether it's hot or cold, the kind of dish,
a bone-strength meter with the reason for it, and buttons to see the recipe,
swap that one meal for another, or save it with a heart. **Shopping list**
gathers every ingredient from the plan, grouped by supermarket aisle, with a
copy and a print button. **Browse all** lists everything matching the filters,
and **Saved** keeps the hearts.

Filters, saved meals, and the current plan are remembered in the browser, so the
app opens where it was left. Any filter that is switched on is named in the
green strip above the "Pick my dinners" button, with a **Clear** button.

## Never showing a food

Two ways, depending on whether you want it gone forever or just for now.

**In the app** (takes effect instantly): type the food into *Foods to never
show* and press **Add**. Dashed chips are the permanent ones below; anything you
add sits beside them and can be removed by clicking it.

**Permanently, so it never even gets loaded:** open `build\exclusions.txt`, add
the food on its own line, save, then run `build\Rebuild.ps1` (right-click →
*Run with PowerShell*). It currently removes anything mentioning:

> cottage cheese, liver (and liverwurst, braunschweiger, foie gras, chopped
> liver), eggplant (and aubergine), curry (and curried, curry powder/paste/sauce,
> vindaloo, korma, tikka, masala, garam masala, madras)

Matching is whole-word and handles plurals, so `liver` catches "chicken livers"
but not "slivered almonds". The blurb on each recipe is checked too, so an
unwanted food is not even mentioned in passing.

## What got thrown out, and why

Starting from 62,126 recipes, each one had to survive every rule below. The
count in brackets is how many were dropped by that rule.

| Rule | Dropped |
| --- | --- |
| Not a dinner — desserts, drinks, baked goods, breakfast sweets | 16,842 |
| Takes longer than 45 minutes, start to finish | 8,882 |
| Dessert or baking category | 5,019 |
| Needs hours of marinating, chilling, rising, or a slow cooker | 4,979 |
| No real main course in it | 3,421 |
| Fewer than four ingredients | 3,025 |
| A condiment or a side, not a meal | 2,534 |
| Sweet baking in disguise (no obvious dessert word in the name) | 2,042 |
| Mentions one of the foods to never show | 1,726 |
| Just a staple or a nibble — tortillas, bagel chips, plain rice | 1,206 |
| An ingredient that is hard to find in North America | 1,235 |
| A single step longer than 75 minutes | 1,152 |
| Never says how long anything cooks | 487 |
| Butchery, canning, or smoking project | 160 |
| A side salad rather than a dinner salad | 125 |
| Offal or oddities | 2 |

That leaves 9,341 recipes; near-duplicate titles are then merged, keeping the
better version of each, for a final **3,818**.

**Cooking times are calculated, not copied.** The source file's own time
estimates were unreliable (it put an air-fryer potato recipe at 97 minutes), so
the time comes from reading the instructions: every stated duration is added up
for the cooking time, and prep is estimated from the number of real ingredients
(pantry staples don't count) plus the amount of knife work. The estimate leans
high rather than low, so 45 minutes means 45 minutes.

**Hard to find** means a normal North American supermarket won't reliably carry
it: galangal, kaffir lime, curry leaves, nopales, dashi and bonito, guanciale,
duck and game meats, sea urchin, paneer, preserved lemon, and so on. Things that
have become ordinary — gruyère, harissa, halloumi, gochujang, mascarpone — are
allowed.

## The bone-strength score

Every recipe gets a 0–100 score for how much it helps bone density, shown as a
five-bar meter with the foods responsible listed beside it. Points come from
calcium, vitamin D, vitamin K, magnesium and protein:

- **Highest:** canned salmon with bones, sardines, yogurt and kefir, tofu,
  collard and turnip greens, milk, ricotta, kale, bok choy, salmon, mackerel
- **High:** parmesan, cheese, sesame and tahini, almonds, white beans, broccoli,
  trout, tuna, fortified soy milk, prunes, eggs, swiss chard, white fish
- **Moderate:** chickpeas, lentils, spinach, brussels sprouts, quinoa,
  mushrooms, walnuts and pumpkin seeds, sweet potato, cabbage, okra, oranges

Points are taken away for cola, alcohol, deep-frying, and salty processed meats.
*Favour* (the default) makes high scorers much more likely to be picked without
ruling anything out; *Best only* shows nothing under 45.

The scoring lists live in `build\Builder.cs` under `BoneFoods` if you ever want
to reweight them.

## Rebuilding

```
powershell -ExecutionPolicy Bypass -File build\Rebuild.ps1
```

It reads `archive.zip` and `build\exclusions.txt`, then writes
`data\recipes.json` and rebuilds `MealPlanner.html` with that data embedded.
Takes about a minute. Add `-Explain` to see samples of what each rule dropped,
which is the quickest way to check a new exclusion did what you wanted.

| File | What it is |
| --- | --- |
| `MealPlanner.html` | The app. Data is baked in; this is the only file needed to use it. |
| `app\template.html` | The app without the data — edit this, not `MealPlanner.html`. |
| `build\exclusions.txt` | Foods to remove. Edit freely. |
| `build\Builder.cs` | Every rule: time model, dish types, seasons, bone scoring, hard-to-find list. |
| `build\Csv.cs` | Streams the 300 MB CSV out of the zip. |
| `build\Rebuild.ps1` | Runs the whole build. |
| `data\recipes.json` | The 3,818 finished recipes. |

Editing `MealPlanner.html` directly works but gets overwritten on the next
rebuild — change `app\template.html` instead.
