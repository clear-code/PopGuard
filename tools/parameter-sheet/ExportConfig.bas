' This Source Code Form is subject to the terms of the Mozilla Public
' License, v. 2.0. If a copy of the MPL was not distributed with this
' file, You can obtain one at https://mozilla.org/MPL/2.0/.
'
' Copyright (c) 2026 ClearCode Inc.

Option Explicit

' PopGuard パラメータシート: 設定ファイル (JSON) の出力
'
' 各シートの入力値を読み取り、PopGuard.rules.json と同じ構造の JSON を組み立てて
' 書き出します。値の参照はシート上のテーブル名で行うため、行や列を挿入しても
' このコードを直す必要はありません。
'
'   T_<機能名>            単一値のパラメータ (「内部パラメータ名」列で引く)
'   A_<機能名>_<項目名>   オブジェクトの配列 (列の並びで読む)

Private Const EXPORT_FOLDER_NAME As String = "PopGuard_export"
Private Const OUTPUT_FILE_NAME As String = "PopGuard.rules.json"

Private Const VALUE_YES As String = "有効"

' 「隠し方」の表示ラベルと JSON 値の対応
Private Const HIDE_BOTTOM_LABEL As String = "背面へ送る"
Private Const HIDE_MINIMIZE_LABEL As String = "最小化"
Private Const HIDE_HIDE_LABEL As String = "非表示"

' 「表示言語」の表示ラベルと JSON 値の対応
Private Const LANG_AUTO_LABEL As String = "自動"
Private Const LANG_JA_LABEL As String = "日本語"
Private Const LANG_EN_LABEL As String = "英語"

Private Const COL_PARAM_NAME As String = "内部パラメータ名"
Private Const COL_PARAM_VALUE As String = "値"


' ==========================================
' メインエントリ
' ==========================================
Public Sub ExportSetting()
    Dim problems As Collection
    Dim destFolder As String
    Dim outputPath As String
    Dim jsonText As String

    On Error GoTo ErrHandler

    Set problems = CollectProblems()
    If problems.Count > 0 Then
        MsgBox "入力内容に問題があります。修正してから再実行してください。" & vbCrLf & vbCrLf & _
               JoinCollection(problems, vbCrLf), vbExclamation, "入力エラー"
        Exit Sub
    End If

    If MsgBox(ThisWorkbook.Path & "\" & EXPORT_FOLDER_NAME & vbCrLf & _
              "に現在の設定を " & OUTPUT_FILE_NAME & " として出力します。よろしいですか？", _
              vbOKCancel + vbQuestion, "確認") <> vbOK Then Exit Sub

    jsonText = BuildJson()

    destFolder = GetOutputFolder()
    outputPath = destFolder & "\" & OUTPUT_FILE_NAME
    SaveTextFileUtf8 jsonText, outputPath

    MsgBox "出力しました。" & vbCrLf & vbCrLf & outputPath, vbInformation, "完了"
    Exit Sub

ErrHandler:
    MsgBox "出力に失敗しました。" & vbCrLf & vbCrLf & Err.Description, vbExclamation, "エラー"
End Sub


' ==========================================
' JSON の組み立て
' ==========================================
' Public にしてあるのは、Excel を開かずに出力内容を検証できるようにするため
' (tools\parameter-sheet\test-parameter-sheet.ps1 が Application.Run で呼びます)。
Public Function BuildJson() As String
    Dim s As String

    s = "{" & vbCrLf
    s = s & BuildRules() & "," & vbCrLf
    s = s & BuildDurations() & "," & vbCrLf
    s = s & "  ""autoSuppressDuringFocus"": " & JBool(ParamBool("T_General", "AutoSuppressDuringFocus")) & "," & vbCrLf
    s = s & "  ""language"": " & JStr(LanguageValue(ParamText("T_General", "Language"))) & vbCrLf
    s = s & "}" & vbCrLf

    BuildJson = s
End Function

