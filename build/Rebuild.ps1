# Rebuilds the recipe database used by MealPlanner.html
#
#   Reads   : archive.zip  (recipes_extended.csv), archive2.zip (recipes.csv,
#             test_recipes.csv; optional)  +  build\exclusions.txt
#   Writes  : data\recipes.json   and   MealPlanner.html (data is embedded)
#
# Run it by right-clicking this file and choosing "Run with PowerShell",
# or from a terminal:   powershell -ExecutionPolicy Bypass -File build\Rebuild.ps1
#
# -AddToExisting leaves every recipe already in data\recipes.json untouched and only adds
# the ones from archive2.zip. The current recipes were built on 2026-09-15 with looser
# rules than Builder.cs has now, so a full rebuild would remove about half of them.

param(
    [int]$MaxRecipes = 5000,
    [switch]$Explain,     # print sample rejections + accepted samples for tuning
    [switch]$IgnoreSpicyLabel, # drop only real heat, not the archive's noisy "spicy" tag
    [switch]$AddToExisting     # keep data\recipes.json exactly as is; only add new recipes
                               # from archive2.zip that it doesn't already have
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$zip = Join-Path $root 'archive.zip'
$dataDir = Join-Path $root 'data'
$outJson = Join-Path $dataDir 'recipes.json'

if (-not (Test-Path $zip)) { throw "archive.zip not found at $zip" }
if (-not (Test-Path $dataDir)) { New-Item -ItemType Directory -Path $dataDir | Out-Null }

Write-Host "Compiling recipe filters..." -ForegroundColor Cyan
$sources = @((Join-Path $PSScriptRoot 'Csv.cs'), (Join-Path $PSScriptRoot 'Builder.cs'))
Add-Type -Path $sources -ReferencedAssemblies 'System.IO.Compression', 'System.IO.Compression.FileSystem'
[Dish.Builder]::UseSpicyLabel = -not $IgnoreSpicyLabel

# ---- foods to exclude -------------------------------------------------
[string[]]$exclude = @(Get-Content (Join-Path $PSScriptRoot 'exclusions.txt') |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith('#') } |
    ForEach-Object { $_.ToLowerInvariant() } |
    Select-Object -Unique)
Write-Host ("Excluding {0} foods: {1}" -f $exclude.Count, ($exclude -join ', ')) -ForegroundColor Yellow

$kept = New-Object 'System.Collections.Generic.List[Dish.Recipe]'
$rejectCounts = @{}
$rejectSamples = @{}
$rows = 0
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# Every source goes through the same rules. $stated is the source's own total time in
# minutes (0 if it gives none); the longer of that and our own estimate wins, so a
# recipe the source itself calls an hour never sneaks in as "25 minutes".
function Add-Candidate($title, $cat, $sub, $desc, $ings, $steps, $tastes, [int]$stated = 0, [int]$serves = 0) {
    $script:rows++
    if ($script:rows % 5000 -eq 0) {
        Write-Host ("  {0,6} read, {1,5} kept  ({2:N0}s)" -f $script:rows, $kept.Count, $sw.Elapsed.TotalSeconds)
    }
    $reason = ''
    $r = [Dish.Builder]::Build($title, $cat, $sub, $desc, $ings, $steps, $tastes, $exclude, [ref]$reason)
    if ($null -ne $r -and $stated -gt 45) { $r = $null; $reason = 'over-45-stated' }
    if ($null -eq $r) {
        $key = ($reason -split ':')[0]
        if ($rejectCounts.ContainsKey($key)) { $rejectCounts[$key]++ } else { $rejectCounts[$key] = 1 }
        if ($Explain -and -not $rejectSamples.ContainsKey($reason) -and $rejectSamples.Count -lt 400) {
            $rejectSamples[$reason] = $title
        }
        return
    }
    if ($stated -gt $r.TotalMin) { $r.PrepMin += $stated - $r.TotalMin; $r.TotalMin = $stated }
    if ($serves -ge 1 -and $serves -le 12) { $r.Servings = $serves }
    $kept.Add($r)
}

function Open-Csv($zipPath, $entry) {
    $a = $null
    $c = [Dish.CsvReader]::FromZip($zipPath, $entry, [ref]$a)
    $h = $c.ReadRow()
    $ix = @{}
    for ($i = 0; $i -lt $h.Length; $i++) { $ix[$h[$i]] = $i }
    return @{ Csv = $c; Arch = $a; Idx = $ix; Max = ($ix.Values | Measure-Object -Maximum).Maximum }
}

