# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 ClearCode Inc.

# PopGuard を自己完結型 (self-contained) の Release として発行し、続けて
# Inno Setup でインストーラをビルドします。対象 PC に .NET ランタイムは不要です。
#
#   pwsh -File build-release.ps1
#   pwsh -File build-release.ps1 -SkipInstaller   # 発行だけ行う

[CmdletBinding()]
param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'PopGuard\PopGuard.csproj'
$issFile = Join-Path $root 'PopGuard.iss'

Write-Host '== 発行 (self-contained Release win-x64) =='
dotnet publish $project -p:PublishProfile=win-x64-selfcontained
if ($LASTEXITCODE -ne 0) { throw "dotnet publish に失敗しました ($LASTEXITCODE)" }

$publishDir = Join-Path $root 'PopGuard\bin\Release\net9.0-windows10.0.22621.0\win-x64\publish'
Write-Host "発行先: $publishDir"

if ($SkipInstaller) {
    Write-Host 'インストーラのビルドは省略しました (-SkipInstaller)。'
    return
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning 'Inno Setup (ISCC.exe) が見つかりません。インストーラのビルドを省略します。'
    Write-Warning '発行物は上記の発行先にあります。Inno Setup を入れてから ISCC.exe PopGuard.iss を実行してください。'
    return
}

Write-Host '== インストーラのビルド (Inno Setup) =='
& $iscc $issFile
if ($LASTEXITCODE -ne 0) { throw "ISCC に失敗しました ($LASTEXITCODE)" }

Write-Host "完了: $(Join-Path $root 'SetupOutput')"
