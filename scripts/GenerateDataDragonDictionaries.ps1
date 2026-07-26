param(
    [Parameter(Mandatory = $true)]
    [string] $SourceRoot,

    [Parameter(Mandatory = $true)]
    [string] $ProjectRoot
)

$ErrorActionPreference = 'Stop'

function Read-DragonJson([string] $Locale, [string] $FileName) {
    $path = Join-Path $SourceRoot "$Locale\$FileName"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing Data Dragon source: $path"
    }

    return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
}

function Add-Term(
    [System.Collections.Generic.List[object]] $Terms,
    [System.Collections.Generic.HashSet[string]] $Seen,
    [string] $Id,
    [string] $Category,
    [string] $Korean,
    [string] $Chinese,
    [string] $English
) {
    if ([string]::IsNullOrWhiteSpace($Korean) `
        -or [string]::IsNullOrWhiteSpace($Chinese) `
        -or [string]::IsNullOrWhiteSpace($English)) {
        return
    }

    $dedupeKey = "$Category`n$Korean`n$Chinese`n$English"
    if (-not $Seen.Add($dedupeKey)) {
        return
    }

    $Terms.Add([ordered]@{
        id = $Id
        category = $Category
        korean = $Korean
        chinese = $Chinese
        english = $English
    })
}

$koChampions = Read-DragonJson 'ko_KR' 'championFull.json'
$zhChampions = Read-DragonJson 'zh_CN' 'championFull.json'
$enChampions = Read-DragonJson 'en_US' 'championFull.json'
$version = [string] $koChampions.version

$existingHeroPath = Join-Path $ProjectRoot 'Assets\heroes.json'
$existingHeroes = @{}
if (Test-Path -LiteralPath $existingHeroPath -PathType Leaf) {
    $oldCatalog = Get-Content -LiteralPath $existingHeroPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($hero in $oldCatalog.heroes) {
        $existingHeroes[[string] $hero.id] = $hero
    }
}

$heroRows = [System.Collections.Generic.List[object]]::new()
foreach ($id in @($koChampions.data.Keys | Sort-Object)) {
    if (-not $zhChampions.data.ContainsKey($id) -or -not $enChampions.data.ContainsKey($id)) {
        continue
    }

    $ko = $koChampions.data[$id]
    $zh = $zhChampions.data[$id]
    $en = $enChampions.data[$id]
    $aliases = @()
    if ($existingHeroes.ContainsKey($id)) {
        $aliases = @($existingHeroes[$id].chineseAliases)
    }

    $heroRows.Add([ordered]@{
        id = [string] $id
        key = [string] $ko.key
        koreanName = [string] $ko.name
        koreanTitle = [string] $ko.title
        chineseOfficial = [string] $zh.name
        chinesePersonal = [string] $zh.title
        englishName = [string] $en.name
        chineseAliases = $aliases
    })
}

$heroCatalog = [ordered]@{
    version = $version
    heroes = $heroRows
}
$heroCatalog |
    ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $existingHeroPath -Encoding utf8NoBOM

$terms = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

foreach ($id in @($koChampions.data.Keys | Sort-Object)) {
    if (-not $zhChampions.data.ContainsKey($id) -or -not $enChampions.data.ContainsKey($id)) {
        continue
    }

    $ko = $koChampions.data[$id]
    $zh = $zhChampions.data[$id]
    $en = $enChampions.data[$id]
    Add-Term $terms $seen "$id`:P" 'championAbility' `
        ([string] $ko.passive.name) ([string] $zh.passive.name) ([string] $en.passive.name)

    $spellCount = [Math]::Min($ko.spells.Count, [Math]::Min($zh.spells.Count, $en.spells.Count))
    for ($index = 0; $index -lt $spellCount; $index++) {
        $slot = if ($index -lt 4) { @('Q', 'W', 'E', 'R')[$index] } else { "S$index" }
        Add-Term $terms $seen "$id`:$slot" 'championAbility' `
            ([string] $ko.spells[$index].name) `
            ([string] $zh.spells[$index].name) `
            ([string] $en.spells[$index].name)
    }
}

$koSummoners = Read-DragonJson 'ko_KR' 'summoner.json'
$zhSummoners = Read-DragonJson 'zh_CN' 'summoner.json'
$enSummoners = Read-DragonJson 'en_US' 'summoner.json'
foreach ($id in @($koSummoners.data.Keys | Sort-Object)) {
    if ($id -like '*Placeholder*' `
        -or -not $zhSummoners.data.ContainsKey($id) `
        -or -not $enSummoners.data.ContainsKey($id)) {
        continue
    }

    Add-Term $terms $seen $id 'summonerSpell' `
        ([string] $koSummoners.data[$id].name) `
        ([string] $zhSummoners.data[$id].name) `
        ([string] $enSummoners.data[$id].name)
}

$koItems = Read-DragonJson 'ko_KR' 'item.json'
$zhItems = Read-DragonJson 'zh_CN' 'item.json'
$enItems = Read-DragonJson 'en_US' 'item.json'
foreach ($id in @($koItems.data.Keys | Sort-Object)) {
    if (-not $zhItems.data.ContainsKey($id) -or -not $enItems.data.ContainsKey($id)) {
        continue
    }

    Add-Term $terms $seen "item:$id" 'item' `
        ([string] $koItems.data[$id].name) `
        ([string] $zhItems.data[$id].name) `
        ([string] $enItems.data[$id].name)
}

$koRunes = @(Read-DragonJson 'ko_KR' 'runesReforged.json')
$zhRunes = @(Read-DragonJson 'zh_CN' 'runesReforged.json')
$enRunes = @(Read-DragonJson 'en_US' 'runesReforged.json')
$zhRunesById = @{}
$enRunesById = @{}
foreach ($style in $zhRunes) { $zhRunesById[[string] $style.id] = $style }
foreach ($style in $enRunes) { $enRunesById[[string] $style.id] = $style }

foreach ($koStyle in $koRunes) {
    $styleId = [string] $koStyle.id
    if (-not $zhRunesById.ContainsKey($styleId) -or -not $enRunesById.ContainsKey($styleId)) {
        continue
    }

    $zhStyle = $zhRunesById[$styleId]
    $enStyle = $enRunesById[$styleId]
    Add-Term $terms $seen "runeStyle:$styleId" 'rune' `
        ([string] $koStyle.name) ([string] $zhStyle.name) ([string] $enStyle.name)

    $zhById = @{}
    $enById = @{}
    foreach ($slot in $zhStyle.slots) {
        foreach ($rune in $slot.runes) { $zhById[[string] $rune.id] = $rune }
    }
    foreach ($slot in $enStyle.slots) {
        foreach ($rune in $slot.runes) { $enById[[string] $rune.id] = $rune }
    }

    foreach ($slot in $koStyle.slots) {
        foreach ($koRune in $slot.runes) {
            $runeId = [string] $koRune.id
            if (-not $zhById.ContainsKey($runeId) -or -not $enById.ContainsKey($runeId)) {
                continue
            }

            Add-Term $terms $seen "rune:$runeId" 'rune' `
                ([string] $koRune.name) `
                ([string] $zhById[$runeId].name) `
                ([string] $enById[$runeId].name)
        }
    }
}

$termCatalog = [ordered]@{
    version = $version
    terms = $terms
}
$termOutput = Join-Path $ProjectRoot 'Assets\lol_terms.json'
$termCatalog |
    ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $termOutput -Encoding utf8NoBOM

Write-Host "Generated $($heroRows.Count) heroes and $($terms.Count) official terms from Data Dragon $version."