Private Function BuildRules() As String
    Dim lo As ListObject
    Dim targetRows As Collection
    Dim r As Variant
    Dim body As String
    Dim isFirst As Boolean

    Set lo = GetTable("A_Rules_Items")
    Set targetRows = NonEmptyRows(lo, 2) ' プロセス列が埋まっている行だけを対象にする

    If targetRows.Count = 0 Then
        BuildRules = "  ""rules"": []"
        Exit Function
    End If

    isFirst = True
    For Each r In targetRows
        If Not isFirst Then body = body & "," & vbCrLf
        isFirst = False
        body = body & "    {" & vbCrLf
        body = body & "      ""enabled"": " & JBool(CellBoolDefault(lo, CLng(r), 1, True)) & "," & vbCrLf
        body = body & "      ""process"": " & JStr(CellText(lo, CLng(r), 2)) & "," & vbCrLf
        body = body & "      ""title"": " & JStr(CellText(lo, CLng(r), 3)) & "," & vbCrLf
        body = body & "      ""class"": " & JStr(CellText(lo, CLng(r), 4)) & "," & vbCrLf
        body = body & "      ""hide"": " & JStr(HideValue(CellText(lo, CLng(r), 5))) & "," & vbCrLf
        body = body & "      ""topMostOnly"": " & JBool(CellBoolDefault(lo, CLng(r), 6, True)) & vbCrLf
        body = body & "    }"
    Next r

    BuildRules = "  ""rules"": [" & vbCrLf & body & vbCrLf & "  ]"
End Function

Private Function BuildDurations() As String
    Dim lo As ListObject
    Dim targetRows As Collection
    Dim r As Variant
    Dim body As String
    Dim isFirst As Boolean

    Set lo = GetTable("A_Durations_Items")
    Set targetRows = NonEmptyRows(lo, 0) ' どこかが埋まっている行を対象にする

    If targetRows.Count = 0 Then
        BuildDurations = "  ""durations"": []"
        Exit Function
    End If

    isFirst = True
    For Each r In targetRows
        If Not isFirst Then body = body & "," & vbCrLf
        isFirst = False
        body = body & "    { ""label"": " & JStr(CellText(lo, CLng(r), 1)) & _
               ", ""minutes"": " & JNum(CellLong(lo, CLng(r), 2)) & " }"
    Next r

    BuildDurations = "  ""durations"": [" & vbCrLf & body & vbCrLf & "  ]"
End Function


' ==========================================
' 入力チェック
' ==========================================
' BuildJson と同じ理由で Public。問題がなければ空文字を返します。
Public Function ValidationMessage() As String
    ValidationMessage = JoinCollection(CollectProblems(), vbLf)
End Function

Private Function CollectProblems() As Collection
    Dim problems As New Collection
    Dim lo As ListObject
    Dim targetRows As Collection
    Dim r As Variant
    Dim minutesValue As Variant

    If LanguageValue(ParamText("T_General", "Language")) = "" Then
        problems.Add "・全般: 表示言語は「" & LANG_AUTO_LABEL & "」「" & LANG_JA_LABEL & _
                     "」「" & LANG_EN_LABEL & "」から選んでください。"
    End If

    ' 抑止ルール
    Set lo = GetTable("A_Rules_Items")
    Set targetRows = NonEmptyRows(lo, 0)
    For Each r In targetRows
        If CellText(lo, CLng(r), 2) = "" Then
            problems.Add "・抑止ルール: " & r & " 行目にプロセス名がありません。"
        End If
        If HideValue(CellText(lo, CLng(r), 5)) = "" Then
            problems.Add "・抑止ルール: " & r & " 行目の「隠し方」は「" & HIDE_BOTTOM_LABEL & "」「" & _
                         HIDE_MINIMIZE_LABEL & "」「" & HIDE_HIDE_LABEL & "」から選んでください。"
        End If
    Next r

    ' 抑止時間の候補
    Set lo = GetTable("A_Durations_Items")
    Set targetRows = NonEmptyRows(lo, 0)
    For Each r In targetRows
        If CellText(lo, CLng(r), 1) = "" Then
            problems.Add "・抑止時間の候補: " & r & " 行目にラベルがありません。"
        End If

        minutesValue = lo.DataBodyRange.Cells(CLng(r), 2).Value
        If Trim$(CStr(minutesValue & "")) = "" Then
            problems.Add "・抑止時間の候補: " & r & " 行目の「分」が空欄です。0 以上の整数を入力してください。"
        ElseIf Not IsNumeric(minutesValue) Then
            problems.Add "・抑止時間の候補: " & r & " 行目の「分」には数値を入力してください。"
        ElseIf CDbl(minutesValue) < 0 Or CDbl(minutesValue) <> Int(CDbl(minutesValue)) Then
            problems.Add "・抑止時間の候補: " & r & " 行目の「分」には 0 以上の整数を入力してください。"
        End If
    Next r

    Set CollectProblems = problems
