# 品目登録画面 機能テスト報告書 ✅

**作成日:** 2026-01-14

---

## 1. 概要 🔎
- 対象: 品目登録ページ `/Register` の機能検証（GET 表示、POST 登録、重複コード時のエラー処理）
- 実行方法: 統合テスト（xUnit + WebApplicationFactory）を用いてローカル DB（LocalDB: `(localdb)\\MSSQLLocalDB`）上で実行
- テスト数: 3 件
- 結果: 全て成功（3 / 3 passed） ✅

---

## 2. 実行環境 ⚙️
- OS: Windows
- .NET SDK: ローカル環境の `dotnet`（プロジェクトの `TargetFramework` は `net8.0`）
- DB: LocalDB `(localdb)\\MSSQLLocalDB`（テスト実行時に一時 DB を作成し、終了時に削除）
- テスト実行コマンド:
  - dotnet test c:\\project\\DotNet10Sample.Tests

---

## 3. テストケース一覧 🧪

| テスト名 | 目的 | 操作手順 | 期待結果 | 実行結果 |
|---|---:|---|---|---|
| Get_RegisterPage_ReturnsOk_AndContainsForm | 登録ページが正常表示されるか | GET /Register | ステータス 200、フォーム要素を含む | Passed ✅ |
| Post_RegisterPage_WithDuplicateCode_ShowsModelError | 既存コードでの重複登録を防ぐ | 事前に `100` のコードを挿入 → POST /Register with Input.ItemCode=100 | エラーメッセージ「品目コードは既に使用されています。」が表示される | Passed ✅ |
| Post_RegisterPage_WithValidData_InsertsAndShowsSuccessMessage | 正常登録の動作を検証 | POST /Register with valid fields (例: Code=123, Name=TestItem) | 成功メッセージ「登録しました (ID: ... )」が表示される | Passed ✅ |

---

## 4. 実行時の注意点 / 再現手順 🔁
1. LocalDB (`(localdb)\\MSSQLLocalDB`) が利用可能であることを確認
2. プロジェクトルートで次を実行:
   - dotnet test c:\\project\\DotNet10Sample.Tests
3. テストは実行時に一意のテスト DB 名を生成して作成し、テスト終了時に削除します（安全にクリーンアップされます）

> 補足: LocalDB 以外の環境で CI を回す場合は、SQL Server コンテナや接続文字列の差し替えを検討してください。

---

## 5. 追加検討 / 推奨事項 💡
- CI 連携（GitHub Actions 等）で `dotnet test` を自動実行し、回帰検知を自動化することを推奨します。
- 追加テスト例:
  - 入力バリデーションの境界ケース（コード桁数・文字種、品目名の最大長など）
  - DB 接続異常時のハンドリング/メッセージの検証
  - E2E テスト（Playwright / Selenium）によるブラウザ実行検証
- LocalDB 非依存での実行を簡易化するため、テスト用の SQL Server コンテナや SQLite に切り替えられる設定を検討してください。

---

## 6. 変更 / アーティファクト 🗂️
- 新規追加ファイル:
  - `DotNet10Sample.Tests/DotNet10Sample.FunctionalTests.csproj`
  - `DotNet10Sample.Tests/RegisterPageTests.cs`
- それ以外の変更はありません（既存コードに対する動作確認のためのテスト追加のみ）

---

## 7. ログ（抜粋）📋
- テスト実行例: `dotnet test c:\\project\\DotNet10Sample.Tests` の実行でビルド・テスト共に成功し、全 3 件が Passed しました。

---

必要であれば、この報告書を **PDF 変換** したり、**CI 用のワークフロー (GitHub Actions)** を作成して自動化することも対応します。どちらを進めますか？