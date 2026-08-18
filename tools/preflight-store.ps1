<#
.SYNOPSIS
    Store submission preflight (Roadmap 10.4b): is this build actually submittable?

.DESCRIPTION
    The twin of tools\preflight-launch.ps1. That one asks the deployed SERVER whether it is fit to open;
    this one asks the REPOSITORY and the store assets whether an upload will be accepted. It is section 0
    plus the per-platform paperwork of docs/store/release-checklist.md, turned into assertions:

      1. VERSION    - tools/version.json, ProjectSettings.asset and Api.csproj must agree. They did not,
                      until 10.4b noticed: ProjectSettings said 1.0 while version.json said 0.1.0, which
                      means set-version.ps1 had never been run against the tree. A machine should catch
                      that, not a person reading three files.
      2. ANDROID    - target API 36 (Google Play requires it for new apps AND updates from 31 August
                      2026), a sane min SDK, and a versionCode that matches version.json. Play refuses an
                      upload that reuses a versionCode, so this number only ever goes up.
      3. SIGNING    - tools/keystore.local.ps1 exists, still has no CHANGE_ME in it, points at a keystore
                      file that is really there, and is NOT tracked by git. The file is PARSED, never
                      dot-sourced: running it would load the signing passwords into this shell.
      4. ASSETS     - every required image present AND the right size TO THE PIXEL, read out of the PNG
                      header rather than trusted. Apple rejects an image one pixel off outright, and
                      Play blocks publishing entirely without a feature graphic. The alpha channel is
                      checked too: the Play icon needs one, the feature graphic and the App Store icon
                      must not have one - a classic rejection that costs a review cycle.
      5. STEAM      - the App ID and depot ids are no longer the 0000000 placeholders.
      6. LEGAL      - with -SiteUrl, the published privacy policy / EULA / account-deletion pages answer
                      200 AND do not still contain a DRAFT banner or an unfilled [CONTACT EMAIL]. A live
                      URL that serves a draft is worse than no URL: it is the page a reviewer opens.

    Exit code 0 when everything a machine can settle passed, 1 otherwise. Everything else - the artwork
    being good, the age-rating questionnaire, the lawyer's read - is printed as the MANUAL list.

    Nothing here uploads, builds or signs anything. It is read-only.

.PARAMETER AssetPath
    Where the store artwork lives. Default: store-assets\ at the repository root. The expected layout is
    printed when something is missing, so the first run doubles as the specification.

.PARAMETER SiteUrl
    The published web root, e.g. https://play.example.com - enables the legal-page checks.

.PARAMETER Platform
    Limit the report to one storefront: Play, AppStore, Steam, Web. Default: all of them.

.EXAMPLE
    .\tools\preflight-store.ps1
    .\tools\preflight-store.ps1 -Platform Play -SiteUrl https://play.example.com

.NOTES
    ASCII-only and PowerShell 5.1 safe.
#>

[CmdletBinding()]
param(
    [string]$AssetPath = "",
    [string]$SiteUrl = "",
    [ValidateSet("All", "Play", "AppStore", "Steam", "Web")]
    [string]$Platform = "All"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($AssetPath)) { $AssetPath = Join-Path $root "store-assets" }

$script:Failures = 0
$script:Warnings = 0
$script:Manual = New-Object System.Collections.ArrayList

function Write-Step([string]$text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }
function Write-Ok([string]$text)   { Write-Host "   OK   $text" -ForegroundColor Green }
function Write-Bad([string]$text)  { Write-Host "   FAIL $text" -ForegroundColor Red; $script:Failures++ }
function Write-Warn([string]$text) { Write-Host "   WARN $text" -ForegroundColor Yellow; $script:Warnings++ }
function Write-Info([string]$text) { Write-Host "        $text" -ForegroundColor DarkGray }
function Check([bool]$condition, [string]$text) { if ($condition) { Write-Ok $text } else { Write-Bad $text } }
function Manual([string]$text) { [void]$script:Manual.Add($text) }

function Want([string]$name) { return ($Platform -eq "All" -or $Platform -eq $name) }

