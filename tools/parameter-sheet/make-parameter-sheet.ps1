# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Copyright (c) 2026 ClearCode Inc.

# docs\parameter-sheet.xlsm を作り直します。
#
# シートの体裁と ExportConfig.bas をこのスクリプトが埋め込むため、パラメータの
# 追加や文言の修正はここと .bas を直してから再実行してください
# (xlsm を直接編集しても、次回の実行で上書きされます)。
#
# 実行には Excel と、[ファイル] > [オプション] > [トラスト センター] >
# [トラスト センターの設定] > [マクロの設定] にある
# 「VBA プロジェクト オブジェクト モデルへのアクセスを信頼する」が必要です。
#
#   pwsh -File tools\parameter-sheet\make-parameter-sheet.ps1

[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..\..')
if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot 'docs\parameter-sheet.xlsm'
}
$vbaPath = Join-Path $scriptDir 'ExportConfig.bas'

# Excel の列挙体 (COM 越しでは名前で参照できないため定数で持つ)
$xlSrcRange = 1
$xlYes = 1
$xlValidateList = 3
$xlValidateWholeNumber = 1
$xlValidAlertStop = 1
$xlGreaterEqual = 7
$xlOpenXMLWorkbookMacroEnabled = 52
$msoShapeRoundedRectangle = 5
$vbextCtStdModule = 1

$COLOR_ACCENT = 7949855      # 濃紺 (ボタン地)
$COLOR_WHITE = 16777215
$COLOR_HEADING = 7949855
$COLOR_MUTED = 8421504

function New-Param {
    param(
        [string]$Group,
        [string]$Label,
        $Value,
        [string]$Name,
        [ValidateSet('bool', 'int', 'text', 'choice')][string]$Kind,
        [string[]]$Choices = @(),
        [string]$Note = ''
    )
    [pscustomobject]@{
        Group = $Group; Label = $Label; Value = $Value
        Name = $Name; Kind = $Kind; Choices = $Choices; Note = $Note
    }
}

function Set-SheetHeading {
    param($Sheet, [string]$Title, [string[]]$Lines)

    $Sheet.Cells(1, 1).Value2 = $Title
    $Sheet.Cells(1, 1).Font.Size = 16
    $Sheet.Cells(1, 1).Font.Bold = $true
    $Sheet.Cells(1, 1).Font.Color = $COLOR_HEADING

    $row = 2
    foreach ($line in $Lines) {
        $Sheet.Cells($row, 1).Value2 = $line
        $Sheet.Cells($row, 1).Font.Color = $COLOR_MUTED
        $row++
    }
    return $row + 1
}

function Set-SectionLabel {
    param($Sheet, [int]$Row, [int]$Column, [string]$Text)

    $Sheet.Cells($Row, $Column).Value2 = $Text
    $Sheet.Cells($Row, $Column).Font.Bold = $true
    $Sheet.Cells($Row, $Column).Font.Color = $COLOR_HEADING
}

function New-Table {
    param($Sheet, [int]$Row, [int]$Column, [string]$Name, [string[]]$Headers, [int]$RowCount)

    for ($i = 0; $i -lt $Headers.Count; $i++) {
        $Sheet.Cells($Row, $Column + $i).Value2 = $Headers[$i]
    }
    $range = $Sheet.Range(
        $Sheet.Cells($Row, $Column),
        $Sheet.Cells($Row + $RowCount, $Column + $Headers.Count - 1))

    $table = $Sheet.ListObjects.Add($xlSrcRange, $range, [Type]::Missing, $xlYes)
    $table.Name = $Name
    $table.TableStyle = 'TableStyleLight9'
    $table.ShowAutoFilter = $false
    return $table
}

function Add-ListValidation {
    param($Range, [string[]]$Choices)

    $Range.Validation.Delete()
    $Range.Validation.Add($xlValidateList, $xlValidAlertStop, 1, ($Choices -join ',')) | Out-Null
    $Range.Validation.IgnoreBlank = $true
    $Range.Validation.InCellDropdown = $true
}

