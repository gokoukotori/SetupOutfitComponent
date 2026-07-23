# 変更履歴

このパッケージの主な変更を記録します。

## [0.2.1] - 2026-07-23

- ステップ4から既存の専用SceneViewを衣装全体ONで開き、個別メニュー項目を`メニューON`／`メニューOFF`へ切り替えて確認できるようにしました。
- 複数ターゲットをまとめた項目や、対象ごとにON時の表示状態が異なる項目を生成結果と同じ規則でプレビューします。
- 個別項目を初期状態へ一括で戻せるようにし、プレビュー内の切り替えは生成計画へ書き戻さない一時状態として扱います。
- 元Scene、Prefab、Transform、Selection、Undo、通常のNDMFプレビュー設定を変更しない既存の非破壊動作を維持しました。

## [0.2.0] - 2026-07-23

- ステップ3から開ける、NDMFのカメラ別PreviewSessionを使った衣装適用プレビューを追加しました。
- 専用SceneViewで衣装の全体ON/OFFと、衣装ON時に非表示にする排他対象を確認できるようにしました。
- 元Scene、Prefab、Transform、Selection、Undo、通常のNDMFプレビュー設定を変更しないRender Mirror方式を採用しました。
- Non-Destructive Modular Framework (NDMF) `>=1.14.1`を直接依存へ追加しました。
- MA装着処理、個別パーツ、BlendShape Sync、最終NDMFビルド結果はプレビュー対象外であることを明記しました。

## [0.1.1] - 2026-07-23

- BlendShape Sync画面のRenderer、Mesh、BlendShape候補をキャッシュし、再描画時の負荷を軽減しました。
- BlendShape選択用ドロップダウンの候補を、クリック時にだけ構築するよう変更しました。
- Scene参照の更新と確認画面の検証をイベント駆動化しました。
- 依存条件をVRChat SDK Avatars `>=3.10.4`、Modular Avatar `>=1.18.0-beta.1`へ緩和しました。

## [0.1.0] - 2026-07-23

- Mozilla Public License 2.0、README、変更履歴を追加しました。
