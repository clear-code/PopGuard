# PopGuard

PopGuard は、作業中に割り込んでくる**最前面（TOPMOST）のポップアップウィンドウ**を、一時的に裏へ送る
（または最小化・非表示にする）Windows 向けのタスクトレイ常駐アプリです。

運用・設定の手順は `docs/sources/` の管理者ガイド／ユーザーガイドを参照してください。

---

## 概要

- 単体の実行ファイル `PopGuard.exe`（.NET / WinForms）。ブラウザー拡張やサービスは使いません。
- 各ユーザーのセッション内で**一般ユーザー権限のプロセス**として常駐します（サービスではないため、ユーザーのデスクトップのウィンドウ操作とトレイ表示が可能）。
- 抑止対象・方法は設定ファイル `PopGuard.rules.json` で定義します。
- 抑止は「トレイメニューからの手動（期限付き）」と「Windows 11 のフォーカス セッション連動」で有効化されます。
- 裏へ送ったウィンドウは、抑止終了時・アプリ終了時・異常時に**必ず元へ戻します**。

---

## アーキテクチャ

### 検知（2 系統）

| 経路 | 実装 | 役割 |
| --- | --- | --- |
| イベント（即時） | `SetWinEventHook`（`EVENT_OBJECT_CREATE` / `EVENT_OBJECT_SHOW`） | 出現した瞬間に評価。チラ見えを最小化 |
| ポーリング（保険） | 700ms ごとに `EnumWindows` | イベントの取りこぼし（表示後に最前面化する等）を補完 |

- イベントは `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS` で購読。メッセージポンプ（`Application.Run`）が
  回っている STA スレッドで登録します。
- トップレベルに絞るため、コールバックで `idObject == OBJID_WINDOW && idChild == 0` かつ
  `GetAncestor(hwnd, GA_ROOT) == hwnd` を確認します。
- どちらの経路も最終的に `GuardEngine.Consider(hwnd)` を呼びます（`GuardEngine` は `_lock` / `_stateLock` で
  保護されており、イベント（UI スレッド）とポーリング（スレッドプール）からの並行呼び出しに対して安全）。

### 判定（`GuardEngine.Consider`）

1. 抑止が有効（`IsActive`：手動またはフォーカス連動）。
2. `IsWindow` かつ `IsWindowVisible`。
3. **最前面**（`GetWindowLong(GWL_EXSTYLE) & WS_EX_TOPMOST`）。
4. 自プロセス以外。
5. ルール一致（まずプロセス名で安く絞り、その後クラス名・タイトルを取得して照合）。
6. **モーダル回避**に該当しない（下記）。

一致したら `hide` に従い処理します。

| `hide` | 実装 |
| --- | --- |
| `Bottom`（既定） | `SetWindowPos(HWND_NOTOPMOST)` → `SetWindowPos(HWND_BOTTOM)` |
| `Minimize` | `ShowWindowAsync(SW_MINIMIZE)` |
| `Hide` | `ShowWindowAsync(SW_HIDE)` |

`HWND_NOTOPMOST` だけでは「非最前面グループの先頭」に残り見た目が変わらないため、明示的に裏送り／最小化／非表示まで行います。

### モーダル回避

モーダルダイアログを裏へ送るとアプリが進められなくなるため、次のいずれかに該当すると対象外にします
（企業向けに安全側へ倒す設計）。

- オーナーウィンドウが無効（`GetWindow(GW_OWNER)` が無効化されている）＝モーダル実行中の典型。
- `WS_EX_DLGMODALFRAME` を持つ。
- ウィンドウクラスが `#32770`（標準の Win32 ダイアログボックス。`MessageBox` / `DialogBox` 由来）。

### 復元

裏へ送ったウィンドウ（`WasTopMost` / `hide` 方法を記録）を追跡し、次のタイミングで `RestoreAll()` により
隠し方を逆転 → 元が最前面なら `HWND_TOPMOST` へ戻します。

- 抑止の期限切れ（`CheckExpiry`、ポーリングから毎回）。
- 手動停止（`Deactivate`）／アプリ終了。
- `AppDomain.UnhandledException` / `ProcessExit`（取り残し防止）。

### 抑止の有効化モデル

- **手動**：トレイメニューの時間候補（`durations`）→ `Activate(TimeSpan?)`（`null` は無制限）。
- **フォーカス連動**：`Windows.UI.Shell.FocusSessionManager`（WinRT）の `IsFocusActiveChanged` を購読し、
  フォーカス セッション中は自動抑止、終了で解除（`FocusSessionWatcher`）。
- **マイク連動**：`MicrophoneWatcher` が ConsentStore
  （`…\CapabilityAccessManager\ConsentStore\microphone`、HKCU/HKLM、`NonPackaged` 配下のデスクトップアプリ含む）の
  各アプリの `LastUsedTimeStart`/`LastUsedTimeStop` を約 2 秒間隔でポーリングし、`LastUsedTimeStop == 0`（使用中）が
  あれば自動抑止、無くなれば解除。レジストリは**参照のみ**（変更しない）。
- **複数の自動要因**：フォーカスとマイクは独立した要因として `GuardEngine` の `_autoSources` 集合で管理し、
  いずれかが有効な間は抑止を継続、すべて解除されたときに自動解除。手動操作は常に優先（自動で始めたものだけ自動解除）。
- **手動 DND は非対応**：`FocusSessionManager.IsFocusActive` はフォーカス セッションのみ反映し、手動の
  「応答不可」では変化しません。`SHQueryUserNotificationState`（`QUNS_QUIET_TIME`）も Win11 の新 DND を
  反映しないため、確実に取得できる公式手段がなく非対応としています（`NotificationStateWatcher` は検証用に残置）。

