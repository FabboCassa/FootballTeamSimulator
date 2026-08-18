<#
.SYNOPSIS
    Turn the store legal documents into publishable HTML pages (Roadmap 10.4b).

.DESCRIPTION
    Both mobile stores require a privacy policy as a LIVE URL, not a document in a repository -
    it is a submission blocker, and today those documents only exist as markdown under docs/store.
    This converts them into self-contained pages in web/, which tools\deploy-web.ps1 already publishes
    alongside delete-account.html:

        docs/store/privacy-policy.md      -> web/privacy-policy.html
        docs/store/privacy-policy.it.md   -> web/privacy-policy.it.html
        docs/store/eula.md                -> web/eula.html
        docs/store/eula.it.md             -> web/eula.it.html

    The markdown stays the single source of truth. Regenerate after every edit; do not hand-edit the
    HTML, it says so at the top of each file.

    IT REFUSES TO BUILD A PUBLISHABLE PAGE OUT OF A DRAFT. The documents currently carry unfilled
    placeholders ([DATE], [CONTACT EMAIL], [X.Y.Z]) and a "Draft. Have a lawyer read this" banner.
    A privacy policy published with "[CONTACT EMAIL]" where the contact address should be is worse
    than no page at all: it is the page a reviewer opens. So a leftover placeholder is an ERROR with
    an exit code, and -AllowDraft only downgrades it to a warning while stamping a loud DRAFT banner
    into the page itself, so a preview can never be mistaken for the real thing.

    No dependencies: the converter handles exactly the markdown these four documents use - h1/h2,
    paragraphs, horizontal rules, unordered lists, pipe tables, **bold**, `code` and _italic_.
    Anything else is left as literal text rather than guessed at.

.PARAMETER AllowDraft
    Build anyway with placeholders present, stamping a visible DRAFT banner. For previewing layout.

.EXAMPLE
    .\tools\build-legal-pages.ps1
    .\tools\build-legal-pages.ps1 -AllowDraft

.NOTES
    ASCII-only and PowerShell 5.1 safe.
#>

