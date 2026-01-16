# 品目検索画面 機能テスト報告書 ✅

**作成日:** 2026-01-14

---

## 1. 概要 🔎
- 対象: 品目検索ページ `/Search` の機能検証（表示、検索条件（品目名／カテゴリ null 指定）、CSV 出力）
- 実行方法: 統合テスト（xUnit + WebApplicationFactory）を用いてローカル DB（LocalDB: `(localdb)\\MSSQLLocalDB`）上で実行
- テスト数: 4 件
- 結果: 全て成功（4 / 4 passed） ✅

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
| Get_SearchPage_ReturnsOk_AndContainsFields | 検索ページが正常表示されるか | GET /Search | ステータス 200、検索フィールド（品目名、品目コード、カテゴリ）を含む | Passed ✅ |
| Get_Search_WithItemName_ReturnsMatchingResults | 品目名で検索できるか | GET /Search?ItemName=Apple | 結果に「Apple」を含む、他の不要行は含まない | Passed ✅ |
| Get_Search_WithCategoryNull_ReturnsItemsWithNullCategory | カテゴリが null の絞り込みが動作するか | GET /Search?CategoryCode=__NULL__ | カテゴリ null の品目（例: Banana）が返る | Passed ✅ |
| Get_ExportCsv_ReturnsCsv_WithHeaderAndRows | CSV エクスポートが正しく動作するか | GET /Search?handler=ExportCsv&ItemName=Grape | Content-Type に `text/csv` を含み、ヘッダと対象行が含まれる | Passed ✅ |

---

## 4. 実行時の注意点 / 再現手順 🔁
1. LocalDB (`(localdb)\\MSSQLLocalDB`) が利用可能であることを確認
2. プロジェクトルートで次を実行:
   - dotnet test c:\\project\\DotNet10Sample.Tests
3. テストは実行時に一意のテスト DB 名を生成して作成し、テスト終了時に削除します（自動クリーンアップ）

---

## 5. 追加検討 / 推奨事項 💡
- 追加テスト例:
  - 組み合わせ検索（品目名＋カテゴリ）や品目コードによる完全一致検索の確認
  - 大量データ時のページ応答やパフォーマンス検証（負荷試験）
  - CSV の文字エンコーディングや Excel での互換性に関する詳細検証
- CI での自動実行（GitHub Actions）を推奨します。

---

## 6. 変更 / アーティファクト 🗂️
- 新規追加ファイル:
  - `DotNet10Sample.Tests/SearchPageTests.cs`
  - `TEST_REPORT_SEARCH.md`（本ファイル）
- 既存のテストはそのまま維持（Register のテスト等）

---

必要であれば、この報告書を PDF に変換したり、CI ワークフローを作成して自動実行するステップを追加します。どちらを進めますか？