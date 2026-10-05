# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 ClearCode Inc.

# docs\parameter-sheet.xlsm の出力を検証します。
#
#   1. 初期状態のまま出力すると、既定の設定 (ルール空・既定の抑止時間・言語 auto) になること
#   2. 各シートに値を入れると、それが JSON に正しく載ること
#      (Windows のパスの \ が \\ に、" が \" になる、といったエスケープを含む)
#   3. 入力チェックが誤りを拾うこと
#
# ブックは読み取り専用で開き、変更は保存しません。
#
#   pwsh -File tools\parameter-sheet\test-parameter-sheet.ps1

[CmdletBinding()]
param(
    [string]$WorkbookPath
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..\..')
if (-not $WorkbookPath) {
    $WorkbookPath = Join-Path $repoRoot 'docs\parameter-sheet.xlsm'
}

$script:failures = 0

# PowerShell 7 からは $xl.Run(...) が COM の省略引数を渡せないため、InvokeMember で呼ぶ。
# 戻り値が空文字のときは $null になるので、文字列に揃えて返す。
function Invoke-Macro {
    param($Excel, [string]$Name)
    [string]$Excel.GetType().InvokeMember('Run', 'InvokeMethod', $null, $Excel, @($Name))
}

function Assert-Equal {
    param([string]$Label, $Expected, $Actual)

    if ($Expected -eq $Actual) {
        Write-Host "  ok   $Label"
    }
    else {
        Write-Host "  FAIL $Label" -ForegroundColor Red
        Write-Host "       expected: $Expected" -ForegroundColor Red
        Write-Host "       actual:   $Actual" -ForegroundColor Red
        $script:failures++
    }
}

function Assert-True {
    param([string]$Label, [bool]$Condition)
    Assert-Equal -Label $Label -Expected $true -Actual $Condition
}

# テーブルの「値」列のセルを、内部パラメータ名で引く。
function Get-ParamCell {
    param($Workbook, [string]$TableName, [string]$ParamName)

    foreach ($sheet in $Workbook.Worksheets) {
        foreach ($table in $sheet.ListObjects) {
            if ($table.Name -ne $TableName) { continue }
            $nameCol = $table.ListColumns('内部パラメータ名').Index
            $valueCol = $table.ListColumns('値').Index
            for ($i = 1; $i -le $table.DataBodyRange.Rows.Count; $i++) {
                if ("$($table.DataBodyRange.Cells($i, $nameCol).Value2)".Trim() -eq $ParamName) {
                    return $table.DataBodyRange.Cells($i, $valueCol)
                }
            }
        }
    }
    throw "$TableName に $ParamName がありません"
}

function Get-Table {
    param($Workbook, [string]$TableName)

    foreach ($sheet in $Workbook.Worksheets) {
        foreach ($table in $sheet.ListObjects) {
            if ($table.Name -eq $TableName) { return $table }
        }
    }
    throw "テーブルが見つかりません: $TableName"
}

$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false
$xl.DisplayAlerts = $false

try {
    $wb = $xl.Workbooks.Open($WorkbookPath, $false, $true)  # UpdateLinks, ReadOnly

    Write-Host '初期状態の出力'
    Assert-Equal -Label '入力チェックを通る' -Expected '' -Actual (Invoke-Macro $xl 'ValidationMessage')

    $config = (Invoke-Macro $xl 'BuildJson') | ConvertFrom-Json
    Assert-True  -Label 'JSON として解釈できる' -Condition ($null -ne $config)
    Assert-Equal -Label '初期はルールが空' -Expected 0 -Actual $config.rules.Count
    Assert-Equal -Label '既定の抑止時間が 5 件' -Expected 5 -Actual $config.durations.Count
    Assert-Equal -Label 'durations[0].label' -Expected '30分' -Actual $config.durations[0].label
    Assert-Equal -Label 'durations[0].minutes' -Expected 30 -Actual $config.durations[0].minutes
    Assert-Equal -Label 'durations[4] は無制限 (0 分)' -Expected 0 -Actual $config.durations[4].minutes
    Assert-Equal -Label 'autoSuppressDuringFocus の既定は true' -Expected $true -Actual $config.autoSuppressDuringFocus
    Assert-Equal -Label 'language の既定は auto' -Expected 'auto' -Actual $config.language

    Write-Host ''
    Write-Host '値を入れたときの出力'

    (Get-ParamCell $wb 'T_General' 'AutoSuppressDuringFocus').Value2 = '無効'
    (Get-ParamCell $wb 'T_General' 'Language').Value2 = '英語'

    $rules = Get-Table $wb 'A_Rules_Items'
    $rules.DataBodyRange.Cells(1, 1).Value2 = '有効'
    $rules.DataBodyRange.Cells(1, 2).Value2 = 'ToastTester'
    $rules.DataBodyRange.Cells(1, 3).Value2 = 'Save C:\tmp\x "y"'
    $rules.DataBodyRange.Cells(1, 5).Value2 = '最小化'
    # 「TOPMOSTのみ」(6 列目) は空欄のまま -> 既定の true になる

    $durations = Get-Table $wb 'A_Durations_Items'
    $durations.DataBodyRange.Cells(6, 1).Value2 = '90分'
    $durations.DataBodyRange.Cells(6, 2).Value2 = 90

    $json = Invoke-Macro $xl 'BuildJson'
    Assert-Equal -Label '入力チェックを通る' -Expected '' -Actual (Invoke-Macro $xl 'ValidationMessage')

    $config = $json | ConvertFrom-Json
    Assert-Equal -Label 'autoSuppressDuringFocus' -Expected $false -Actual $config.autoSuppressDuringFocus
    Assert-Equal -Label 'language が内部値になる' -Expected 'en' -Actual $config.language

    Assert-Equal -Label 'rules の件数' -Expected 1 -Actual $config.rules.Count
    Assert-Equal -Label 'rules[0].enabled' -Expected $true -Actual $config.rules[0].enabled
    Assert-Equal -Label 'rules[0].process' -Expected 'ToastTester' -Actual $config.rules[0].process
    Assert-Equal -Label 'rules[0].title (\ と " を含む)' -Expected 'Save C:\tmp\x "y"' -Actual $config.rules[0].title
    Assert-True  -Label 'JSON 上で \ が 2 文字・" が \" になっている' `
        -Condition ($json -match [regex]::Escape('Save C:\\tmp\\x \"y\"'))
    Assert-Equal -Label 'rules[0].hide が内部値になる' -Expected 'Minimize' -Actual $config.rules[0].hide
    Assert-Equal -Label 'rules[0].topMostOnly は空欄で true' -Expected $true -Actual $config.rules[0].topMostOnly

    Assert-Equal -Label 'durations の件数 (既定 5 + 追加 1)' -Expected 6 -Actual $config.durations.Count
    Assert-Equal -Label '追加した durations の label' -Expected '90分' -Actual $config.durations[5].label
    Assert-Equal -Label '追加した durations の minutes' -Expected 90 -Actual $config.durations[5].minutes

    Write-Host ''
    Write-Host '入力チェック'

    (Get-ParamCell $wb 'T_General' 'Language').Value2 = 'フランス語'
    Assert-True -Label '未対応の言語なら止まる' `
        -Condition ((Invoke-Macro $xl 'ValidationMessage') -match '表示言語')
    (Get-ParamCell $wb 'T_General' 'Language').Value2 = '英語'

    $rules.DataBodyRange.Cells(2, 3).Value2 = 'プロセスなしのルール'
    Assert-True -Label 'プロセス名のない行があれば止まる' `
        -Condition ((Invoke-Macro $xl 'ValidationMessage') -match 'プロセス名')
    $rules.DataBodyRange.Cells(2, 3).Value2 = ''

    $durations.DataBodyRange.Cells(7, 1).Value2 = '不正'
    $durations.DataBodyRange.Cells(7, 2).Value2 = ''
    Assert-True -Label '分が空欄なら止まる' `
        -Condition ((Invoke-Macro $xl 'ValidationMessage') -match '分')
    $durations.DataBodyRange.Cells(7, 2).Value2 = -1
    Assert-True -Label '分が負数なら止まる' `
        -Condition ((Invoke-Macro $xl 'ValidationMessage') -match '0 以上')
    $durations.DataBodyRange.Cells(7, 1).Value2 = ''
    $durations.DataBodyRange.Cells(7, 2).Value2 = ''
    Assert-Equal -Label '不正な行を消すと通る' -Expected '' -Actual (Invoke-Macro $xl 'ValidationMessage')

    $wb.Close($false)
}
finally {
    $xl.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) | Out-Null
}

Write-Host ''
if ($script:failures -gt 0) {
    Write-Host "$script:failures 件失敗しました。" -ForegroundColor Red
    exit 1
}
Write-Host 'すべて成功しました。' -ForegroundColor Green