# --- reading a PNG without decoding it ------------------------------------------------------------
# The IHDR chunk is fixed at the start of every PNG: 8-byte signature, 4-byte length, "IHDR", then
# width and height as big-endian 32-bit integers, then bit depth and COLOUR TYPE. Colour type 6 is
# RGBA and 4 is grey+alpha; 2 and 0 are the same without an alpha channel. That is everything the
# stores' rules are written in terms of, and it costs 26 bytes to read.
function Get-PngInfo([string]$path) {
    $bytes = New-Object -TypeName "byte[]" -ArgumentList 26
    $stream = [System.IO.File]::OpenRead($path)
    try {
        $read = $stream.Read($bytes, 0, 26)
        if ($read -lt 26) { return $null }
    } finally {
        $stream.Close()
    }

    if ($bytes[0] -ne 0x89 -or $bytes[1] -ne 0x50 -or $bytes[2] -ne 0x4E -or $bytes[3] -ne 0x47) { return $null }

    $width  = ($bytes[16] -shl 24) -bor ($bytes[17] -shl 16) -bor ($bytes[18] -shl 8) -bor $bytes[19]
    $height = ($bytes[20] -shl 24) -bor ($bytes[21] -shl 16) -bor ($bytes[22] -shl 8) -bor $bytes[23]
    $colourType = $bytes[25]

    return @{
        Width = $width
        Height = $height
        ColourType = $colourType
        HasAlpha = ($colourType -eq 4 -or $colourType -eq 6)
    }
}

# $Alpha: $true = must have one, $false = must NOT have one, $null = do not care.
function Test-Asset {
    param(
        [string]$Relative, [int]$Width, [int]$Height, $Alpha = $null, [switch]$Optional
    )

    $path = Join-Path $AssetPath $Relative
    if (-not (Test-Path $path)) {
        if ($Optional) { Write-Info "$Relative - not provided (optional)" }
        else { Write-Bad "$Relative MISSING (needs $($Width)x$($Height))" }
        return
    }

    if ($path -notlike "*.png") {
        Write-Warn "$Relative is not a PNG - dimensions not verified. Apple validates to the pixel; use PNG or check by hand."
        return
    }

    $info = Get-PngInfo $path
    if ($null -eq $info) { Write-Bad "$Relative is not a readable PNG"; return }

    if ($info.Width -ne $Width -or $info.Height -ne $Height) {
        Write-Bad ("$Relative is {0}x{1}, must be {2}x{3}" -f $info.Width, $info.Height, $Width, $Height)
        return
    }

    if ($null -ne $Alpha) {
        if ($Alpha -and -not $info.HasAlpha) {
            Write-Bad "$Relative has NO alpha channel and needs one"
            return
        }
        if (-not $Alpha -and $info.HasAlpha) {
            Write-Bad "$Relative HAS an alpha channel and must not - this is a routine store rejection"
            return
        }
    }

    Write-Ok ("$Relative {0}x{1}" -f $info.Width, $info.Height)
}

# Screenshots are a COUNT plus a size, and the names are free-form within a folder.
function Test-Screenshots {
    param([string]$Folder, [int]$Width, [int]$Height, [int]$Min, [int]$Max)

    $dir = Join-Path $AssetPath $Folder
    if (-not (Test-Path $dir)) {
        Write-Bad "$Folder/ MISSING - needs $Min to $Max screenshots at $($Width)x$($Height)"
        return
    }

    $imageExtensions = @(".png", ".jpg", ".jpeg")
    $shots = @(Get-ChildItem $dir -File | Where-Object { $imageExtensions -contains $_.Extension.ToLower() } | Sort-Object Name)
    if ($shots.Count -lt $Min) {
        Write-Bad "$Folder/ has $($shots.Count) screenshot(s), needs at least $Min"
    } elseif ($shots.Count -gt $Max) {
        Write-Bad "$Folder/ has $($shots.Count) screenshot(s), the store accepts at most $Max"
    } else {
        Write-Ok "$Folder/ has $($shots.Count) screenshot(s)"
    }

    foreach ($shot in $shots) {
        if ($shot.Extension -ne ".png") {
            Write-Warn "$Folder/$($shot.Name) is not a PNG - size not verified"
            continue
        }
        $info = Get-PngInfo $shot.FullName
        if ($null -eq $info) { Write-Bad "$Folder/$($shot.Name) is not a readable PNG"; continue }
        if ($info.Width -ne $Width -or $info.Height -ne $Height) {
            Write-Bad ("$Folder/{0} is {1}x{2}, must be {3}x{4}" -f $shot.Name, $info.Width, $info.Height, $Width, $Height)
        }
    }
}

