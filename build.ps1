param(
    [string]$GameManagedDir = "",
    [string]$OutputDir = "",
    [string]$CompilerDirectory = ""
)

$ErrorActionPreference = 'Stop'

$sourceDir = $PSScriptRoot
$workspaceRoot = Split-Path -Parent $sourceDir

if ([string]::IsNullOrEmpty($GameManagedDir)) {
    $GameManagedDir = Join-Path $workspaceRoot 'Managed'
}
if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path (Join-Path $workspaceRoot 'mod') 'SmartQuickTransfer'
}

$assemblyName = 'SmartQuickTransfer'
$outDll = Join-Path $OutputDir ($assemblyName + '.dll')

if (-not (Test-Path $GameManagedDir)) {
    throw "Game managed folder not found: $GameManagedDir"
}
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

$references = @(
    'mscorlib.dll',
    'System.dll',
    'System.Core.dll',
    'System.Runtime.dll',
    'netstandard.dll',
    'UnityEngine.dll',
    'UnityEngine.CoreModule.dll',
    'UnityEngine.UI.dll',
    'UnityEngine.UIModule.dll',
    'UnityEngine.TextRenderingModule.dll',
    'UnityEngine.InputLegacyModule.dll',
    'UnityEngine.IMGUIModule.dll',
    'UnityEngine.ImageConversionModule.dll',
    'UnityEngine.AnimationModule.dll',
    'UnityEngine.Physics2DModule.dll',
    'UnityEngine.AudioModule.dll',
    'Unity.TextMeshPro.dll',
    'Assembly-CSharp.dll',
    '0Harmony.dll'
)

$referenceArgs = @()
foreach ($name in $references) {
    $path = Join-Path $GameManagedDir $name
    if (Test-Path $path) {
        $referenceArgs += ('/reference:' + $path)
    }
    else {
        Write-Host ("  missing reference (skipped): " + $name)
    }
}

$sources = Get-ChildItem -Path $sourceDir -Recurse -Filter '*.cs' | Select-Object -ExpandProperty FullName

if ($sources.Count -eq 0) {
    throw "No C# source files found under $sourceDir"
}

if (-not [string]::IsNullOrEmpty($CompilerDirectory)) {
    $compiler = Get-ChildItem -Path $CompilerDirectory -Filter '*.dll' -File |
        Where-Object { Test-Path (Join-Path $_.DirectoryName ($_.BaseName + '.runtimeconfig.json')) } |
        Where-Object { $_.BaseName -notlike 'VB*' } |
        Select-Object -First 1
    if ($null -eq $compiler) {
        throw "No command line compiler found in $CompilerDirectory"
    }

    $compilerArgs = @()
    $compilerArgs += '/noconfig'
    $compilerArgs += '/nostdlib+'
    $compilerArgs += '/target:library'
    $compilerArgs += '/langversion:7.3'
    $compilerArgs += '/platform:anycpu'
    $compilerArgs += '/optimize+'
    $compilerArgs += '/deterministic+'
    $compilerArgs += ('/out:' + $outDll)
    $compilerArgs += $referenceArgs
    $compilerArgs += $sources

    Write-Host ("Compiling with " + $compiler.FullName)
    & dotnet exec $compiler.FullName @compilerArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed with exit code $LASTEXITCODE"
    }
}
else {
    Write-Host "Compiling with the .NET SDK"
    & dotnet build (Join-Path $sourceDir ($assemblyName + '.csproj')) -c Release -o $OutputDir ("-p:GameManagedDir=" + $GameManagedDir)
    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed with exit code $LASTEXITCODE"
    }
}

Copy-Item -Path (Join-Path $sourceDir 'manifest.xml') -Destination (Join-Path $OutputDir 'manifest.xml') -Force

Write-Host ""
Write-Host ("Built: " + $outDll)
Write-Host ("Mod folder ready: " + $OutputDir)