---

## 技術スタック・要件

- **.NET 9** / `TargetFramework = net9.0-windows10.0.22621.0`（`FocusSessionManager` は 22621 で追加のため）。
- `SupportedOSPlatformVersion = 10.0.17763.0`。実行時に `OperatingSystem.IsWindowsVersionAtLeast(10,0,22621)` で
  フォーカス連動をガード（未満の OS では連動スキップ、他機能は動作）。
- **WinForms**（`UseWindowsForms`）でトレイ UI。WinRT 射影で `FocusSessionManager`。
- 動作：Windows 10 1809+ / Windows 11（フォーカス連動は 22H2+）、x64。

---

## 設定（`PopGuard.rules.json`）

`PopGuard.exe` と同じフォルダーに置き、**起動時に一度だけ**読み込みます（キー名は大文字小文字非依存）。

```json
{
  "rules": [
    { "enabled": true, "process": "SomeNotifier", "title": "*通知*",
      "class": "", "hide": "Bottom", "topMostOnly": true }
  ],
  "durations": [
    { "label": { "ja": "30分", "en": "30 min" }, "minutes": 30 }
  ],
  "autoSuppressDuringFocus": true,
  "autoSuppressDuringMicrophone": false,
  "language": "auto"
}
```

- `rules[]`：`process`（必須・ワイルドカード）、`title`/`class`（空で無指定）、`hide`（`Bottom`/`Minimize`/`Hide`）、
  `enabled`、`topMostOnly`（**現状未実装**。常に TOPMOST のみ対象）。
  - ワイルドカードは `*` `?`、全体一致・大文字小文字無視。`process` が空／ワイルドカードのみは無効。
- `durations[]`：`minutes`（0 以下＝無制限）と `label`。`label` は文字列（全言語共通）でも、
  `{ "ja": "…", "en": "…" }` の言語別オブジェクトでもよい。表示言語に該当する言語だけが使われ、無い言語は
  `minutes` から自動生成される（片方だけ指定した場合、もう一方は自動生成）。`label` 自体を省略すると両言語とも
  自動生成。`durations` 省略時は既定候補（30分/1時間/2時間/一日/無制限、言語連動）。
- `autoSuppressDuringFocus`：フォーカス セッション連動（既定 true）。
- `autoSuppressDuringMicrophone`：マイク使用中（通話・Web 会議など）の連動（既定 false）。
- `language`：`auto` / `ja` / `en`。

生成支援として `tools/parameter-sheet/`（Excel パラメータシート）があります。

---

## 国際化（i18n）

- RESX：`Resources/Strings.resx`（既定＝英語, ニュートラル）＋ `Strings.ja.resx`（日本語サテライト）。
- アクセサは手書き（`Resources/Strings.cs`、`ResourceManager`）。`Strings.Culture` で上書き可能。
- `language` 設定（`ApplyOverride`）で `ja`/`en`/`auto` を選択。`auto` は `CurrentUICulture` に従う。

---

## ログ

- 出力先：`%LOCALAPPDATA%\PopGuard\PopGuard.log`（ユーザーごと）。
- 書き込みのたびに開閉し、**10MB 超で世代ローテーション**（`PopGuard_1.log`〜`_10.log`）。
- 複数インスタンス/スレッドに備え名前付き Mutex（`Local\PopGuard.Logger`）で直列化。
- Debug ビルドでは `Debug.WriteLine` にも出力（`[Conditional("DEBUG")]`）。
- 主なログ：`rules:` / `win-event:` / `focus-session:` / `mic-watch:` / `guardEngine: suppression …` /
  `guardEngine: demote|restore|skip:` / `window toplevel:`。

---

## ビルド・実行

```powershell
# 開発ビルド / 実行（フレームワーク依存）
dotnet build PopGuard\PopGuard.csproj
dotnet run   --project PopGuard\PopGuard.csproj

# 配布用：自己完結発行（.NET ランタイム不要）
dotnet publish PopGuard\PopGuard.csproj -p:PublishProfile=win-x64-selfcontained

# 発行 + インストーラ（Inno Setup）を一括
pwsh -File build.ps1
```

- インストーラは `SetupOutput\PopGuardSetup-<version>.exe`（`PopGuard.iss`、Inno Setup 6）。
- 自己完結のため TFM 配下の `win-x64\publish\` を丸ごと同梱（`ja\` サテライト含む）。

---

## 動作確認（ToastTester）

`ToastTester` は検証用の WinForms アプリです。

- **自前トースト**：TOPMOST・非アクティブ化・ツールウィンドウの右下ポップアップ（PopGuard の抑止対象）。
- **OS トースト**：本物の Windows トースト（PopGuard では抑止できないことの確認用）。
- **モーダルウィンドウ**：TOPMOST かつモーダル（モーダル回避で `guard skip` になることの確認用）。

ルールを `process = ToastTester` にして PopGuard を起動 → 抑止を有効化すると、自前トーストは裏送り、
モーダルは対象外、OS トーストは無反応、という挙動を確認できます。

---

## 制限事項

- 抑止できるのは最前面のトップレベルウィンドウのみ。Windows 標準のトースト通知は制御不可。
- 一般ユーザー権限で動作するため、管理者権限のアプリや他セッションのウィンドウは対象外（UIPI／セッション分離）。
- 「表示→その後 TOPMOST 化」するアプリでは、次のポーリング（最大 ~700ms）まで裏送りが遅れ、その間見えることがある。
- モーダルダイアログは安全のため対象外。
- `topMostOnly` は設定を受け付けるが未実装（常に TOPMOST のみ）。
- 手動 DND 連動は非対応（公式 API で確実に取得できないため）。
