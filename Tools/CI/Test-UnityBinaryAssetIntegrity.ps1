<#
.SYNOPSIS
    Verifies that Git's text normalisation has not silently corrupted any binary Unity asset.

.DESCRIPTION
    This exists because of a real defect. .gitattributes carried "*.asset text eol=lf"
    from the very first commit. Most ScriptableObject assets really are YAML, so the rule
    looked harmless for months. But TerrainData, LightingData and NavMesh use the same
    extension with a binary NativeFormat payload, and an explicit "text" attribute makes
    Git convert line endings without guessing the content type. Committing
    Elven_Sanctuary_Ter.asset therefore stripped 92 CR bytes from its heightmap inside the
    clean filter. Nothing warned, the commit looked clean, the file still opened on the
    machine that built it, and the next checkout overwrote the last good copy. Unity could
    then no longer deserialise the terrain, so the formal task scene stopped loading.

    The dependency check next to this one did not catch it: it verified that Git tracks
    every dependency and that LFS files are pointers rather than fat blobs, but never that
    the tracked bytes were still the asset's own bytes.

    Two invariants are checked, neither of which needs the original asset package:

      1. A Unity SerializedFile declares its own total length in its header, so a
         truncated or normalised payload is self-evident: declared length must equal
         actual length.
      2. No tracked file that Git would classify as binary may carry an explicit "text"
         attribute. "auto" and "unset" are both safe, because Git then detects the binary
         payload itself and leaves it alone. Only an explicit "text" overrides that
         detection, which is what caused the corruption above.

    Invariant 2 is the preventive one: it fails while the attribute is still wrong,
    before anyone stages a binary asset and loses bytes that only the vendor package has.

    Reads no environment variables and prints no secrets.
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot,

    # Extensions Unity may serialise in its binary NativeFormat. Invariant 1 only applies
    # to these; everything else is covered by invariant 2.
    [string[]]$NativeAssetExtensions = @(
        ".asset", ".unity", ".prefab", ".mat", ".terrainlayer", ".lighting", ".cubemap",
        ".renderTexture", ".anim", ".controller", ".overrideController", ".mixer",
        ".physicMaterial", ".playable", ".signal", ".mask", ".preset", ".guiskin",
        ".fontsettings", ".flare", ".giparams", ".spriteatlas", ".shadervariants", ".brush"
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Git writes paths as UTF-8. PowerShell otherwise decodes a native command's output using
# whatever code page the launching console happens to use, which mangled the repository's
# Chinese-named documents into names that no longer exist on disk. They were then counted
# as absent and skipped — and whether that happened depended on how this script was
# launched, which is the worst kind of check: one that quietly verifies less than it says.
$previousConsoleEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

# Git's own binary heuristic only looks at the start of the file, so matching that window
# keeps this check and Git's decision in agreement.
$probeBytes = 8000

Push-Location $RepositoryRoot
try {
    $failures = [System.Collections.Generic.List[string]]::new()

    function Add-Failure([string]$message) {
        $failures.Add($message) | Out-Null
    }

    function Read-Head([string]$path, [int]$count) {
        $stream = [System.IO.File]::OpenRead($path)
        try {
            $buffer = New-Object byte[] $count
            $total = 0
            while ($total -lt $count) {
                $read = $stream.Read($buffer, $total, $count - $total)
                if ($read -le 0) { break }
                $total += $read
            }

            # The leading comma stops PowerShell unrolling the array into the pipeline.
            # Without it an empty file returns $null and a one-byte file returns a bare
            # byte, and neither has a Length for the caller to test. ProjectSettings
            # carries a zero-byte boot.config, so this path is real, not theoretical.
            if ($total -eq $count) { return , [byte[]]$buffer }
            if ($total -eq 0) { return , [byte[]]@() }
            return , [byte[]]($buffer[0..($total - 1)])
        }
        finally {
            $stream.Dispose()
        }
    }

    function Get-BigEndian([byte[]]$bytes, [int]$offset, [int]$width) {
        $value = [int64]0
        for ($i = 0; $i -lt $width; $i++) {
            $value = ($value -shl 8) -bor [int64]$bytes[$offset + $i]
        }
        return $value
    }

    # Returns the total length a Unity SerializedFile header claims, or -1 when the bytes
    # are not a SerializedFile header at all.
    function Get-DeclaredLength([byte[]]$head) {
        if ($head.Length -lt 48) { return [int64](-1) }

        $legacyMetadata = Get-BigEndian $head 0 4
        $legacyLength = Get-BigEndian $head 4 4
        $version = Get-BigEndian $head 8 4

        # Version 22 moved the sizes into 64-bit fields and zeroed the legacy ones.
        if ($version -ge 22 -and $version -le 100 -and $legacyMetadata -eq 0 -and $legacyLength -eq 0) {
            return Get-BigEndian $head 24 8
        }

        if ($version -ge 1 -and $version -lt 22 -and $legacyLength -gt 0) {
            return $legacyLength
        }

        return [int64](-1)
    }

    $trackedRaw = & git ls-files -z
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed; is $RepositoryRoot a Git repository?"
    }

    $tracked = @($trackedRaw -split "`0" | Where-Object { $_ })
    if ($tracked.Count -eq 0) {
        throw "git ls-files returned no tracked files."
    }

    Write-Host "tracked files: $($tracked.Count)"

    $nativeExtensions = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]$NativeAssetExtensions, [System.StringComparer]::OrdinalIgnoreCase)

    $binaryPaths = [System.Collections.Generic.List[string]]::new()
    $absentPaths = [System.Collections.Generic.List[string]]::new()
    $serializedChecked = 0
    $corrupt = 0
    $unrecognised = 0

    foreach ($path in $tracked) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            # Whether a tracked file ought to exist is the dependency check's business,
            # but every skip here is a file this check did not actually verify, so the
            # paths are named rather than merely counted. A file being written while the
            # scan runs lands here too.
            $absentPaths.Add($path) | Out-Null
            continue
        }

        $head = Read-Head $path $probeBytes
        if ($head.Length -eq 0) { continue }

        $hasNul = ([Array]::IndexOf($head, [byte]0) -ge 0)
        if ($hasNul) {
            $binaryPaths.Add($path) | Out-Null
        }

        $extension = [System.IO.Path]::GetExtension($path)
        if (-not $nativeExtensions.Contains($extension)) { continue }
        if (-not $hasNul) { continue }   # YAML, JSON or an unsmudged LFS pointer

        $declared = Get-DeclaredLength $head
        if ($declared -lt 0) {
            $unrecognised++
            Write-Host "note: binary payload with an unrecognised header: $path"
            continue
        }

        $serializedChecked++
        $actual = (Get-Item -LiteralPath $path).Length
        if ($declared -ne $actual) {
            $corrupt++
            $delta = $declared - $actual
            Add-Failure ("$path declares $declared bytes but is $actual on disk " +
                "(missing $delta). A binary Unity asset lost bytes, almost certainly to " +
                "end-of-line conversion. Restore it from the vendor package; Git cannot " +
                "recover the removed bytes.")
        }
    }

    # ---------------------------------------------------------------- attributes
    $explicitText = 0
    $answered = 0
    if ($binaryPaths.Count -gt 0) {
        # Deliberately not "check-attr --stdin": piping to a native command appends the
        # platform newline, so Git received every path with a trailing CR, matched it
        # against the catch-all rule instead of the one being audited, and reported a
        # reassuring answer for a path that does not exist. Passing the paths as arguments
        # avoids the newline entirely, and core.quotePath=false keeps non-ASCII paths
        # readable. Answers come back one per path, in order, so they are matched by
        # position rather than by re-parsing the path out of the message.
        $batchSize = 100
        $index = 0
        while ($index -lt $binaryPaths.Count) {
            $take = [Math]::Min($batchSize, $binaryPaths.Count - $index)
            $chunk = $binaryPaths.GetRange($index, $take)
            $index += $take

            $lines = @(& git -c core.quotePath=false check-attr text -- @chunk)
            if ($LASTEXITCODE -ne 0) {
                throw "git check-attr failed while auditing binary file attributes."
            }

            # A silent no-op here would make the whole check worthless, so a short answer
            # is a hard error rather than something to shrug at.
            if ($lines.Count -ne $chunk.Count) {
                throw ("git check-attr answered $($lines.Count) of $($chunk.Count) paths; " +
                    "the attribute audit cannot be trusted.")
            }

            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match ': text: (?<value>\S+)$') {
                    $answered++
                    if ($Matches['value'] -eq "set") {
                        $explicitText++
                        Add-Failure ("$($chunk[$i]) is binary but carries an explicit 'text' " +
                            "attribute. Git will strip CR bytes from it on the next stage. " +
                            "Use 'text=auto' or '-text' for this path.")
                    }
                }
                else {
                    throw "unparsable git check-attr output: $($lines[$i])"
                }
            }
        }

        if ($answered -ne $binaryPaths.Count) {
            throw ("audited $answered of $($binaryPaths.Count) binary files; " +
                "the attribute audit is incomplete.")
        }
    }

    # ---------------------------------------------------------------- summary
    Write-Host ""
    Write-Host "binary tracked files        : $($binaryPaths.Count)"
    Write-Host "SerializedFile length checks: $serializedChecked"
    Write-Host "length mismatch (corrupt)   : $corrupt"
    Write-Host "binary files audited        : $answered"
    Write-Host "binary with explicit text   : $explicitText"
    Write-Host "unrecognised binary header  : $unrecognised"
    Write-Host "not verified (absent)       : $($absentPaths.Count)"
    foreach ($absent in ($absentPaths | Select-Object -First 10)) {
        Write-Host "  not verified: $absent"
    }
    if ($absentPaths.Count -gt 10) {
        Write-Host "  ... and $($absentPaths.Count - 10) more"
    }
    Write-Host ""

    if ($failures.Count -gt 0) {
        foreach ($failure in ($failures | Select-Object -First 40)) {
            Write-Host "FAIL: $failure"
        }

        if ($failures.Count -gt 40) {
            Write-Host "... and $($failures.Count - 40) more"
        }

        throw "Unity binary asset integrity check failed with $($failures.Count) problem(s)."
    }

    Write-Host "Unity binary asset integrity check passed."
}
finally {
    Pop-Location
    [Console]::OutputEncoding = $previousConsoleEncoding
}
