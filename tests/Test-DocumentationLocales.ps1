param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sources = @('README.md', 'VALIDATION.md', 'docs/capture-contents.md', 'docs/evidence-format.md', 'docs/model-coverage.md', 'docs/support-evidence-requirements.md')
$locales = @('zh-CN', 'ru-RU')
function Get-Slug([string]$heading) {
    return ($heading.Trim().ToLowerInvariant() -replace '[^\p{L}\p{N}_\- ]', '') -replace ' ', '-'
}
function Assert-EqualMatches([string]$source, [string]$translation, [string]$pattern, [string]$label) {
    $a = @([regex]::Matches($source, $pattern) | ForEach-Object { $_.Value } | Sort-Object)
    $b = @([regex]::Matches($translation, $pattern) | ForEach-Object { $_.Value } | Sort-Object)
    if (($a -join "`n") -cne ($b -join "`n")) { throw "$label differs from English source" }
}
function Normalize-NumericGrouping([string]$text) {
    # English commas and Russian spaces group thousands without changing the value.
    return [regex]::Replace($text, '(?<![\p{L}\p{N}])\d{1,3}(?:[, \u00A0\u202F]\d{3})+(?!\d)', {
        param($match)
        $match.Value -replace '[, \u00A0\u202F]', ''
    })
}
function Get-ParagraphCount([string]$text) {
    return [regex]::Matches($text.Trim(), '\S[\s\S]*?(?:\r?\n\s*\r?\n|$)').Count
}
$checked = 0
$links = 0
foreach ($source in $sources) {
    $sourcePath = Join-Path $root $source
    $english = [IO.File]::ReadAllText($sourcePath)
    if ($english -match '(?<!\r)\n') { throw "$source must use CRLF" }
    foreach ($locale in $locales) {
        $relative = if ($source -eq 'README.md') { "README.$locale.md" } else { "docs/$locale/$(Split-Path $source -Leaf)" }
        $file = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $file)) { throw "Missing translation: $relative" }
        $text = [IO.File]::ReadAllText($file)
        if ($text -match '(?<!\r)\n' -or $text -match '\r(?!\n)') { throw "$relative must use CRLF" }
        if ($text.Contains([char]0xFFFD)) { throw "$relative contains a replacement character" }
        $plain = $text -replace '(?m)^<a id="[^"]+"></a>\r?\n', ''
        Assert-EqualMatches $english $plain '(?<!`)`[^`\r\n]+`(?!`)' "$relative inline literals"
        Assert-EqualMatches $english $plain '```[\s\S]*?```' "$relative fenced code"
        Assert-EqualMatches $english $plain '(?m)^#{1,6} ' "$relative heading levels"
        Assert-EqualMatches $english $plain '(?m)^\s*(?:-|\d+\.) ' "$relative list structure"
        Assert-EqualMatches (Normalize-NumericGrouping $english) (Normalize-NumericGrouping $plain) '(?<![\p{L}\p{N}])\d+(?:[.,:]\d+)*(?![\p{L}\p{N}])' "$relative numeric facts"
        Assert-EqualMatches $english $plain 'https?://[^\s)\]>]+' "$relative external links"
        if ((Get-ParagraphCount $english) -ne (Get-ParagraphCount $plain)) { throw "$relative paragraph coverage differs" }
        $expected = @([regex]::Matches($english, '(?m)^#{2,6} (.+)$') | ForEach-Object { Get-Slug $_.Groups[1].Value })
        $actual = @([regex]::Matches($text, '<a id="([^"]+)"></a>') | ForEach-Object { $_.Groups[1].Value })
        if (($expected -join "`n") -cne ($actual -join "`n")) { throw "$relative English fragment anchors differ" }
        if ($plain -notmatch '\[English\]\(' -or $plain -notmatch '\u7b80\u4f53\u4e2d\u6587' -or $plain -notmatch '\u0420\u0443\u0441\u0441\u043a\u0438\u0439') { throw "$relative language navigation missing" }
        foreach ($match in [regex]::Matches($text, '\[[^\]]*\]\(([^)]+)\)')) {
            $href = $match.Groups[1].Value
            if ($href -match '^[a-z]+://') { continue }
            $parts = $href.Split('#', 2)
            $target = if ($parts[0]) { [IO.Path]::GetFullPath((Join-Path (Split-Path $file -Parent) $parts[0])) } else { $file }
            if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "$relative broken local link: $href" }
            if ($parts.Count -gt 1 -and $parts[1]) {
                $targetText = [IO.File]::ReadAllText($target)
                $ids = @([regex]::Matches($targetText, '<a id="([^"]+)"></a>') | ForEach-Object { $_.Groups[1].Value })
                $ids += @([regex]::Matches($targetText, '(?m)^#{1,6} (.+)$') | ForEach-Object { Get-Slug $_.Groups[1].Value })
                if ($parts[1] -notin $ids) { throw "$relative missing link fragment: $href" }
            }
            $links++
        }
        $checked++
    }
}
Write-Host "PASS: $checked translations across $($sources.Count) sources; heading/list/paragraph coverage, code, numbers, URLs, anchors, $links local links, navigation and CRLF."
