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
* 抑止は、トレイメニューからの手動（期限付き）と、Windows 11 のフォーカス セッション連動で有効化されます。
* 裏へ送ったウィンドウは、抑止の終了時・アプリ終了時・異常時に元へ戻します。
* 単体の実行ファイル（`PopGuard.exe`）で完結します。

■ PopGuardが扱えるのは「アプリが独自に表示するウィンドウ」です。
Windows 標準のトースト通知（通知プラットフォームが描画するもの）は制御できません。
Windows 標準のトースト通知を制御したい場合は、Windowsのフォーカス機能を使用してください。
また、Windowsのフォーカス機能と連動して、PopGuardを有効化することも可能です。

\newpage

# 動作の仕組み

## 判定と処理

検知したウィンドウについて、次の順で判定します。

1. 抑止が有効（手動またはフォーカス連動）であること。
2. 最前面（TOPMOST）かつ可視であること。
3. 設定のルール（プロセス名・タイトル・クラス）に一致すること。
4. モーダルダイアログでないこと（後述の「モーダル回避」）。

一致したウィンドウは、ルールの `hide` に従って処理します。

| `hide` | 処理 |
| --- | --- |
| `Bottom` | 最前面属性を外し、Z オーダーの最背面へ送る（既定） |
| `Minimize` | 最小化する |
| `Hide` | 非表示にする |

## モーダル回避

モーダルダイアログを裏へ送るとアプリの操作が進められなくなるため、次のいずれかに該当するウィンドウは
対象から除外します（企業向けに安全側へ倒しています）。

* オーナーウィンドウが無効化されている（モーダル実行中の典型的な状態）
* 拡張スタイルに `WS_EX_DLGMODALFRAME` を持つ（モーダルフレーム）
* ウィンドウクラスが `#32770`（標準の Win32 ダイアログボックス）

## 復元

PopGuardは裏へ送ったウィンドウを追跡し、次のタイミングで元へ戻します。

* 抑止の期限切れ、または「抑止を停止」
* PopGuardの終了
* 未処理例外などの異常時（`RestoreAll` を保証）

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

■ 一般ユーザー権限で動作するため、**管理者権限（昇格）で動くアプリのウィンドウや、他セッションの
ウィンドウは操作できません**（UIPI／セッション分離による制約）。

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
    { "enabled": true, "process": "SomeNotifier", "title": "*通知*",
      "class": "", "hide": "Bottom", "topMostOnly": true }
  ],
  "durations": [
    { "label": "30分", "minutes": 30 },
    { "label": "1時間", "minutes": 60 },
    { "label": "2時間", "minutes": 120 },
    { "label": "一日", "minutes": 1440 },
    { "label": "無制限", "minutes": 0 }
  ],
  "autoSuppressDuringFocus": true,
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
| `topMostOnly` | 真偽 | **現状未実装**。常に TOPMOST のみが対象（将来用の設定） |

* ワイルドカードは `*`（任意の 0 文字以上）と `?`（任意の 1 文字）が使用可能です。
* `process` が空、または `*` / `?` だけのルールは「何にでも一致してしまう」ため無効として読み飛ばされます。
* 複数のルールは上から順に評価し、最初に一致したものを適用します。

## durations（抑止時間の候補）

トレイメニューの「ウィンドウを抑止する」に表示する時間の候補です。

| キー | 型 | 説明 |
| --- | --- | --- |
| `label` | 文字列 | メニューに表示するラベル |
| `minutes` | 整数 | 分。**0 以下は「無制限」** |

`durations` を省略、または空にした場合は、既定の候補（30分 / 1時間 / 2時間 / 一日 / 無制限）が使われます。

## autoSuppressDuringFocus

`true`（既定）で、Windows 11 のフォーカス セッション中に自動で抑止します。
フォーカス終了で自動的に解除します。PopGuard側で手動で抑止した場合、そちらが優先されます。（手動で開始した抑止は、
フォーカス終了では解除されません）。

■　**手動の「応答不可（DND）」単体では連動しません**（この挙動は Windows の API 仕様によるものです）。

## language（表示言語）

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
%APPDATA%\PopGuard\PopGuard.log
```

* 1 行 1 レコードのテキスト（先頭に `yyyy-MM-dd HH:mm:ss` のタイムスタンプ）。
* サイズが **10 MB** を超えると世代ローテーション（`PopGuard_1.log`〜`PopGuard_10.log`、最大 10 世代）。
* 同一マシンの複数インスタンスに備え、名前付き Mutex で書き込みを直列化します。

## 主なログ行

| 行 | 意味 |
| --- | --- |
| `rules: enabled=… invalid=… durations=… focusSync=… …` | 設定の読み込み結果 |
| `win-event: hook installed …` | イベントフックの登録結果 |
| `focus-session: IsFocusActive=…` | フォーカス セッションの状態変化 |
| `guard: suppression started/ended …` | 抑止の開始・終了（手動／フォーカス／時間満了） |
| `guard demote: process=… title=… method=…` | ウィンドウを裏へ送った |
| `guard restore: process=… title=…` | ウィンドウを元へ戻した |
| `guard skip: … (likely dialog: …)` | モーダル回避で対象外にした |
| `window toplevel: process=… title=…` | 新しく出現したトップレベルウィンドウ（検知の記録） |

\newpage

# 制限事項・留意点

* 抑止できるのは**最前面（TOPMOST）のトップレベルウィンドウ**のみです。
  Windows 標準のトースト通知は制御できません（通知プラットフォーム側で描画されるため）。
* PopGuardは一般ユーザー権限で動作します。**管理者権限のアプリや他セッションのウィンドウは対象外**です。
* モーダルダイアログは安全のため対象外です（前述のモーダル回避）。
* 「表示してから最前面にする」という順序で表示されるポップアップウィンドウの場合、裏へ送られるまでに最大で 700 ミリ秒程度
  （次のポーリングまで）の遅延が生じ、その間ウィンドウが見えることがあります。
* `topMostOnly` は設定として受け付けますが、現状の実装では常に TOPMOST のみを対象にします。
* 手動の「応答不可（DND）」への連動は、Windows の API で確実に取得できないためサポートしていません。

\newpage

# トラブルシューティング

## 対象のウィンドウが裏へ送られない

* 抑止が有効になっているか（トレイアイコンが緑か）を確認します。
* 対象ウィンドウが**最前面**かどうかを確認します（最前面でなければ対象外）。
* ルールの `process` / `title` / `class` が一致しているか確認します。
  `window toplevel:` のログに、検知されたウィンドウの `process` と `title` が出ます。
* モーダルダイアログ（`guard skip:` が出る）や管理者権限のアプリでないか確認します。

## 設定が反映されない

* `PopGuard.rules.json` が `PopGuard.exe` と**同じフォルダー**にあるか確認します。
* 変更後に PopGuard を**再起動**したか確認します（設定は起動時のみ読み込み）。
* ログの `rules: …` 行で、読み込んだルール数やエラーを確認します。

## フォーカス連動が効かない

* OS が **Windows 11 22H2（22621）以降**か確認します（それ未満は連動不可）。
* ログに `focus-session: not supported …` が出ていないか確認します。
* 連動対象は**フォーカス セッション**です。手動の「応答不可」では発火しません。