[CmdletBinding()]
param(
    [switch]$AllowDraft
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$storeDir = Join-Path $root "docs/store"
$webDir = Join-Path $root "web"

$script:Problems = 0

function Write-Ok([string]$text)   { Write-Host "   OK   $text" -ForegroundColor Green }
function Write-Bad([string]$text)  { Write-Host "   FAIL $text" -ForegroundColor Red; $script:Problems++ }
function Write-Warn([string]$text) { Write-Host "   WARN $text" -ForegroundColor Yellow }

# The four documents, and how they link to each other in the page header.
$docs = @(
    @{ Source = "privacy-policy.md";    Target = "privacy-policy.html";    Lang = "en"; Kind = "privacy" },
    @{ Source = "privacy-policy.it.md"; Target = "privacy-policy.it.html"; Lang = "it"; Kind = "privacy" },
    @{ Source = "eula.md";              Target = "eula.html";              Lang = "en"; Kind = "eula" },
    @{ Source = "eula.it.md";           Target = "eula.it.html";           Lang = "it"; Kind = "eula" }
)

# A placeholder left in a published policy is the failure this script exists to prevent. Both language
# sets are listed here rather than per-document: a document is checked against all of them, so an Italian
# marker accidentally left in the English file is still caught.
$placeholders = @(
    "[DATE]", "[CONTACT EMAIL]", "[X.Y.Z]", "[DATA]", "[EMAIL DI CONTATTO]",
    "Draft. Have a lawyer", "Bozza. Falla leggere"
)

function ConvertTo-HtmlText([string]$text) {
    # Escape first, THEN add markup, or an escaped entity could be re-escaped.
    $t = $text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
    $t = [regex]::Replace($t, '\*\*(.+?)\*\*', '<strong>$1</strong>')
    $t = [regex]::Replace($t, '`(.+?)`', '<code>$1</code>')
    # Underscore italics only when the underscores hug the run - avoids mangling file_names.
    $t = [regex]::Replace($t, '(?<![A-Za-z0-9_])_([^_]+)_(?![A-Za-z0-9_])', '<em>$1</em>')
    return $t
}

function Split-TableRow([string]$line) {
    $trimmed = $line.Trim().Trim('|')
    return ($trimmed -split '\|') | ForEach-Object { $_.Trim() }
}

function ConvertTo-Html([string[]]$lines) {
    $sb = New-Object System.Text.StringBuilder
    $para = New-Object System.Collections.ArrayList
    $list = New-Object System.Collections.ArrayList
    $table = New-Object System.Collections.ArrayList

    function Flush-Para {
        if ($para.Count -gt 0) {
            [void]$sb.AppendLine("<p>" + (ConvertTo-HtmlText ($para -join " ")) + "</p>")
            $para.Clear()
        }
    }
    function Flush-List {
        if ($list.Count -gt 0) {
            [void]$sb.AppendLine("<ul>")
            foreach ($item in $list) { [void]$sb.AppendLine("  <li>" + (ConvertTo-HtmlText $item) + "</li>") }
            [void]$sb.AppendLine("</ul>")
            $list.Clear()
        }
    }
    function Flush-Table {
        if ($table.Count -gt 0) {
            [void]$sb.AppendLine("<table>")
            $first = $true
            foreach ($row in $table) {
                # The |---|---| separator row carries no content.
                if (($row -join "") -match '^[-: ]+$') { continue }
                $cellTag = if ($first) { "th" } else { "td" }
                [void]$sb.Append("  <tr>")
                foreach ($cell in $row) {
                    [void]$sb.Append("<$cellTag>" + (ConvertTo-HtmlText $cell) + "</$cellTag>")
                }
                [void]$sb.AppendLine("</tr>")
                $first = $false
            }
            [void]$sb.AppendLine("</table>")
            $table.Clear()
        }
    }
    function Flush-All { Flush-Para; Flush-List; Flush-Table }

    foreach ($raw in $lines) {
        $line = $raw.TrimEnd()

        if ($line.Trim().Length -eq 0) { Flush-All; continue }

        if ($line -match '^\s*---+\s*$') { Flush-All; [void]$sb.AppendLine("<hr>"); continue }

        if ($line -match '^(#{1,4})\s+(.*)$') {
            Flush-All
            $level = $matches[1].Length
            [void]$sb.AppendLine("<h$level>" + (ConvertTo-HtmlText $matches[2].Trim()) + "</h$level>")
            continue
        }

        if ($line -match '^\s*[-*]\s+(.*)$') {
            Flush-Para; Flush-Table
            [void]$list.Add($matches[1].Trim())
            continue
        }

        if ($line.Trim().StartsWith("|")) {
            Flush-Para; Flush-List
            [void]$table.Add((Split-TableRow $line))
            continue
        }

        # Anything else is prose. Soft-wrapped lines join into one paragraph.
        Flush-List; Flush-Table
        [void]$para.Add($line.Trim())
    }

    Flush-All
    return $sb.ToString()
}

function New-Page {
    param([hashtable]$Doc, [string]$Body, [bool]$IsDraft)

    $isIt = ($Doc.Lang -eq "it")
    $title = if ($Doc.Kind -eq "privacy") {
        if ($isIt) { "Informativa sulla privacy" } else { "Privacy Policy" }
    } else {
        if ($isIt) { "Contratto di licenza" } else { "End-User Licence Agreement" }
    }

    # Cross-links: the other language of this document, the other document, and account deletion.
    $otherLang = if ($isIt) { $Doc.Target -replace '\.it\.html$', '.html' } else { $Doc.Target -replace '\.html$', '.it.html' }
    $otherLangLabel = if ($isIt) { "English" } else { "Italiano" }
    $otherDoc = if ($Doc.Kind -eq "privacy") {
        if ($isIt) { @{ Href = "eula.it.html"; Label = "Licenza d'uso" } } else { @{ Href = "eula.html"; Label = "Licence" } }
    } else {
        if ($isIt) { @{ Href = "privacy-policy.it.html"; Label = "Privacy" } } else { @{ Href = "privacy-policy.html"; Label = "Privacy" } }
    }
    $deleteLabel = if ($isIt) { "Elimina account" } else { "Delete account" }

    $draftBanner = ""
    if ($IsDraft) {
        $draftBanner = '<div class="draft">DRAFT - NOT FOR PUBLICATION. This page still contains unfilled placeholders.</div>'
    }

    $header = @"
<!DOCTYPE html>
<html lang="$($Doc.Lang)">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>$title | Football Team Simulator</title>
<meta name="robots" content="index, follow">
<!-- GENERATED by tools/build-legal-pages.ps1 from docs/store/$($Doc.Source) - do not edit by hand. -->
<style>
  :root{
    --bg:#141C30; --surface:#1D2740; --surface-alt:#26304C; --border:#36415F;
    --text:#EAF0FF; --muted:#9AA7C4; --accent:#27C281; --amber:#F2B33D; --danger:#E0574B;
  }
  *{box-sizing:border-box}
  body{
    margin:0; padding:32px 16px; background:var(--bg); color:var(--text);
    font-family:system-ui,-apple-system,"Segoe UI",Roboto,Arial,sans-serif; line-height:1.6;
  }
  main{max-width:760px; margin:0 auto}
  .card{background:var(--surface); border:1px solid var(--border); border-radius:14px; padding:24px 28px}
  nav{display:flex; gap:14px; flex-wrap:wrap; margin-bottom:20px; font-size:14px}
  nav a{color:var(--accent); text-decoration:none}
  nav a:hover{text-decoration:underline}
  h1{font-size:26px; margin:0 0 18px; line-height:1.3}
  h2{font-size:18px; margin:28px 0 8px; color:var(--accent)}
  h3{font-size:16px; margin:20px 0 6px}
  p{margin:10px 0}
  ul{padding-left:20px} li{margin:5px 0}
  code{background:var(--surface-alt); padding:1px 5px; border-radius:5px; font-size:.92em}
  hr{border:0; border-top:1px solid var(--border); margin:26px 0}
  table{border-collapse:collapse; width:100%; margin:14px 0; font-size:14px}
  th,td{border:1px solid var(--border); padding:8px 10px; text-align:left; vertical-align:top}
  th{background:var(--surface-alt)}
  em{color:var(--muted); font-style:normal}
  .draft{
    background:rgba(224,87,75,.15); border:1px solid var(--danger); color:#FFB4AC;
    padding:12px 14px; border-radius:10px; margin-bottom:20px; font-weight:700; font-size:14px;
  }
  footer{color:var(--muted); font-size:13px; text-align:center; margin-top:18px}
  footer a{color:var(--accent)}
  /* The privacy policy's "what we collect / why / legal basis" table is three columns of prose, which
     at phone width squeezes into three narrow ribbons. Give the cells their space back rather than
     forcing a horizontal scroll, which would hide the legal-basis column - the one a reviewer looks
     for. Checked at 390px. */
  @media (max-width:600px){
    body{padding:18px 10px}
    .card{padding:18px 16px}
    table{font-size:13px}
    th,td{padding:6px 7px}
  }
</style>
</head>
<body>
<main>
  <div class="card">
    <nav>
      <a href="$otherLang">$otherLangLabel</a>
      <a href="$($otherDoc.Href)">$($otherDoc.Label)</a>
      <a href="delete-account.html">$deleteLabel</a>
    </nav>
    $draftBanner
"@

    $footer = @"
  </div>
  <footer>Football Team Simulator &middot; Fabio Casarini</footer>
</main>
</body>
</html>
"@

    return $header + "`r`n" + $Body + $footer
}

# ------------------------------------------------------------------------------------------------
Write-Host "Building the legal pages (Roadmap 10.4b)" -ForegroundColor White

if (-not (Test-Path $webDir)) { New-Item -ItemType Directory -Path $webDir | Out-Null }

$anyDraft = $false

foreach ($doc in $docs) {
    $sourcePath = Join-Path $storeDir $doc.Source
    if (-not (Test-Path $sourcePath)) {
        Write-Bad "$($doc.Source) not found in docs/store"
        continue
    }

    $text = Get-Content $sourcePath -Raw -Encoding UTF8
    $found = @()
    foreach ($p in $placeholders) {
        if ($text.Contains($p)) { $found += $p }
    }

    $isDraft = $false
    if ($found.Count -gt 0) {
        $isDraft = $true
        $anyDraft = $true
        $list = ($found -join ", ")
        if ($AllowDraft) {
            Write-Warn "$($doc.Source) still contains: $list - building with a DRAFT banner"
        } else {
            Write-Bad "$($doc.Source) still contains: $list - fill these in before publishing (or pass -AllowDraft to preview)"
            continue
        }
    }

    $body = ConvertTo-Html (Get-Content $sourcePath -Encoding UTF8)
    $page = New-Page -Doc $doc -Body $body -IsDraft $isDraft

    $targetPath = Join-Path $webDir $doc.Target
    [System.IO.File]::WriteAllText($targetPath, $page, (New-Object System.Text.UTF8Encoding($false)))
    Write-Ok ("web/{0} ({1:N1} KB)" -f $doc.Target, ((Get-Item $targetPath).Length / 1KB))
}

Write-Host ""
if ($script:Problems -gt 0) {
    Write-Host "NOT PUBLISHABLE - $($script:Problems) document(s) are still drafts." -ForegroundColor Red
    Write-Host ""
    Write-Host "To publish, in each of docs/store/privacy-policy*.md and eula*.md:" -ForegroundColor Yellow
    Write-Host "  - replace [CONTACT EMAIL] / [EMAIL DI CONTATTO] with a real address you will read" -ForegroundColor Yellow
    Write-Host "  - replace [DATE] / [DATA] and [X.Y.Z] with the publication date and the version" -ForegroundColor Yellow
    Write-Host "  - delete the 'Draft / Bozza' banner paragraph once a lawyer has read it" -ForegroundColor Yellow
    Write-Host "Then re-run this, and publish with .\tools\deploy-web.ps1 -PagesOnly -Deploy" -ForegroundColor Yellow
    exit 1
}

if ($anyDraft) {
    Write-Host "Built WITH DRAFT BANNERS - preview only, do not deploy." -ForegroundColor Yellow
    exit 1
}

Write-Host "Pages built. Publish them with .\tools\deploy-web.ps1 -PagesOnly -Deploy" -ForegroundColor Green
Write-Host "Then enter the resulting URLs in the Play Console and App Store Connect." -ForegroundColor DarkGray
exit 0