function Get-Body([string]$url) {
    try {
        $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 20
        return @{ Status = [int]$resp.StatusCode; Body = $resp.Content }
    } catch {
        $status = -1
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        return @{ Status = $status; Body = "" }
    }
}

Write-Host "FTS store preflight (Roadmap 10.4b)" -ForegroundColor White
Write-Host "Assets: $AssetPath"

# ------------------------------------------------------------------------------- 1. version
Write-Step "1. One version, everywhere"
$versionFile = Join-Path $PSScriptRoot "version.json"
$cfg = $null
if (-not (Test-Path $versionFile)) {
    Write-Bad "tools/version.json missing"
} else {
    $cfg = Get-Content $versionFile -Raw | ConvertFrom-Json
    Write-Info ("version.json: {0} ({1}), androidVersionCode {2}, iosBuildNumber {3}" -f `
        $cfg.version, $cfg.channel, $cfg.androidVersionCode, $cfg.iosBuildNumber)

    $psPath = Join-Path $root "client/ProjectSettings/ProjectSettings.asset"
    $csPath = Join-Path $root "server/Api/Api.csproj"

    if (Test-Path $psPath) {
        $ps = Get-Content $psPath -Raw
        $bundle = ([regex]::Match($ps, '(?m)^\s*bundleVersion:\s*(.+)$')).Groups[1].Value.Trim()
        $code = ([regex]::Match($ps, '(?m)^\s*AndroidBundleVersionCode:\s*(\d+)')).Groups[1].Value.Trim()
        Check ($bundle -eq $cfg.version) "ProjectSettings bundleVersion $bundle matches version.json"
        Check ($code -eq [string]$cfg.androidVersionCode) "ProjectSettings AndroidBundleVersionCode $code matches version.json"
    } else {
        Write-Bad "ProjectSettings.asset not found"
    }

    if (Test-Path $csPath) {
        $cs = Get-Content $csPath -Raw
        $apiVersion = ([regex]::Match($cs, '<Version>([^<]+)</Version>')).Groups[1].Value.Trim()
        Check ($apiVersion -eq $cfg.version) "Api.csproj Version $apiVersion matches version.json"
    } else {
        Write-Bad "server/Api/Api.csproj not found"
    }

    if ($cfg.channel -eq "alpha" -or $cfg.channel -eq "beta") {
        Write-Info "channel is '$($cfg.channel)' - fine for a test track, decide before a public release"
    }
}

# ------------------------------------------------------------------------------- 2. android
if (Want "Play") {
    Write-Step "2. Android build settings"
    $psPath = Join-Path $root "client/ProjectSettings/ProjectSettings.asset"
    if (Test-Path $psPath) {
        $ps = Get-Content $psPath -Raw
        $target = ([regex]::Match($ps, '(?m)^\s*AndroidTargetSdkVersion:\s*(\d+)')).Groups[1].Value
        $min = ([regex]::Match($ps, '(?m)^\s*AndroidMinSdkVersion:\s*(\d+)')).Groups[1].Value

        if ($target -eq "0") {
            Write-Bad "AndroidTargetSdkVersion is 0 (auto = whatever SDK this machine happens to have). Google Play requires API 36 for new apps and updates from 31 August 2026 - pin it."
        } elseif ([int]$target -lt 36) {
            Write-Bad "AndroidTargetSdkVersion is $target - Google Play requires 36 or higher from 31 August 2026 (an extension to 1 November can be requested in the Play Console)"
        } else {
            Write-Ok "AndroidTargetSdkVersion $target"
            Manual "SDK Platform $target must be INSTALLED (Unity Hub / Android SDK manager) or the build fails - this script reads the setting, it cannot see your SDK folder."
        }

        if ($min -ne "") { Write-Info "AndroidMinSdkVersion $min" }
    }
}

# ------------------------------------------------------------------------------- 3. signing
if (Want "Play") {
    Write-Step "3. Android signing"
    $creds = Join-Path $PSScriptRoot "keystore.local.ps1"
    if (-not (Test-Path $creds)) {
        Write-Bad "tools/keystore.local.ps1 missing - copy keystore.local.example.ps1 and fill it in"
        Manual "Create the release keystore with keytool, and BACK IT UP off this machine - without it the app can never be updated under the same identity unless Play App Signing is on."
    } else {
        # PARSED, never dot-sourced: executing it would load the signing passwords into this shell.
        $text = Get-Content $creds -Raw
        if ($text -match "CHANGE_ME") {
            Write-Bad "tools/keystore.local.ps1 still contains CHANGE_ME"
        } else {
            Write-Ok "tools/keystore.local.ps1 has no placeholders left"
        }

        $ksPath = ([regex]::Match($text, '(?m)FTS_ANDROID_KEYSTORE\s*=\s*"([^"]+)"')).Groups[1].Value
        if ([string]::IsNullOrWhiteSpace($ksPath)) {
            Write-Bad "FTS_ANDROID_KEYSTORE is not set in keystore.local.ps1"
        } elseif (-not (Test-Path $ksPath)) {
            Write-Bad "the keystore file is not at $ksPath"
        } else {
            Write-Ok "keystore file present"
        }

        try {
            $tracked = & git ls-files --error-unmatch $creds 2>$null
            if ($LASTEXITCODE -eq 0 -and $tracked) {
                Write-Bad "tools/keystore.local.ps1 is TRACKED BY GIT - the signing passwords are in the repository history"
            } else {
                Write-Ok "tools/keystore.local.ps1 is not tracked by git"
            }
        } catch {
            Write-Info "git not available - could not check whether the credentials file is tracked"
        }
    }
}

# ------------------------------------------------------------------------------- 4. assets
Write-Step "4. Store artwork"
if (-not (Test-Path $AssetPath)) {
    Write-Bad "$AssetPath does not exist"
    Write-Host ""
    Write-Host "Expected layout (sizes from docs/store/asset-specs.md):" -ForegroundColor Yellow
    Write-Host @"
  store-assets/
    play/      icon-512.png (alpha)  feature-1024x500.png (NO alpha)  phone/ 2-8 at 1080x1920
    appstore/  icon-1024.png (NO alpha)                               iphone/ 3-10 at 1320x2868
    steam/     capsule-small-231x87.png  capsule-header-460x215.png  capsule-main-616x353.png
               capsule-vertical-374x448.png  library-600x900.png  library-hero-3840x1240.png
               library-logo-1280x720.png (alpha)  community-184x184.png  screenshots/ 5+ at 1920x1080
    web/       favicon-32.png  favicon-180.png  og-1200x630.png
"@ -ForegroundColor DarkGray
} else {
    if (Want "Play") {
        Write-Info "Google Play"
        Test-Asset -Relative "play/icon-512.png" -Width 512 -Height 512 -Alpha $true
        Test-Asset -Relative "play/feature-1024x500.png" -Width 1024 -Height 500 -Alpha $false
        Test-Screenshots -Folder "play/phone" -Width 1080 -Height 1920 -Min 2 -Max 8
    }
    if (Want "AppStore") {
        Write-Info "App Store"
        Test-Asset -Relative "appstore/icon-1024.png" -Width 1024 -Height 1024 -Alpha $false
        Test-Screenshots -Folder "appstore/iphone" -Width 1320 -Height 2868 -Min 3 -Max 10
    }
    if (Want "Steam") {
        Write-Info "Steam"
        Test-Asset -Relative "steam/capsule-small-231x87.png" -Width 231 -Height 87
        Test-Asset -Relative "steam/capsule-header-460x215.png" -Width 460 -Height 215
        Test-Asset -Relative "steam/capsule-main-616x353.png" -Width 616 -Height 353
        Test-Asset -Relative "steam/capsule-vertical-374x448.png" -Width 374 -Height 448
        Test-Asset -Relative "steam/library-600x900.png" -Width 600 -Height 900
        Test-Asset -Relative "steam/library-hero-3840x1240.png" -Width 3840 -Height 1240
        Test-Asset -Relative "steam/library-logo-1280x720.png" -Width 1280 -Height 720 -Alpha $true
        Test-Asset -Relative "steam/community-184x184.png" -Width 184 -Height 184
        Test-Screenshots -Folder "steam/screenshots" -Width 1920 -Height 1080 -Min 5 -Max 20
    }
    if (Want "Web") {
        Write-Info "Web"
        Test-Asset -Relative "web/favicon-32.png" -Width 32 -Height 32 -Optional
        Test-Asset -Relative "web/favicon-180.png" -Width 180 -Height 180 -Optional
        Test-Asset -Relative "web/og-1200x630.png" -Width 1200 -Height 630 -Optional
    }
    Manual "No real club, competition, sponsor or player name or logo may appear in any store image - the game generates its own identities precisely so this is never a problem, but a screenshot can still catch one."
}

# ------------------------------------------------------------------------------- 5. steam ids
if (Want "Steam") {
    Write-Step "5. Steam configuration"
    $steamCfgPath = Join-Path $PSScriptRoot "steam/steam.config.json"
    if (-not (Test-Path $steamCfgPath)) {
        Write-Bad "tools/steam/steam.config.json missing"
    } else {
        $steam = Get-Content $steamCfgPath -Raw | ConvertFrom-Json
        if ($steam.appId -eq "0000000" -or [string]::IsNullOrWhiteSpace($steam.appId)) {
            Write-Bad "steam.config.json still has the placeholder appId - fill it in from the Steamworks partner site"
        } else {
            Write-Ok "Steam appId $($steam.appId)"
        }

        foreach ($os in @("windows", "mac", "linux")) {
            $depot = $steam.depots.$os
            if ([string]::IsNullOrWhiteSpace($depot) -or $depot -like "000000*") {
                Write-Bad "the $os depot id is still a placeholder"
            } else {
                Write-Ok "$os depot $depot"
            }
        }

        if ($steam.branch -eq "default") {
            Write-Warn "steam.config.json branch is 'default' - an upload goes LIVE to everyone. Use a named beta branch until the build has been played end to end."
        }
    }
}

# ------------------------------------------------------------------------------- 6. legal pages
Write-Step "6. The published legal pages"
if ([string]::IsNullOrWhiteSpace($SiteUrl)) {
    Write-Info "no -SiteUrl: the published pages were not checked"
    Manual "Publish the legal pages (.\tools\build-legal-pages.ps1 then .\tools\deploy-web.ps1 -PagesOnly -Deploy) and re-run with -SiteUrl."
} else {
    $site = $SiteUrl.TrimEnd('/')
    $pages = @("privacy-policy.html", "privacy-policy.it.html", "eula.html", "eula.it.html", "delete-account.html")
    foreach ($page in $pages) {
        $result = Get-Body "$site/$page"
        if ($result.Status -ne 200) {
            Write-Bad "$page -> $($result.Status)"
            continue
        }
        # A live URL serving a draft is worse than no URL - it is the page a reviewer opens.
        if ($result.Body -match "NOT FOR PUBLICATION" -or $result.Body -match "\[CONTACT EMAIL\]" -or $result.Body -match "\[EMAIL DI CONTATTO\]" -or $result.Body -match "\[DATE\]" -or $result.Body -match "\[DATA\]") {
            Write-Bad "$page is LIVE but still a draft (placeholder or DRAFT banner in the page)"
        } else {
            Write-Ok "$page -> 200, no placeholders"
        }
    }
}

# ------------------------------------------------------------------------------- verdict
Manual "Age rating questionnaires (IARC through Play, Steam's own, Apple's) - the answers are drafted in docs/store/release-checklist.md."
Manual "Text limits (title 30 / short 80 / full 4000 on Play; name 30 / subtitle 30 / keywords 100 on Apple) - the copy is in docs/store/store-copy.md and is not machine-checked here."
Manual "A lawyer's read of the privacy policy and the EULA, and a real contact address in both."

Write-Host ""
Write-Host "----------------------------------------------------------------" -ForegroundColor White
if ($script:Failures -eq 0) {
    Write-Host "STORE PREFLIGHT PASSED" -ForegroundColor Green -NoNewline
    Write-Host ("  ({0} warning(s))" -f $script:Warnings)
} else {
    Write-Host ("STORE PREFLIGHT FAILED - {0} blocker(s), {1} warning(s)" -f $script:Failures, $script:Warnings) -ForegroundColor Red
}

if ($script:Manual.Count -gt 0) {
    Write-Host ""
    Write-Host "Still on you (this script cannot settle these):" -ForegroundColor Yellow
    foreach ($item in $script:Manual) { Write-Host "  - $item" -ForegroundColor Yellow }
}

exit ([int]($script:Failures -gt 0))
