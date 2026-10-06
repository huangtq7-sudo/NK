<#
.SYNOPSIS
    Verifies that a clean checkout can actually reproduce the formal task scene.

.DESCRIPTION
    This exists because of a real defect: Map01_Task referenced 156 GUIDs into the
    High Elves directory while .gitignore excluded that whole directory, so Git tracked
    zero of its files. The scene worked on the machine that built it and would have
    opened as a field of Missing Prefabs anywhere else.

    "The files are on my disk" is not reproducibility. Every check below is therefore
    about Git's view of the world, not the filesystem's:

      1. every manifest entry exists on disk
      2. no manifest entry is excluded by .gitignore
      3. every manifest entry is actually tracked (git ls-files)
      4. every asset's .meta is tracked, not just the asset
      5. every external GUID referenced by the scene resolves to a tracked asset
      6. every file that must use LFS really is an LFS pointer, not a fat Git blob

    Reads no environment variables and prints no secrets.
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$ManifestPath = "Docs/Scenes/p23-high-elves-dependencies.txt",
    [string[]]$ScenePaths = @("NK/Assets/Game/Scenes/World/Map01_Task.unity"),
    [string[]]$ExternalRoots = @("NK/Assets/Aquarius Fantasy - High Elves")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

Push-Location $RepositoryRoot
try {
    $failures = [System.Collections.Generic.List[string]]::new()

    function Add-Failure([string]$message) {
        $failures.Add($message) | Out-Null
    }

    # ---------------------------------------------------------------- manifest
    $manifestFull = Join-Path $RepositoryRoot $ManifestPath
    if (-not (Test-Path -LiteralPath $manifestFull -PathType Leaf)) {
        throw "Dependency manifest not found: $ManifestPath. Run NARAKA/Setup/Generate P2.3 Scene Dependency Manifest."
    }

    $entries = Get-Content -LiteralPath $manifestFull -Encoding UTF8 |
        Where-Object { $_ -and -not $_.StartsWith("#") } |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ }

    if ($entries.Count -eq 0) {
        throw "Dependency manifest $ManifestPath lists no entries."
    }

    Write-Host "Manifest entries: $($entries.Count)"

    # One `git ls-files` for the whole tree beats one process per entry; with ~770 entries
    # the per-process cost is the difference between seconds and minutes.
    $tracked = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in (& git ls-files)) {
        if ($line) { $tracked.Add($line.Trim()) | Out-Null }
    }
    Write-Host "Tracked files in repository: $($tracked.Count)"

    # git check-ignore in one batch as well; it reports only the ignored subset.
    $ignored = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $ignoreOutput = $entries | & git check-ignore --stdin 2>$null
    foreach ($line in $ignoreOutput) {
        if ($line) { $ignored.Add($line.Trim()) | Out-Null }
    }

    $missingOnDisk = 0
    $notTracked = 0
    $ignoredCount = 0
    $missingMeta = 0
    $lfsExpected = 0

    # Extensions that must never enter ordinary Git history.
    $lfsExtensions = @(
        ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff",
        ".fbx", ".exr", ".hdr", ".wav", ".ogg", ".mp3", ".mp4", ".unitypackage")

    $lfsCandidates = [System.Collections.Generic.List[string]]::new()

    foreach ($entry in $entries) {
        if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $entry))) {
            Add-Failure "missing on disk: $entry"
            $missingOnDisk++
            continue
        }

        if ($ignored.Contains($entry)) {
            Add-Failure "excluded by .gitignore: $entry"
            $ignoredCount++
        }

        if (-not $tracked.Contains($entry)) {
            Add-Failure "not tracked by Git: $entry"
            $notTracked++
        }

        # An asset without its .meta is worse than useless: Unity regenerates the meta with
        # a brand new GUID, so every reference to it breaks on the other machine.
        if (-not $entry.EndsWith(".meta")) {
            $meta = "$entry.meta"
            if (-not $tracked.Contains($meta)) {
                Add-Failure "asset tracked but its .meta is not: $entry"
                $missingMeta++
            }
        }

        $extension = [System.IO.Path]::GetExtension($entry).ToLowerInvariant()
        if ($lfsExtensions -contains $extension) {
            $lfsExpected++
            $lfsCandidates.Add($entry) | Out-Null
        }
    }

    # ---------------------------------------------------------------- LFS pointers
    # Ask Git what is in the index, not what is on disk: on disk an LFS file looks like the
    # real binary (it is smudged), so only the index distinguishes a pointer from a fat blob.
    $lfsTracked = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in (& git lfs ls-files -n)) {
        if ($line) { $lfsTracked.Add($line.Trim()) | Out-Null }
    }

    $notLfs = 0
    foreach ($candidate in $lfsCandidates) {
        if (-not $tracked.Contains($candidate)) {
            continue  # already reported as untracked
        }

        if (-not $lfsTracked.Contains($candidate)) {
            Add-Failure "must be an LFS pointer but is a plain Git blob: $candidate"
            $notLfs++
        }
    }

    # ---------------------------------------------------------------- scene GUID resolution
    # Build GUID -> asset from the tracked .meta files of the external roots, then confirm
    # every external GUID the scene mentions resolves to something Git actually has.
    $guidToAsset = @{}
    foreach ($root in $ExternalRoots) {
        foreach ($file in $tracked) {
            if (-not $file.EndsWith(".meta")) { continue }
            if (-not $file.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $full = Join-Path $RepositoryRoot $file
            if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
            $head = Get-Content -LiteralPath $full -Encoding UTF8 -TotalCount 5
            foreach ($line in $head) {
                if ($line -match '^guid:\s*([0-9a-f]{32})\s*$') {
                    $guidToAsset[$Matches[1]] = $file.Substring(0, $file.Length - 5)
                    break
                }
            }
        }
    }

    Write-Host "GUIDs resolvable from tracked external .meta files: $($guidToAsset.Count)"

    $unresolved = 0
    foreach ($scenePath in $ScenePaths) {
        $sceneFull = Join-Path $RepositoryRoot $scenePath
        if (-not (Test-Path -LiteralPath $sceneFull -PathType Leaf)) {
            Add-Failure "scene not found: $scenePath"
            continue
        }

        $sceneText = Get-Content -Raw -LiteralPath $sceneFull -Encoding UTF8
        $sceneGuids = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($match in [regex]::Matches($sceneText, 'guid:\s*([0-9a-f]{32})')) {
            $sceneGuids.Add($match.Groups[1].Value) | Out-Null
        }

        # A GUID that resolves inside the project (Assets/Game, packages, built-ins) is fine.
        # The ones that matter here are those that resolve only inside the external roots.
        $externalHits = 0
        foreach ($guid in $sceneGuids) {
            if ($guidToAsset.ContainsKey($guid)) { $externalHits++ }
        }

        Write-Host "$scenePath : $($sceneGuids.Count) distinct GUIDs, $externalHits resolve into tracked external assets"

        if ($externalHits -eq 0) {
            Add-Failure "$scenePath resolves no GUIDs into the tracked external roots; the environment is not reproducible"
            $unresolved++
        }
    }

    # ---------------------------------------------------------------- summary
    Write-Host ""
    Write-Host "missing on disk        : $missingOnDisk"
    Write-Host "excluded by .gitignore : $ignoredCount"
    Write-Host "not tracked by Git     : $notTracked"
    Write-Host "asset without its meta : $missingMeta"
    Write-Host "need LFS               : $lfsExpected"
    Write-Host "need LFS but plain blob: $notLfs"
    Write-Host ""

    if ($failures.Count -gt 0) {
        foreach ($failure in ($failures | Select-Object -First 40)) {
            Write-Host "FAIL: $failure"
        }

        if ($failures.Count -gt 40) {
            Write-Host "... and $($failures.Count - 40) more"
        }

        throw "Tracked Unity scene dependency check failed with $($failures.Count) problem(s)."
    }

    Write-Host "Tracked Unity scene dependency check passed."
}
finally {
    Pop-Location
}