End Function


' ==========================================
' シートの読み取り
' ==========================================
Private Function GetTable(ByVal tableName As String) As ListObject
    Dim ws As Worksheet
    Dim lo As ListObject

    For Each ws In ThisWorkbook.Worksheets
        For Each lo In ws.ListObjects
            If StrComp(lo.Name, tableName, vbTextCompare) = 0 Then
                Set GetTable = lo
                Exit Function
            End If
        Next lo
    Next ws

    Err.Raise vbObjectError + 513, , "テーブルが見つかりません: " & tableName
End Function

Private Function ParamRaw(ByVal tableName As String, ByVal paramName As String) As Variant
    Dim lo As ListObject
    Dim nameCol As Long
    Dim valueCol As Long
    Dim i As Long

    Set lo = GetTable(tableName)
    If lo.DataBodyRange Is Nothing Then
        Err.Raise vbObjectError + 514, , tableName & " が空です。"
    End If

    nameCol = lo.ListColumns(COL_PARAM_NAME).Index
    valueCol = lo.ListColumns(COL_PARAM_VALUE).Index

    For i = 1 To lo.DataBodyRange.Rows.Count
        If Trim$(CStr(lo.DataBodyRange.Cells(i, nameCol).Value & "")) = paramName Then
            ParamRaw = lo.DataBodyRange.Cells(i, valueCol).Value
            Exit Function
        End If
    Next i

    Err.Raise vbObjectError + 515, , tableName & " に " & paramName & " の行がありません。"
End Function

Private Function ParamText(ByVal tableName As String, ByVal paramName As String) As String
    ParamText = Trim$(CStr(ParamRaw(tableName, paramName) & ""))
End Function

Private Function ParamBool(ByVal tableName As String, ByVal paramName As String) As Boolean
    ParamBool = (ParamText(tableName, paramName) = VALUE_YES)
End Function

' どこか 1 つでも埋まっている行の番号。keyCol が 0 なら全列を見る。
Private Function NonEmptyRows(ByVal lo As ListObject, ByVal keyCol As Long) As Collection
    Dim result As New Collection
    Dim i As Long
    Dim j As Long
    Dim used As Boolean

    If lo.DataBodyRange Is Nothing Then
        Set NonEmptyRows = result
        Exit Function
    End If

    For i = 1 To lo.DataBodyRange.Rows.Count
        If keyCol > 0 Then
            used = (Trim$(CStr(lo.DataBodyRange.Cells(i, keyCol).Value & "")) <> "")
        Else
            used = False
            For j = 1 To lo.ListColumns.Count
                If Trim$(CStr(lo.DataBodyRange.Cells(i, j).Value & "")) <> "" Then used = True
            Next j
        End If
        If used Then result.Add i
    Next i

    Set NonEmptyRows = result
End Function

Private Function CellText(ByVal lo As ListObject, ByVal rowIndex As Long, ByVal colIndex As Long) As String
    CellText = Trim$(CStr(lo.DataBodyRange.Cells(rowIndex, colIndex).Value & ""))
End Function

Private Function CellLong(ByVal lo As ListObject, ByVal rowIndex As Long, ByVal colIndex As Long) As Long
    Dim v As Variant

    v = lo.DataBodyRange.Cells(rowIndex, colIndex).Value
    If Trim$(CStr(v & "")) = "" Then Exit Function
    CellLong = CLng(v)
End Function

' 空欄のときは default を返す bool セル。「有効」で真、それ以外（「無効」等）で偽。
Private Function CellBoolDefault(ByVal lo As ListObject, ByVal rowIndex As Long, _
                                 ByVal colIndex As Long, ByVal defaultValue As Boolean) As Boolean
    Dim t As String

    t = CellText(lo, rowIndex, colIndex)
    If t = "" Then
        CellBoolDefault = defaultValue
    Else
        CellBoolDefault = (t = VALUE_YES)
    End If
