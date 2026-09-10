param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
$projectPath = [System.IO.Path]::GetFullPath($ProjectRoot)
$assetRoot = [System.IO.Path]::GetFullPath((Join-Path $projectPath 'Assets\Aquarius Fantasy - High Elves'))
$backupRoot = [System.IO.Path]::GetFullPath((Join-Path $projectPath '..\artifacts\AquariusUrpBackup'))

if (-not $assetRoot.StartsWith($projectPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Asset root is outside the Unity project: $assetRoot"
}
if (-not (Test-Path -LiteralPath $assetRoot)) {
    throw "Aquarius asset folder not found: $assetRoot"
}

$nl = "`r`n"
$urpLitGuid = '933532a4fcc9baf4fa0491de14d08ed7'
$foliageGuid = 'a1f3c55a4d12484bb126c2ac65b5ef01'
$waterGuid = 'b2a462985c974454b8f6a4bc5925f102'
$waterfallGuid = 'c3b571ba6da743ae93e51477c4ae2103'

function Get-TextureBlock([string]$text, [string]$name) {
    $escaped = [regex]::Escape($name)
    return [regex]::Match($text, "(?ms)^    - ${escaped}:\r?\n.*?(?=^    - |^    m_Ints:)")
}

function Set-TextureSlot([string]$text, [string]$target, [string[]]$sources) {
    $sourceMatch = $null
    foreach ($source in $sources) {
        $candidate = Get-TextureBlock $text $source
        if ($candidate.Success -and $candidate.Value -notmatch 'm_Texture: \{fileID: 0\}') {
            $sourceMatch = $candidate
            break
        }
        if ($null -eq $sourceMatch -and $candidate.Success) {
            $sourceMatch = $candidate
        }
    }
    if ($null -eq $sourceMatch -or -not $sourceMatch.Success) {
        return $text
    }

    $newBlock = [regex]::Replace($sourceMatch.Value, "(?m)^    - [^:]+:", "    - ${target}:", 1)
    $targetMatch = Get-TextureBlock $text $target
    if ($targetMatch.Success) {
        return $text.Substring(0, $targetMatch.Index) + $newBlock + $text.Substring($targetMatch.Index + $targetMatch.Length)
    }
    return [regex]::Replace($text, '(?m)^    m_Ints:', [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $newBlock + $m.Value }, 1)
}

function Get-FloatValue([string]$text, [string]$name, [double]$fallback) {
    $match = [regex]::Match($text, "(?m)^    - $([regex]::Escape($name)): ([^\r\n]+)")
    if (-not $match.Success) { return $fallback }
    $value = 0.0
    if ([double]::TryParse($match.Groups[1].Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$value)) {
        return $value
    }
    return $fallback
}

function Set-FloatValue([string]$text, [string]$name, [double]$value) {
    $serialized = $value.ToString('0.######', [System.Globalization.CultureInfo]::InvariantCulture)
    $pattern = "(?m)^    - $([regex]::Escape($name)): [^\r\n]+"
    if ([regex]::IsMatch($text, $pattern)) {
        return [regex]::Replace($text, $pattern, "    - ${name}: $serialized", 1)
    }
    return [regex]::Replace($text, '(?m)^    m_Colors:', "    - ${name}: $serialized${nl}    m_Colors:", 1)
}

function Set-ColorSlot([string]$text, [string]$target, [string[]]$sources) {
    foreach ($source in $sources) {
        $pattern = "(?m)^    - $([regex]::Escape($source)): (\{[^\r\n]+\})"
        $match = [regex]::Match($text, $pattern)
        if (-not $match.Success) { continue }
        $targetPattern = "(?m)^    - $([regex]::Escape($target)): \{[^\r\n]+\}"
        $newLine = "    - ${target}: $($match.Groups[1].Value)"
        if ([regex]::IsMatch($text, $targetPattern)) {
            return [regex]::Replace($text, $targetPattern, $newLine, 1)
        }
        return [regex]::Replace($text, '(?m)^  m_BuildTextureStacks:', "${newLine}${nl}  m_BuildTextureStacks:", 1)
    }
    return $text
}

function Test-TextureAssigned([string]$text, [string]$name) {
    $match = Get-TextureBlock $text $name
    return $match.Success -and $match.Value -notmatch 'm_Texture: \{fileID: 0\}'
}

function Set-Keywords([string]$text, [string[]]$keywords) {
    $replacement = if ($keywords.Count -eq 0) {
        '  m_ValidKeywords: []' + $nl
    } else {
        '  m_ValidKeywords:' + $nl + (($keywords | ForEach-Object { "  - $_" }) -join $nl) + $nl
    }
    return [regex]::Replace($text, '(?ms)^  m_ValidKeywords:.*?(?=^  m_InvalidKeywords:)', $replacement, 1)
}

function Set-RenderTag([string]$text, [string]$renderType) {
    $replacement = if ([string]::IsNullOrEmpty($renderType)) {
        '  stringTagMap: {}' + $nl
    } else {
        "  stringTagMap:${nl}    RenderType: ${renderType}${nl}"
    }
    return [regex]::Replace($text, '(?ms)^  stringTagMap:.*?(?=^  disabledShaderPasses:)', $replacement, 1)
}

function Save-Backup([System.IO.FileInfo]$file) {
    $relative = [System.IO.Path]::GetRelativePath($projectPath, $file.FullName)
    $destination = Join-Path $backupRoot $relative
    $destinationDirectory = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    if (-not (Test-Path -LiteralPath $destination)) {
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}

$converted = 0
$skippedSkybox = 0
$materials = Get-ChildItem -LiteralPath $assetRoot -Recurse -Filter '*.mat' -File
foreach ($materialFile in $materials) {
    $text = [System.IO.File]::ReadAllText($materialFile.FullName)
    $shaderMatch = [regex]::Match($text, '(?m)^  m_Shader: \{fileID: ([^,]+), guid: ([0-9a-f]+), type: ([^}]+)\}')
    if (-not $shaderMatch.Success) { continue }

    $oldFileId = $shaderMatch.Groups[1].Value
    $oldGuid = $shaderMatch.Groups[2].Value
    if ($oldGuid -eq $urpLitGuid) {
        $relativeMaterialPath = [System.IO.Path]::GetRelativePath($projectPath, $materialFile.FullName)
        $originalMaterialPath = Join-Path $backupRoot $relativeMaterialPath
        $originalWasGraph = $true
        if (Test-Path -LiteralPath $originalMaterialPath) {
            $originalText = [System.IO.File]::ReadAllText($originalMaterialPath)
            $originalShader = [regex]::Match($originalText, '(?m)^  m_Shader: \{fileID: ([^,]+), guid: ([0-9a-f]+)')
            $originalWasGraph = $originalShader.Success -and $originalShader.Groups[2].Value -ne '0000000000000000f000000000000000'
        }
        $colorSources = if ($originalWasGraph) { @('_Albedo_Color', '_Color') } else { @('_Color', '_Albedo_Color') }
        $updatedText = Set-ColorSlot $text '_BaseColor' $colorSources
        if ($updatedText -ne $text) {
            Save-Backup $materialFile
            [System.IO.File]::WriteAllText($materialFile.FullName, $updatedText, [System.Text.UTF8Encoding]::new($false))
            $converted++
        }
        continue
    }
    if ($oldGuid -in @($foliageGuid, $waterGuid, $waterfallGuid)) { continue }
    if ($oldGuid -eq '0000000000000000f000000000000000' -and $oldFileId -eq '108') {
        $skippedSkybox++
        continue
    }

    $targetGuid = $null
    $targetFileId = '4800000'
    $keywords = @()
    $renderType = ''
    $queue = -1

    if ($oldGuid -eq '1a990567e3a4fa847826061cb6e1bba7') {
        $targetGuid = $foliageGuid
        $keywords = @('_ALPHATEST_ON')
        $renderType = 'TransparentCutout'
        $queue = 2450
    } elseif ($oldGuid -eq 'e48f517cb46f20243a268277c8c95fb7') {
        $targetGuid = $waterGuid
        $renderType = 'Transparent'
        $queue = 3000
    } elseif ($oldGuid -eq 'e7ca17732f192dc469d5f8b58a26d6f1') {
        $targetGuid = $waterfallGuid
        $renderType = 'Transparent'
        $queue = 3000
    } else {
        $targetGuid = $urpLitGuid
        $graphMaterial = $oldGuid -ne '0000000000000000f000000000000000'
        if ($graphMaterial) {
            $text = Set-TextureSlot $text '_BaseMap' @('_Albedo', '_MainTex')
            $text = Set-TextureSlot $text '_BumpMap' @('_Normal', '_BumpMap')
            $text = Set-TextureSlot $text '_MetallicGlossMap' @('_Metallic', '_MetallicGlossMap')
            $text = Set-TextureSlot $text '_OcclusionMap' @('_AO_1', '_OcclusionMap')
            $text = Set-ColorSlot $text '_BaseColor' @('_Albedo_Color', '_Color')
            $text = Set-FloatValue $text '_Metallic' (Get-FloatValue $text '_Metallic_Strength' 0.0)
            $text = Set-FloatValue $text '_BumpScale' (Get-FloatValue $text '_Normal_Strength' 1.0)
        } else {
            $text = Set-TextureSlot $text '_BaseMap' @('_MainTex', '_Albedo')
            $text = Set-ColorSlot $text '_BaseColor' @('_Color', '_Albedo_Color')
        }

        $oldMode = Get-FloatValue $text '_Mode' 0.0
        $transparent = $text -match '(?m)^  m_CustomRenderQueue: 3000$' -or $oldMode -ge 2.0
        if (Test-TextureAssigned $text '_BumpMap') { $keywords += '_NORMALMAP' }
        if (Test-TextureAssigned $text '_MetallicGlossMap') { $keywords += '_METALLICSPECGLOSSMAP' }
        if (Test-TextureAssigned $text '_OcclusionMap') { $keywords += '_OCCLUSIONMAP' }
        if (Test-TextureAssigned $text '_EmissionMap') { $keywords += '_EMISSION' }

        $text = Set-FloatValue $text '_WorkflowMode' 1.0
        $text = Set-FloatValue $text '_AlphaClip' 0.0
        $text = Set-FloatValue $text '_Cull' 2.0
        $text = Set-FloatValue $text '_QueueOffset' 0.0
        $text = Set-FloatValue $text '_QueueControl' -1.0
        if ($transparent) {
            $keywords += '_SURFACE_TYPE_TRANSPARENT'
            $renderType = 'Transparent'
            $queue = 3000
            $text = Set-FloatValue $text '_Surface' 1.0
            $text = Set-FloatValue $text '_Blend' 0.0
            $text = Set-FloatValue $text '_SrcBlend' 5.0
            $text = Set-FloatValue $text '_DstBlend' 10.0
            $text = Set-FloatValue $text '_ZWrite' 0.0
        } else {
            $renderType = 'Opaque'
            $queue = -1
            $text = Set-FloatValue $text '_Surface' 0.0
            $text = Set-FloatValue $text '_Blend' 0.0
            $text = Set-FloatValue $text '_SrcBlend' 1.0
            $text = Set-FloatValue $text '_DstBlend' 0.0
            $text = Set-FloatValue $text '_ZWrite' 1.0
        }
    }

    Save-Backup $materialFile
    $newShaderLine = "  m_Shader: {fileID: ${targetFileId}, guid: ${targetGuid}, type: 3}"
    $text = [regex]::Replace($text, '(?m)^  m_Shader: \{[^\r\n]+\}', $newShaderLine, 1)
    $text = [regex]::Replace($text, '(?m)^  m_CustomRenderQueue: -?\d+', "  m_CustomRenderQueue: $queue", 1)
    $text = Set-RenderTag $text $renderType
    $text = Set-Keywords $text ($keywords | Sort-Object -Unique)
    [System.IO.File]::WriteAllText($materialFile.FullName, $text, [System.Text.UTF8Encoding]::new($false))
    $converted++
}

$urpAsset = Join-Path $projectPath 'Assets\Game\Settings\NarakaUniversalRenderPipeline.asset'
if (Test-Path -LiteralPath $urpAsset) {
    $urpFile = Get-Item -LiteralPath $urpAsset
    Save-Backup $urpFile
    $urpText = [System.IO.File]::ReadAllText($urpAsset)
    $urpText = [regex]::Replace($urpText, '(?m)^  m_RequireDepthTexture: 0$', '  m_RequireDepthTexture: 1', 1)
    [System.IO.File]::WriteAllText($urpAsset, $urpText, [System.Text.UTF8Encoding]::new($false))
}

Write-Output "Converted materials: $converted"
Write-Output "Preserved supported skybox materials: $skippedSkybox"
Write-Output "Backup directory: $backupRoot"
