---
CJKmainfont: Noto Sans CJK JP
CJKoptions:
  - BoldFont=Noto Sans CJK JP Bold
title:     PopGuard \newline 管理者ガイド v1.0
titlepage-logo: ./media/titlepage-logo.png
author:    株式会社クリアコード
date:      2026-10
titlepage: true
colorlinks: true
toc-title: 目次
toc-own-page: true
code-block-font-size: \footnotesize
listings-disable-line-numbers: true
footnotes-pretty: true
titlepage-rule-color: "AA0000"
titlepage-rule-height: 2
---


# 概要

PopGuardは、作業中に割り込んでくる最前面のポップアップウィンドウを、一時的に
裏側へ送って（または最小化・非表示にして）邪魔にならないようにする、Windows 向けの常駐アプリケーションです。

* タスクトレイに常駐し、各ユーザーのセッション内で一般ユーザー権限のプロセスとして動作します。
* 抑止の対象や方法は、設定ファイル `PopGuard.rules.json`（JSON）で定義します。
* 抑止は、トレイメニューからの手動（期限付き）のほか、Windows 11 のフォーカス セッション連動や
  マイク使用中（通話・Web 会議など）の連動でも自動的に有効化できます。
* 裏へ送ったウィンドウは、抑止の終了時・アプリ終了時・異常時に元へ戻します。
* 単体の実行ファイル（`PopGuard.exe`）で完結します。

■ PopGuardが扱えるのは「アプリが独自に表示するウィンドウ」です。
Windows 標準のトースト通知（通知プラットフォームが描画するもの）は制御できません。
Windows 標準のトースト通知を制御したい場合は、Windowsのフォーカス機能を使用してください。
また、Windowsのフォーカス機能と連動して、PopGuardを有効化することも可能です。

\newpage

# 動作要件

* OS: Windows 10 バージョン 1809 以降、または Windows 11。
  * フォーカス セッション連動は Windows 11 バージョン 22H2（ビルド 22621）以降でのみ動作します。
    それ未満の環境では連動は自動的に無効になり、その他の機能は通常どおり動作します。
* アーキテクチャ: x64

\newpage

# インストール

配布用インストーラ（`PopGuardSetup-<バージョン>.exe`）を管理者権限で実行します。

* 既定のインストール先は `C:\Program Files\PopGuard` です。
* インストーラは次の処理を行います。
  * `PopGuard.exe` と関連ファイルを配置。
  * `HKLM\Software\PopGuard` に設置情報（Path / Version / ConfigFile）を記録。
  * 「スタートアップ」タスク（既定で有効）を選んだ場合、`HKLM\...\Run` に自動起動エントリを登録。

## 自動起動とプロセスの動作

自動起動を有効にすると、`HKLM\Software\Microsoft\Windows\CurrentVersion\Run` に登録され、
各ユーザーのサインイン時に、そのユーザーのセッション内・一般ユーザー権限のプロセスとして起動します。
ユーザーのデスクトップのトレイアイコンにPopGuardが追加されます。

■ 一般ユーザー権限で動作するため、**管理者権限で動くアプリのウィンドウや、他のユーザーの
ウィンドウは操作できません**（Windows の権限・セッションの制約）。

## アンインストール

アンインストールすると、配置したファイル、`HKLM\...\Run` の自動起動エントリ、`HKLM\Software\PopGuard` が
削除されます。実行中のPopGuardは停止されます。永続的なシステム設定の変更は行っていないため、
追加の後始末は不要です（裏へ送ったウィンドウはPopGuard終了時に復元済み）。

\newpage

# 設定ファイル

設定は、`PopGuard.exe` と同じフォルダーの **`PopGuard.rules.json`**（UTF-8）で定義します。
PopGuardは**起動時に一度だけ**読み込みます。変更後はPopGuardを再起動してください。

ファイルが存在しない場合、PopGuardは初回起動時に既定値のサンプルを生成しようとします
（ただしインストール先が `Program Files` の場合、書き込み権限がないため生成されません。
その場合は後述のパラメータシートで作成したファイルを配置してください）。

## 全体構造

```json
{
  "rules": [
    { "enabled": true, "process": "SomeNotifier", "title": "*重要*", "exclude": true },
    { "enabled": true, "process": "SomeNotifier", "title": "*通知*",
      "class": "", "hide": "Bottom", "topMostOnly": true }
  ],
  "durations": [
    { "label": { "ja": "30分", "en": "30 min" }, "minutes": 30 },
    { "label": { "ja": "1時間", "en": "1 hour" }, "minutes": 60 },
    { "label": { "ja": "2時間", "en": "2 hours" }, "minutes": 120 },
    { "label": { "ja": "一日", "en": "1 day" }, "minutes": 1440 },
    { "label": { "ja": "無制限", "en": "Unlimited" }, "minutes": 0 }
  ],
  "autoSuppressDuringFocus": true,
  "autoSuppressDuringMicrophone": false,
  "excludeSystemWindows": true,
  "language": "auto"
}
```