function Add-WholeNumberValidation {
    param($Range)

    $Range.Validation.Delete()
    $Range.Validation.Add($xlValidateWholeNumber, $xlValidAlertStop, $xlGreaterEqual, '0') | Out-Null
    $Range.Validation.IgnoreBlank = $true
    $Range.Validation.ErrorTitle = '入力エラー'
    $Range.Validation.ErrorMessage = '0 以上の整数を入力してください。'
}

# パラメータ表。「値」と「内部パラメータ名」の列名で VBA が引くので、列を足しても壊れない。
function New-ParamTable {
    param($Sheet, [int]$Row, [string]$Name, [object[]]$Params, [switch]$WithGroup)

    $headers = @()
    if ($WithGroup) { $headers += '分類' }
    $headers += @('パラメータ', '値', '内部パラメータ名', '備考')

    $table = New-Table -Sheet $Sheet -Row $Row -Column 1 -Name $Name -Headers $headers -RowCount $Params.Count

    $offset = if ($WithGroup) { 1 } else { 0 }
    for ($i = 0; $i -lt $Params.Count; $i++) {
        $p = $Params[$i]
        $r = $Row + 1 + $i
        if ($WithGroup) { $Sheet.Cells($r, 1).Value2 = $p.Group }
        $Sheet.Cells($r, 1 + $offset).Value2 = $p.Label
        $Sheet.Cells($r, 3 + $offset).Value2 = $p.Name
        $Sheet.Cells($r, 4 + $offset).Value2 = $p.Note

        $valueCell = $Sheet.Cells($r, 2 + $offset)
        switch ($p.Kind) {
            'bool' {
                Add-ListValidation -Range $valueCell -Choices @('有効', '無効')
                $valueCell.Value2 = $p.Value
            }
            'choice' {
                Add-ListValidation -Range $valueCell -Choices $p.Choices
                $valueCell.Value2 = $p.Value
            }
            'int' {
                Add-WholeNumberValidation -Range $valueCell
                $valueCell.NumberFormatLocal = '0'
                $valueCell.Value2 = [int]$p.Value
            }
            'text' {
                $valueCell.NumberFormatLocal = '@'
                if ($p.Value) { $valueCell.Value2 = $p.Value }
            }
        }
        $valueCell.HorizontalAlignment = -4131  # xlLeft
    }

    $noteColumn = 4 + $offset
    $Sheet.Columns($noteColumn).WrapText = $false
    return $table
}

$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false
$xl.DisplayAlerts = $false
$xl.ScreenUpdating = $false