# ---- source 1: archive.zip (recipes_extended.csv) --------------------
if (-not $AddToExisting) {
Write-Host "Reading recipes from archive.zip (this takes a couple of minutes)..." -ForegroundColor Cyan
$src = Open-Csv $zip 'recipes_extended.csv'
$idx = $src.Idx
$cTitle = $idx['recipe_title']; $cCat = $idx['category']; $cDesc = $idx['description']
$cIng = $idx['ingredients']; $cDir = $idx['directions']
$cSub = $idx['subcategory']; $cTastes = $idx['tastes']
while ($true) {
    $row = $src.Csv.ReadRow()
    if ($null -eq $row) { break }
    if ($row.Length -le $src.Max) { continue }
    Add-Candidate $row[$cTitle] $row[$cCat] $row[$cSub] $row[$cDesc] `
        ([Dish.Builder]::ParseArray($row[$cIng])) ([Dish.Builder]::ParseArray($row[$cDir])) $row[$cTastes]
}
$src.Csv.Dispose(); $src.Arch.Dispose()
}

# ---- source 2: archive2.zip (recipes.csv + test_recipes.csv) ---------
# A smaller Allrecipes download that states servings and times. Optional.
$zip2 = Join-Path $root 'archive2.zip'
if (Test-Path $zip2) {
    Write-Host "Reading recipes from archive2.zip..." -ForegroundColor Cyan
    $src = Open-Csv $zip2 'recipes.csv'
    $ix = $src.Idx
    while ($true) {
        $row = $src.Csv.ReadRow()
        if ($null -eq $row) { break }
        if ($row.Length -le $src.Max) { continue }
        # cuisine_path looks like /Main Dishes/Chicken/ ; its first part is the section
        $path = $row[$ix['cuisine_path']].Trim('/')
        $sect = ($path -split '/')[0]
        $sv = 0; [void][int]::TryParse($row[$ix['servings']], [ref]$sv)
        Add-Candidate $row[$ix['recipe_name']] $sect $path '' `
            ([Dish.Builder]::SplitIngredientLine($row[$ix['ingredients']])) `
            ([Dish.Builder]::SplitLines($row[$ix['directions']])) $null `
            ([Dish.Builder]::StatedMinutes($row[$ix['total_time']])) $sv
    }
    $src.Csv.Dispose(); $src.Arch.Dispose()

    $src = Open-Csv $zip2 'test_recipes.csv'
    $ix = $src.Idx
    while ($true) {
        $row = $src.Csv.ReadRow()
        if ($null -eq $row) { break }
        if ($row.Length -le $src.Max) { continue }
        $sv = 0; [void][int]::TryParse($row[$ix['Servings']], [ref]$sv)
        Add-Candidate $row[$ix['Name']] '' '' '' `
            ([Dish.Builder]::ParsePyIngredients($row[$ix['Ingredients']])) `
            ([Dish.Builder]::ParsePyList($row[$ix['Directions']])) $null `
            ([Dish.Builder]::StatedMinutes($row[$ix['Total Time']])) $sv
    }
    $src.Csv.Dispose(); $src.Arch.Dispose()
}
Write-Host ("Read {0} recipes in {1:N0}s; {2} passed every rule." -f $rows, $sw.Elapsed.TotalSeconds, $kept.Count) -ForegroundColor Green

"" ; "--- why recipes were dropped ---"
$rejectCounts.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { "{0,7}  {1}" -f $_.Value, $_.Key }

if ($Explain) {
    "" ; "--- sample drops ---"
    $rejectSamples.GetEnumerator() | Sort-Object Name | Select-Object -First 200 | ForEach-Object { "{0,-42} {1}" -f $_.Name, $_.Value }
}

# ---- de-duplicate near-identical titles, keep the best ---------------
$byKey = @{}
foreach ($r in $kept) {
    $k = [Dish.Builder]::NormTitle($r.Title)
    if ($k.Length -lt 4) { $k = $r.Title.ToLowerInvariant() }
    if ($byKey.ContainsKey($k)) {
        if ($r.Quality -gt $byKey[$k].Quality) { $byKey[$k] = $r }
    }
    else { $byKey[$k] = $r }
}
$unique = @($byKey.Values)
Write-Host ("After removing near-duplicate titles: {0}" -f $unique.Count) -ForegroundColor Green

# ---- trim to MaxRecipes, keeping variety across season x dish type ---
$final = $unique
if ($unique.Count -gt $MaxRecipes) {
    # round-robin across season x dish-type buckets so nothing gets crowded out
    $buckets = New-Object 'System.Collections.Generic.List[object]'
    foreach ($g in ($unique | Group-Object { $_.Season + '|' + $_.Base })) {
        $arr = New-Object 'System.Collections.Generic.List[Dish.Recipe]'
        foreach ($r in ($g.Group | Sort-Object { -$_.Quality })) { $arr.Add($r) }
        $buckets.Add($arr)
    }
    $picked = New-Object 'System.Collections.Generic.List[Dish.Recipe]'
    $round = 0
    while ($picked.Count -lt $MaxRecipes) {
        $added = 0
        foreach ($b in $buckets) {
            if ($round -lt $b.Count -and $picked.Count -lt $MaxRecipes) { $picked.Add($b[$round]); $added++ }
        }
        if ($added -eq 0) { break }
        $round++
    }
    $final = $picked
}
Write-Host ("Final recipe count: {0}" -f $final.Count) -ForegroundColor Green

# ---- stats ------------------------------------------------------------
"" ; "--- dish types ---"
$final | Group-Object Base | Sort-Object Count -Descending | ForEach-Object { "{0,6}  {1}" -f $_.Count, $_.Name }
"" ; "--- season / temperature ---"
$final | Group-Object Season | Sort-Object Count -Descending | ForEach-Object { "{0,6}  {1}" -f $_.Count, $_.Name }
$final | Group-Object Temp | Sort-Object Count -Descending | ForEach-Object { "{0,6}  {1}" -f $_.Count, $_.Name }
"" ; "--- protein ---"
$final | Group-Object Protein | Sort-Object Count -Descending | ForEach-Object { "{0,6}  {1}" -f $_.Count, $_.Name }
"" ; "--- total time ---"
$final | Group-Object { [math]::Floor($_.TotalMin / 10) * 10 } | Sort-Object Name | ForEach-Object { "{0,6}  {1}-{2} min" -f $_.Count, $_.Name, ([int]$_.Name + 9) }
"" ; "--- difficulty (ranked easiest first) ---"
$final | Group-Object DiffLabel | Sort-Object { $_.Group[0].Diff } | ForEach-Object { "{0,6}  {1}  (level {2})" -f $_.Count, $_.Name, $_.Group[0].Diff }
"" ; "--- standing / popularity signal ---"
$final | Group-Object Top | Sort-Object Name | ForEach-Object { "{0,6}  top score {1}" -f $_.Count, $_.Name }
"" ; "--- most popular picks ---"
$final | Sort-Object { -$_.Top } | Select-Object -First 12 | ForEach-Object { "{0}  {1,-54} {2} min  {3}" -f $_.Top, $_.Title, $_.TotalMin, $_.DiffLabel }
"" ; "--- easiest picks ---"
$final | Sort-Object { $_.Diff }, { -$_.Top } | Select-Object -First 12 | ForEach-Object { "{0,-11} {1,-52} {2} min  bone {3}" -f $_.DiffLabel, $_.Title, $_.TotalMin, $_.Bone }
"" ; "--- bone-health score ---"
$final | Group-Object { [math]::Floor($_.Bone / 10) * 10 } | Sort-Object Name | ForEach-Object { "{0,6}  {1}s" -f $_.Count, $_.Name }
"" ; "--- highest bone-health picks ---"
$final | Sort-Object Bone -Descending | Select-Object -First 15 | ForEach-Object { "{0,3}  {1,-52} {2} min  [{3}]" -f $_.Bone, $_.Title, $_.TotalMin, ($_.BoneWhy -join ', ') }
"" ; "--- random accepted samples ---"
$final | Get-Random -Count ([math]::Min(25, $final.Count)) | ForEach-Object {
    "{0,-50} {1,-22} {2,-7} {3,-7} {4,2}+{5,2}={6,2}m  bone {7}" -f $_.Title, $_.Base, $_.Temp, $_.Season, $_.PrepMin, $_.CookMin, $_.TotalMin, $_.Bone
}

# ---- write recipes.json ----------------------------------------------
# Keep each recipe's number from the last build: the app remembers saved hearts, the
# current plan and serving choices by number, so renumbering would scramble them.
$oldIds = @{}
$nextId = 0
$prevObjs = @()
if (Test-Path $outJson) {
    $prev = [System.IO.File]::ReadAllText($outJson)
    foreach ($m in [regex]::Matches($prev, '\{"i":(\d+),"t":"((?:[^"\\]|\\.)*)"')) {
        $n = [int]$m.Groups[1].Value
        $oldIds[$m.Groups[2].Value] = $n
        if ($n -ge $nextId) { $nextId = $n + 1 }
    }
    if ($AddToExisting) {
        $body = $prev.Substring($prev.IndexOf('"recipes":[{') + 11)
        $body = $body.Substring(1, $body.Length - 4)          # drop the outer [{ ... }]}
        $prevObjs = @($body -split '\},\{"i":' | ForEach-Object -Begin { $f = $true } -Process {
            if ($f) { $f = $false; '{' + $_ + '}' } else { '{"i":' + $_ + '}' }
        })
    }
}
elseif ($AddToExisting) { throw "-AddToExisting needs an existing $outJson" }

# Each entry: difficulty, standing, order, json. Output is easiest first, most popular
# first within a level; "order" keeps ties where they were.
$entries = New-Object 'System.Collections.Generic.List[object]'
$seq = 0
if ($AddToExisting) {
    $haveKeys = @{}
    foreach ($t in $oldIds.Keys) { $haveKeys[[Dish.Builder]::NormTitle([regex]::Unescape($t))] = $true }
    foreach ($o in $prevObjs) {
        $entries.Add(@([int]([regex]::Match($o, '"df":(\d+)').Groups[1].Value),
                       [int]([regex]::Match($o, '"tp":(\d+)').Groups[1].Value), $seq++, $o))
    }
    $final = @($final | Where-Object { -not $haveKeys.ContainsKey([Dish.Builder]::NormTitle($_.Title)) })
    Write-Host ("Kept all {0} existing recipes; adding {1} new ones." -f $prevObjs.Count, $final.Count) -ForegroundColor Green
}
foreach ($r in ($final | Sort-Object { $_.Diff }, { -$_.Top }, { -$_.Quality })) {
    $tk = [Dish.Builder]::Esc($r.Title)
    if ($oldIds.ContainsKey($tk)) { $r.Id = $oldIds[$tk]; $oldIds.Remove($tk) } else { $r.Id = $nextId++ }
    $entries.Add(@($r.Diff, $r.Top, $seq++, [Dish.Builder]::ToJson($r)))
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.Append('{"built":"').Append((Get-Date).ToString('yyyy-MM-dd')).Append('","count":').Append($entries.Count)
[void]$sb.Append(',"excluded":').Append([Dish.Builder]::Arr($exclude))
[void]$sb.Append(',"rules":').Append([Dish.Builder]::Arr([string[]]@('anything spicy', 'over 45 minutes', 'hard-to-find ingredients')))
[void]$sb.Append(',"recipes":[')
$first = $true
foreach ($e in ($entries | Sort-Object { $_[0] }, { -$_[1] }, { $_[2] })) {
    if (-not $first) { [void]$sb.Append(',') }
    [void]$sb.Append($e[3])
    $first = $false
}
[void]$sb.Append(']}')
$json = $sb.ToString()
[System.IO.File]::WriteAllText($outJson, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("Wrote {0} ({1:N1} MB)" -f $outJson, ($json.Length / 1MB)) -ForegroundColor Green

# ---- embed the data into the app ------------------------------------
$template = Join-Path $root 'app\template.html'
$appOut = Join-Path $root 'MealPlanner.html'
if (Test-Path $template) {
    $html = [System.IO.File]::ReadAllText($template)
    $marker = '/*__RECIPE_DATA__*/null'
    if ($html.Contains($marker)) {
        $html = $html.Replace($marker, $json)
        [System.IO.File]::WriteAllText($appOut, $html, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host ("Wrote {0} ({1:N1} MB) - double-click it to use the planner." -f $appOut, ($html.Length / 1MB)) -ForegroundColor Green
    }
    else { Write-Warning "Marker $marker not found in app\template.html; MealPlanner.html not updated." }
}
else { Write-Warning "app\template.html missing; wrote data only." }