キー名は大文字・小文字を区別しません。

## rules（抑止ルール）

対象のポップアップウィンドウを 1 要素 1 ルールで指定します。

| キー | 型 | 説明 |
| --- | --- | --- |
| `enabled` | 真偽 | ルールの有効・無効（省略時は有効） |
| `process` | 文字列 | **必須**。プロセス名のワイルドカード（拡張子なし） |
| `title` | 文字列 | ウィンドウタイトルのワイルドカード。空なら絞らない |
| `class` | 文字列 | ウィンドウクラス名のワイルドカード。空なら絞らない |
| `hide` | 文字列 | `Bottom` / `Minimize` / `Hide`（省略時は `Bottom`） |
| `exclude` | 真偽 | `true` で「**どんなときも抑止しない**」除外ルール（省略時は通常の抑止ルール） |
| `topMostOnly` | 真偽 | **現状未実装**。常に TOPMOST のみが対象（将来用の設定） |

* ワイルドカードは `*`（任意の 0 文字以上）と `?`（任意の 1 文字）が使用可能です。
* `process` が空のルールは設定ミスとみなし無効として読み飛ばされます。
  `*`（ワイルドカードのみ）は「全プロセスに一致」を意味し、有効です。
* 複数のルールは上から順に評価し、**最初に一致したもの**を適用します。
* `exclude: true` のルールに一致したウィンドウは抑止しません。**除外ルールは広いルールより上に置いてください**。
  例: 先頭で特定タイトルを `exclude`、その下で `process: "*"` にして「それ以外はすべて抑止」。
* `hide` の値の意味: `Bottom`＝背面へ送る（既定）、`Minimize`＝最小化、`Hide`＝非表示（`exclude` のときは無視）。
* 対象になるのは最前面のウィンドウだけです。モーダルダイアログ（応答するまで操作をブロックするもの）は、
  安全のため対象外です。
* タスクバーやデスクトップなど **Windows シェルのウィンドウは既定で対象外**です。`process` に `*` を指定しても
  これらは抑止されません（タスクバーが消えることはありません）。この挙動は `excludeSystemWindows` で切り替えできます（下記）。

## durations（抑止時間の候補）

トレイメニューの「ウィンドウを抑止する」に表示する時間の候補です。

| キー | 型 | 説明 |
| --- | --- | --- |
| `label` | 文字列 または オブジェクト | メニューに表示するラベル（下記参照） |
| `minutes` | 整数 | 分。**0 以下は「無制限」** |

`label` は次の 2 通りで指定できます。

* **文字列**：全言語共通のラベル（例: `"label": "90分"`）。
* **言語別オブジェクト**：表示言語ごとのラベル（例: `"label": { "ja": "2時間", "en": "2 hours" }`）。

ラベルは**表示言語（`language`）に連動**します。該当する言語のラベルがあればそれを使い、無ければ `minutes` から
自動生成します（自動生成も言語連動）。**片方の言語だけ指定した場合、もう一方はその言語に流用せず自動生成します。**
`label` 自体を省略した場合も、両言語とも `minutes` から自動生成します。

`durations` を省略、または空にした場合は、既定の候補（30分 / 1時間 / 2時間 / 一日 / 無制限）が使われます
（既定の候補も表示言語に連動します）。

## autoSuppressDuringFocus

`true`（既定）で、Windows 11 のフォーカス セッション中に自動で抑止します。
フォーカス終了で自動的に解除します。PopGuard側で手動で抑止した場合、そちらが優先されます。（手動で開始した抑止は、
フォーカス終了では解除されません）。

■　**手動の「応答不可（DND）」単体では連動しません**（この挙動は Windows の API 仕様によるものです）。

## autoSuppressDuringMicrophone

`true` で、マイクが使用中の間（通話・Web 会議など）に自動で抑止します（**既定は `false`**）。
マイクの使用が終わると自動的に解除します。PopGuard 側で手動抑止している場合は、そちらが優先されます
（手動で開始した抑止は、マイク使用の終了では解除されません）。

* 判定は Windows が記録している**アプリごとのマイク使用状況**（ConsentStore の使用開始／終了時刻）を
  数秒間隔で読み取って行います。レジストリや設定を**変更するものではなく、参照のみ**です。
* デスクトップアプリ（Teams, Zoom など）と Store アプリの両方が対象です。
* フォーカス セッション連動とマイク連動の両方が有効な場合、**どちらかが有効な間は抑止が継続**し、
  両方が解除されたときに自動で解除されます。

## excludeSystemWindows

`true`（既定）で、タスクバーやデスクトップなど **Windows シェルのウィンドウを常に抑止対象外**にします。
これにより、`process` に `*` を指定した場合でもタスクバーが消えることはありません。