try {
    if ($xl.Workbooks.Count -eq 0) { $xl.SheetsInNewWorkbook = 1 }
    $wb = $xl.Workbooks.Add()
    while ($wb.Worksheets.Count -gt 1) { $wb.Worksheets.Item($wb.Worksheets.Count).Delete() }

    $sheetNames = @(
        '本パラメータシートについて',
        'エクスポート',
        '全般 (General)',
        '抑止ルール (Rules)',
        '抑止時間 (Durations)'
    )
    $wb.Worksheets.Item(1).Name = $sheetNames[0]
    for ($i = 1; $i -lt $sheetNames.Count; $i++) {
        $added = $wb.Worksheets.Add([Type]::Missing, $wb.Worksheets.Item($wb.Worksheets.Count))
        $added.Name = $sheetNames[$i]
    }

    # ---------------------------------------------------------------- 説明
    $ws = $wb.Worksheets.Item('本パラメータシートについて')
    $row = Set-SheetHeading -Sheet $ws -Title 'PopGuard パラメータシート' -Lines @(
        '本パラメータシートは、PopGuard の設定項目を一元的に定義・管理するための文書です。',
        '機能ごとにシートを分けてあり、各シートの「値」列に設定内容を記入します。',
        '記入した内容は「エクスポート」シートのボタンから、PopGuard.rules.json として出力できます。'
    )

    Set-SectionLabel -Sheet $ws -Row $row -Column 1 -Text '設定ファイルの配置'
    $row++
    foreach ($line in @(
        '出力した PopGuard.rules.json を、PopGuard.exe と同じフォルダーに置いてください。',
        'PopGuard を起動し直すと、記入した内容が反映されます (設定は起動時に読み込まれます)。',
        'ファイルが無い場合、PopGuard は初回起動時に既定値のサンプルを生成します。')) {
        $ws.Cells($row, 1).Value2 = $line
        $row++
    }
    $row++

    Set-SectionLabel -Sheet $ws -Row $row -Column 1 -Text '記入のきまり'
    $row++
    foreach ($line in @(
        '・「全般」シートは「値」列だけを編集してください。「内部パラメータ名」は JSON のキーに対応し、変更すると出力できなくなります。',
        '・有効・無効や隠し方・表示言語はプルダウンから選びます。数値は 0 以上の整数です。',
        '・表の行が足りない場合は、表の最終行で Tab キーを押すか、表内で行を挿入して増やせます。',
        '・空欄の行は出力時に読み飛ばされるため、消し忘れの空行があっても問題ありません。')) {
        $ws.Cells($row, 1).Value2 = $line
        $row++
    }
    $row++

    Set-SectionLabel -Sheet $ws -Row $row -Column 1 -Text 'シートの一覧'
    $row++
    $toc = @(
        @('シート', '設定する内容'),
        @('全般 (General)', 'フォーカス セッション中の自動抑止と、表示言語'),
        @('抑止ルール (Rules)', '裏へ送る対象のウィンドウ (プロセス・タイトル・クラス) と隠し方'),
        @('抑止時間 (Durations)', 'トレイメニューに出す「抑止する時間」の候補')
    )
    foreach ($entry in $toc) {
        $ws.Cells($row, 1).Value2 = $entry[0]
        $ws.Cells($row, 2).Value2 = $entry[1]
        if ($entry[0] -eq 'シート') {
            $ws.Range($ws.Cells($row, 1), $ws.Cells($row, 2)).Font.Bold = $true
        }
        $row++
    }
    $ws.Columns(1).ColumnWidth = 30
    $ws.Columns(2).ColumnWidth = 80

    # ------------------------------------------------------------ エクスポート
    $ws = $wb.Worksheets.Item('エクスポート')
    $row = Set-SheetHeading -Sheet $ws -Title 'エクスポート' -Lines @(
        '各シートに記入した内容を、PopGuard の設定ファイル (JSON) として書き出します。'
    )

    $button = $ws.Shapes.AddShape($msoShapeRoundedRectangle, 12, 68, 400, 48)
    $button.Name = 'ExportButton'
    $button.Fill.ForeColor.RGB = $COLOR_ACCENT
    $button.Line.Visible = $false
    $button.TextFrame2.TextRange.Text = 'パラメータシートの内容を設定ファイルとして出力する'
    $button.TextFrame2.TextRange.Font.Size = 12
    $button.TextFrame2.TextRange.Font.Bold = $true
    $button.TextFrame2.TextRange.Font.Fill.ForeColor.RGB = $COLOR_WHITE
    $button.TextFrame2.TextRange.ParagraphFormat.Alignment = 2  # msoAlignCenter
    $button.TextFrame2.VerticalAnchor = 3                       # msoAnchorMiddle
    $button.OnAction = 'ExportSetting'

    $row = 10
    foreach ($line in @(
        '出力先: このブックと同じ場所の PopGuard_export\<日時>\PopGuard.rules.json',
        '',
        '実行するたびに日時のフォルダーを作るため、以前に出力したファイルは上書きされません。',
        '出力前に入力内容を確認し、問題があれば内容を表示して中断します。',
        '',
        'マクロが動かない場合は、ブックを開いたときに表示される「コンテンツの有効化」を押してください。',
        'ダウンロードしたファイルでボタンが反応しない場合は、エクスプローラーでブックのプロパティを開き、',
        '「セキュリティ: 許可する」にチェックを入れてから開き直してください。')) {
        $ws.Cells($row, 1).Value2 = $line
        $row++
    }
    $ws.Columns(1).ColumnWidth = 100

    # ---------------------------------------------------------------- 全般
    $ws = $wb.Worksheets.Item('全般 (General)')
    $row = Set-SheetHeading -Sheet $ws -Title '全般 (General)' -Lines @(
        'PopGuard 全体の動作を決めます。'
    )
    New-ParamTable -Sheet $ws -Row $row -Name 'T_General' -Params @(
        (New-Param -Label 'フォーカス セッション中は自動で抑止する' -Value '有効' -Name 'AutoSuppressDuringFocus' -Kind bool `
            -Note 'Windows 11 のフォーカス セッション中だけ自動で抑止します。手動の「応答不可」は対象外です'),
        (New-Param -Label 'マイク使用中は自動で抑止する' -Value '無効' -Name 'AutoSuppressDuringMicrophone' -Kind bool `
            -Note '通話や Web 会議などでマイクが使われている間だけ自動で抑止します（既定は無効）'),
        (New-Param -Label '表示言語' -Value '自動' -Name 'Language' -Kind choice -Choices @('自動', '日本語', '英語') `
            -Note '「自動」は OS の表示言語に従います (日本語以外は英語)')
    ) | Out-Null

    $ws.Columns(1).ColumnWidth = 40
    $ws.Columns(2).ColumnWidth = 14
    $ws.Columns(3).ColumnWidth = 30
    $ws.Columns(4).ColumnWidth = 80

    # ------------------------------------------------------------ 抑止ルール
    $ws = $wb.Worksheets.Item('抑止ルール (Rules)')
    $row = Set-SheetHeading -Sheet $ws -Title '抑止ルール (Rules)' -Lines @(
        '裏へ送る対象のウィンドウを 1 行 1 ルールで記入します。',
        'プロセス名は必須です。タイトル・クラスは空欄なら絞り込みません (ワイルドカード * ? が使えます)。',
        '対象になるのは最前面 (TOPMOST) のウィンドウだけです。モーダルダイアログは対象外です。'
    )

    $rulesTable = New-Table -Sheet $ws -Row $row -Column 1 -Name 'A_Rules_Items' `
        -Headers @('有効', 'プロセス', 'タイトル', 'クラス', '隠し方', 'TOPMOSTのみ', '抑止しない') -RowCount 20
    $rulesTable.DataBodyRange.NumberFormatLocal = '@'
    $rulesTable.DataBodyRange.VerticalAlignment = -4160  # xlTop

    Add-ListValidation -Range $rulesTable.ListColumns('有効').DataBodyRange -Choices @('有効', '無効')
    Add-ListValidation -Range $rulesTable.ListColumns('隠し方').DataBodyRange -Choices @('背面へ送る', '最小化', '非表示')
    Add-ListValidation -Range $rulesTable.ListColumns('TOPMOSTのみ').DataBodyRange -Choices @('有効', '無効')
    Add-ListValidation -Range $rulesTable.ListColumns('抑止しない').DataBodyRange -Choices @('有効', '無効')

    $noteRow = $row + 22
    foreach ($line in @(
        '・「有効」を空欄にすると有効 (true) として扱います。',
        '・「隠し方」: 背面へ送る = Bottom / 最小化 = Minimize / 非表示 = Hide。空欄は「背面へ送る」。',
        '・「抑止しない」を「有効」にすると、その行に一致したウィンドウは常に抑止しません (除外ルール)。',
        '　 ルールは上の行から順に評価されるため、除外ルールは広いルールより上の行に書いてください。',
        '・「TOPMOSTのみ」は現状の実装では常に TOPMOST のみが対象です (将来用の設定)。')) {
        $ws.Cells($noteRow, 1).Value2 = $line
        $ws.Cells($noteRow, 1).Font.Color = $COLOR_MUTED
        $noteRow++
    }

    $ws.Columns(1).ColumnWidth = 10
    $ws.Columns(2).ColumnWidth = 22
    $ws.Columns(3).ColumnWidth = 30
    $ws.Columns(4).ColumnWidth = 24
    $ws.Columns(5).ColumnWidth = 14
    $ws.Columns(6).ColumnWidth = 14
    $ws.Columns(7).ColumnWidth = 12

    # ------------------------------------------------------------ 抑止時間
    $ws = $wb.Worksheets.Item('抑止時間 (Durations)')
    $row = Set-SheetHeading -Sheet $ws -Title '抑止時間 (Durations)' -Lines @(
        'トレイメニューの「ウィンドウを抑止する」に出す、時間の候補を記入します。',
        'ラベルは表示言語に合わせて「日本語」「英語」を切り替えます。片方だけ書くと、その言語だけに使われ、',
        'もう一方は「分」から自動生成します（両方空欄なら両言語とも自動生成）。',
        '「分」は 0 以上の整数で、0 は「無制限」を表します。',
        '1 行も書かなければ、PopGuard は既定の候補 (30分 / 1時間 / 2時間 / 一日 / 無制限) を使います。'
    )

    $durTable = New-Table -Sheet $ws -Row $row -Column 1 -Name 'A_Durations_Items' `
        -Headers @('ラベル (日本語)', 'ラベル (英語)', '分') -RowCount 12
    $durTable.ListColumns('ラベル (日本語)').DataBodyRange.NumberFormatLocal = '@'
    $durTable.ListColumns('ラベル (英語)').DataBodyRange.NumberFormatLocal = '@'
    $durTable.ListColumns('分').DataBodyRange.NumberFormatLocal = '0'
    Add-WholeNumberValidation -Range $durTable.ListColumns('分').DataBodyRange

    $defaults = @(
        @('30分', '30 min', 30),
        @('1時間', '1 hour', 60),
        @('2時間', '2 hours', 120),
        @('一日', '1 day', 1440),
        @('無制限', 'Unlimited', 0)
    )
    for ($i = 0; $i -lt $defaults.Count; $i++) {
        $durTable.DataBodyRange.Cells($i + 1, 1).Value2 = $defaults[$i][0]
        $durTable.DataBodyRange.Cells($i + 1, 2).Value2 = $defaults[$i][1]
        $durTable.DataBodyRange.Cells($i + 1, 3).Value2 = [int]$defaults[$i][2]
    }

    $ws.Columns(1).ColumnWidth = 20
    $ws.Columns(2).ColumnWidth = 20
    $ws.Columns(3).ColumnWidth = 10
    $ws.Columns(4).ColumnWidth = 60

    # ------------------------------------------------------------- 仕上げ
    foreach ($name in $sheetNames) {
        $sheet = $wb.Worksheets.Item($name)
        $sheet.Activate()
        $xl.ActiveWindow.DisplayGridlines = $false
        $sheet.Range('A1').Select() | Out-Null

        $sheet.PageSetup.Orientation = 2  # xlLandscape
        $sheet.PageSetup.Zoom = $false
        $sheet.PageSetup.FitToPagesWide = 1
        $sheet.PageSetup.FitToPagesTall = $false
        $sheet.PageSetup.CenterFooter = "$name  -  &P / &N"
    }

    $vbaCode = Get-Content -Raw -Encoding UTF8 $vbaPath
    $module = $wb.VBProject.VBComponents.Add($vbextCtStdModule)
    $module.Name = 'ExportConfig'
    $module.CodeModule.AddFromString($vbaCode)

    $wb.Worksheets.Item($sheetNames[0]).Activate()

    $outputDir = Split-Path -Parent $OutputPath
    if (-not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }
    if (Test-Path $OutputPath) { Remove-Item $OutputPath -Force }
    $wb.SaveAs($OutputPath, $xlOpenXMLWorkbookMacroEnabled)
    $wb.Close($false)

    Write-Host "作成しました: $OutputPath"
}
finally {
    $xl.ScreenUpdating = $true
    $xl.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) | Out-Null
}