End Function

' 「隠し方」ラベル -> JSON 値。空欄は既定の Bottom。未知のラベルは空文字 (入力チェックで弾く)。
Private Function HideValue(ByVal label As String) As String
    Select Case label
        Case "": HideValue = "Bottom"
        Case HIDE_BOTTOM_LABEL, "Bottom": HideValue = "Bottom"
        Case HIDE_MINIMIZE_LABEL, "Minimize": HideValue = "Minimize"
        Case HIDE_HIDE_LABEL, "Hide": HideValue = "Hide"
    End Select
End Function

' 「表示言語」ラベル -> JSON 値。空欄は auto。未知のラベルは空文字 (入力チェックで弾く)。
Private Function LanguageValue(ByVal label As String) As String
    Select Case label
        Case "": LanguageValue = "auto"
        Case LANG_AUTO_LABEL, "auto": LanguageValue = "auto"
        Case LANG_JA_LABEL, "ja": LanguageValue = "ja"
        Case LANG_EN_LABEL, "en": LanguageValue = "en"
    End Select
End Function

Private Function JoinCollection(ByVal items As Collection, ByVal delimiter As String) As String
    Dim item As Variant
    Dim s As String

    For Each item In items
        If s <> "" Then s = s & delimiter
        s = s & CStr(item)
    Next item

    JoinCollection = s
End Function


' ==========================================
' JSON の値
' ==========================================
Private Function JBool(ByVal value As Boolean) As String
    JBool = IIf(value, "true", "false")
End Function

Private Function JNum(ByVal value As Long) As String
    JNum = CStr(value)
End Function

Private Function JStr(ByVal value As String) As String
    Dim i As Long
    Dim ch As String
    Dim code As Long
    Dim out As String

    For i = 1 To Len(value)
        ch = Mid$(value, i, 1)
        Select Case ch
            Case """": out = out & "\"""
            Case "\": out = out & "\\"
            Case vbLf: out = out & "\n"
            Case vbCr: out = out & "\r"
            Case vbTab: out = out & "\t"
            Case Else
                ' AscW は &H8000 以降を負で返すため、制御文字と混ざらないよう補正する。
                code = AscW(ch)
                If code < 0 Then code = code + 65536
                If code < 32 Or code = &H7F Then
                    out = out & "\u" & Right$("000" & Hex$(code), 4)
                Else
                    out = out & ch
                End If
        End Select
    Next i

    JStr = """" & out & """"
End Function


' ==========================================
' ファイル出力
' ==========================================
Private Function GetOutputFolder() As String
    Dim basePath As String
    Dim stampedPath As String

    If ThisWorkbook.Path = "" Then
        Err.Raise vbObjectError + 516, , "先にこのブックを保存してください。"
    End If

    basePath = ThisWorkbook.Path & "\" & EXPORT_FOLDER_NAME
    EnsureFolder basePath

    stampedPath = basePath & "\" & Format$(Now(), "yyyymmdd_hhnnss")
    EnsureFolder stampedPath

    GetOutputFolder = stampedPath
End Function

Private Sub EnsureFolder(ByVal folderPath As String)
    If Dir(folderPath, vbDirectory) = "" Then MkDir folderPath
End Sub

' ADODB.Stream の UTF-8 は BOM を付けるため、バイナリに移して 3 バイト落とす。
Private Sub SaveTextFileUtf8(ByVal content As String, ByVal filePath As String)
    Dim textStream As Object
    Dim binaryStream As Object

    Set textStream = CreateObject("ADODB.Stream")
    textStream.Type = 2
    textStream.Charset = "UTF-8"
    textStream.Open
    textStream.WriteText content
    textStream.Position = 0
    textStream.Type = 1
    textStream.Position = 3

    Set binaryStream = CreateObject("ADODB.Stream")
    binaryStream.Type = 1
    binaryStream.Open
    textStream.CopyTo binaryStream
    binaryStream.SaveToFile filePath, 2
    binaryStream.Close
    textStream.Close
End Sub