* 対象クラス：`Shell_TrayWnd` / `Shell_SecondaryTrayWnd`（タスクバー）、`Progman` / `WorkerW`（デスクトップ）、
  `NotifyIconOverflowWindow`（通知領域のあふれ）。
* `false` にすると、この自動除外を無効化します。**通常は既定の `true` のままにしてください**
  （`false` にすると広いルールがタスクバー等を巻き込む恐れがあります）。

トレイメニューや状態表示の言語です。

| 値 | 説明 |
| --- | --- |
| `auto` | OS の表示言語に従う（既定。日本語以外は英語） |
| `ja` | 日本語 |
| `en` | 英語 |

\newpage

# パラメータシート（設定作成ツール）

設定ファイルを手で書く代わりに、Excel の**パラメータシート**から生成できます。

同梱の parameter-sheet.xlsm がパラメータシートです。

## 使い方

1. `parameter-sheet.xlsm` を開き、各シート（全般 / 抑止ルール / 抑止時間）に設定を記入します。
2. 「エクスポート」シートのボタンから `PopGuard.rules.json` を出力します
   （出力先: ブックと同じ場所の `PopGuard_export\<日時>\`）。
3. 出力した `PopGuard.rules.json` を、`PopGuard.exe` と同じフォルダー（例: `C:\Program Files\PopGuard`）へ配置します。
4. PopGuardを再起動して反映します。

\newpage

# ログ

PopGuardは動作記録を次のファイルに出力します（ユーザーごと）。

```
%LOCALAPPDATA%\PopGuard\PopGuard.log
```

* 1 行 1 レコードのテキスト（先頭に `yyyy-MM-dd HH:mm:ss` のタイムスタンプ）。
* サイズが **10 MB** を超えると世代ローテーション（`PopGuard_1.log`〜`PopGuard_10.log`、最大 10 世代）。

## 主なログ行

| 行 | 意味 |
| --- | --- |
| `rules: …` | 設定の読み込み結果（有効ルール数やエラー） |
| `focus-session: …` | フォーカス セッションの状態変化 |
| `mic-watch: …` | マイク使用状況の変化（使用開始／終了） |
| `guardEngine: suppression started/ended …` | 抑止の開始・終了（手動／フォーカス／マイク／時間満了） |
| `guardEngine: demote: process=… title=… …` | ウィンドウを裏へ送った |
| `guardEngine: restore: process=… title=…` | ウィンドウを元へ戻した |
| `guardEngine: skip: … (likely dialog: …)` | ダイアログとみなして対象外にした |
| `guardEngine: skip: … (excluded by rule)` | 除外ルール（`exclude`）に一致して対象外にした |
| `guardEngine: skip: … (system shell window)` | タスクバー等のシェルウィンドウとして対象外にした |
| `window toplevel: process=… title=…` | 新しく出現したウィンドウ（ルール作成時の確認に利用可） |

\newpage

# 制限事項・留意点

* 抑止できるのは**最前面のウィンドウ**のみです。
  Windows 標準のトースト通知は制御できません。
* PopGuardは一般ユーザー権限で動作します。**管理者権限のアプリや他のユーザーのウィンドウは対象外**です。
* モーダルダイアログは安全のため対象外です。
* 「表示してから最前面にする」という順序で表示されるポップアップウィンドウの場合、
  裏へ送られるまでのごく短い間、ウィンドウが見えることがあります。
* 手動の「応答不可（DND）」への連動はサポートしていません（自動連動の対象はフォーカス セッションとマイク使用です）。

\newpage

# トラブルシューティング

## 対象のウィンドウが裏へ送られない

* 抑止が有効になっているか（トレイアイコンが緑か）を確認します。
* 対象ウィンドウが**最前面**かどうかを確認します（最前面でなければ対象外）。
* ルールの `process` / `title` / `class` が一致しているか確認します。
  `window toplevel:` のログに、検知されたウィンドウの `process` と `title` が出ます。
* モーダルダイアログ（`guardEngine: skip:` が出る）や管理者権限のアプリでないか確認します。

## 設定が反映されない

* `PopGuard.rules.json` が `PopGuard.exe` と**同じフォルダー**にあるか確認します。
* 変更後に PopGuard を**再起動**したか確認します（設定は起動時のみ読み込み）。
* ログの `rules: …` 行で、読み込んだルール数やエラーを確認します。

## フォーカス連動が効かない

* OS が **Windows 11 22H2（22621）以降**か確認します（それ未満は連動不可）。
* ログに、フォーカス セッションが未対応である旨の記録が出ていないか確認します。
* 連動対象は**フォーカス セッション**です。手動の「応答不可」では発火しません。

## マイク連動が効かない

* `autoSuppressDuringMicrophone` が `true` か確認します（ログの `mic-watch: disabled …` が出ていれば無効）。
* 検知は数秒間隔のポーリングのため、マイク開始・終了の反映に数秒かかります。
* ログの `mic-watch: inUse=True/False` で、マイク使用状況を検知できているか確認します。
