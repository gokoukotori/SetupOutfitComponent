# 変更履歴

このパッケージの主な変更を記録します。

## [0.2.8] - 2026-07-26

- ステップ6の各Shape設定で`Set`／`Delete`を選択できるようにし、Deleteは`Value=100`、`Threshold=0.01`へ固定しました。
- Deleteは全BlendShapeフレームの差分を評価し、Thresholdを超える頂点を1つでも含むPrimitiveを削除します。複数Shapeは削除対象の和集合として扱います。
- SetとDeleteを同一コンポーネントへ混在させつつ、条件反転の違いは従来どおり最大2個のMA Shape Changerへ分割生成します。
- Hierarchyで最後に有効な設定を優先し、後段Setによる先行Deleteの解除と、後段DeleteによるPrimitive削除へ対応しました。
- 専用NDMF SceneViewへ生成予定Deleteの視覚結果を反映し、必要なProxy Meshだけを生成・更新・破棄するようにしました。NaNimation、Animator、既存Deleteの一時状態追従は対象外です。
- DeleteはBlendShape Syncへ伝播させず、既存Deleteとの競合は警告として生成を許可します。
- 元Prefab、Scene、Transform、BlendShape Weight、公開API、Runtime assembly、Unity／VRChat SDK／Modular Avatar／NDMFの依存条件を変更しない既存契約を維持しました。

## [0.2.7] - 2026-07-26

- ステップ6の各Shape設定へ`条件を反転`を追加し、シェイプ単位でMA Shape Changerの適用条件を反転できるようにしました。
- 同じ制御元で通常と反転の設定が混在する場合、設定順を維持して`Inverted=false`、`Inverted=true`の最大2コンポーネントへ分割生成するようにしました。
- 条件反転を所有GameObjectのactive階層とメニュー条件の評価後に適用し、衣装全体OFF、個別項目OFF、祖先非アクティブを専用プレビューへ反映しました。
- 反転切り替えではShape Filterのルールだけを更新し、Render Mirror、Scene Filter、Renderer集合、NDMF Nodeを再構築しない動作を維持しました。
- 同一制御元内の同一Renderer＋Shape重複、未指定行の生成拒否、元Scene／Prefab／Transformを変更しない既存契約を維持しました。
- 公開API、Runtime assembly、生成Hierarchy、Unity／VRChat SDK／Modular Avatar／NDMFの依存条件は変更していません。

## [0.2.6] - 2026-07-25

- 選択アバターに既に追加されているMA Shape Changerの`Set`を、専用NDMF SceneViewの累積プレビューへ常時反映するようにしました。
- 既存Shape Changerの所有GameObjectと祖先の`activeSelf`、`Inverted`、最寄りのMA Menu Itemの初期状態、Hierarchy上の優先順をプレビューへ反映しました。
- ステップ3のScene表示設定とステップ4の個別項目による一時的な表示切り替えへ、既存Shape Changerの有効状態が追従するようにしました。
- 既存の`Delete`はMA公式NDMFプレビューの現在状態に委ね、専用プレビューの一時状態には追従しないことを明記しました。
- 外部Animator、既存メニューの任意状態、Reaction Debugger、BlendShape Sync伝播、最終NDMF競合は引き続き専用プレビューの対象外です。
- 元Scene、Prefab、生成されるMA構成、公開API、Runtime assembly、依存条件を変更しない非破壊動作を維持しました。
- 衣装セットアップウィンドウ上部の7ステップを、最小幅でもラベルが見切れない4＋3の二段ナビゲーションへ変更しました。

## [0.2.5] - 2026-07-24

- 新しいステップ6を追加し、衣装全体または個別メニュー項目のON状態に連動するMA Shape Changerの`Set`設定を作成できるようにしました。
- Rendererが直接付いている衣装Prefab内GameObjectへMA Shape ChangerをAdded Component Overrideとして追加し、そのGameObjectと祖先の表示状態にSetを連動できるようにしました。
- 衣装Renderer所有者の候補をPrefab Hierarchy順に選択でき、元の`activeSelf`、Renderer種別、関連する個別メニュー項目をウィザードと確認画面へ表示するようにしました。
- 対象アバター側と衣装Prefab側の`SkinnedMeshRenderer`、BlendShape名、`0～100`のSet値を指定できるようにしました。
- 専用NDMF SceneViewへ生成予定のShape Changer Setを反映し、全体・衣装Rendererのactive階層・個別メニュー状態とHierarchy後勝ちの結果を生成前に確認できるようにしました。
- 入力Prefabに含まれる既存MA Shape Changerを変更せず保持し、ウィザードと確認画面へ読み取り専用で表示するようにしました。
- 衣装Renderer表示連動は元PrefabやBlendShape Weightを変更せず、所有者が非アクティブになった場合はそのSet寄与だけを解放する構成にしました。
- Shape操作対象RendererまたはBlendShape名が未指定の行を専用プレビューから除外し、完成済みのShape Changer設定と衣装表示のプレビューを継続できるようにしました。未指定行は生成時のエラーとして維持します。
- ステップ3・4・6のプレビュー入口を同じ累積プレビューへ統一しました。初回は全体トグルの初期ON設定に従い、同じウィザードから開き直した場合は全体と個別項目の一時ON/OFFを維持し、明示的な初期状態リセットだけで個別状態を戻します。
- Shape Changerの`Delete`、Threshold編集、Inverted編集は対象外とし、公開API、Runtime assembly、既存の依存条件を維持しました。

## [0.2.4] - 2026-07-24

- ステップ3のScene対象ごとに、衣装ON時の`表示`／`非表示`を指定できるようにしました。新規対象は従来互換の`非表示`で追加され、全体OFF時は元Sceneの状態へ戻ります。
- NDMF適用プレビューと個別パーツプレビューへScene表示設定を反映し、個別項目がONの場合はメニューで最も下にある項目の設定を優先するようにしました。
- 同じPrefab／Sceneターゲットを複数の個別メニュー項目へ指定できるようにしました。
- 手動Priorityを廃止し、ウィザードの項目順をそのまま生成HierarchyとVRChatメニューへ反映して、同時にONの項目では下側を優先するようにしました。
- ▲／▼ボタンとドラッグ＆ドロップによる個別メニュー項目の並べ替えを追加しました。
- 個別項目OFF時の反対状態を強制する補助GameObjectと`個別OFF制御`階層を廃止し、Prefab／Sceneの基準状態へ戻すようにしました。
- 個別パーツプレビューもメニュー順の競合解決に対応し、並べ替え後も項目ごとの一時ON/OFFを維持するようにしました。

## [0.2.3] - 2026-07-23

- ステップ3のScene排他対象を、ステップ4の個別メニュー項目へGameObjectの`activeSelf`単位で追加できるようにしました。
- Scene排他対象は全体OFFで元状態を維持し、全体ONでは個別メニューOFF時に`!ActiveWhenOn`、ON時に`ActiveWhenOn`を適用するようにしました。
- Scene排他対象の個別OFF状態を設定する補助階層`個別OFF制御/<項目> OFF`を生成し、元Sceneオブジェクトを変更しない構成にしました。
- 同じScene対象を制御する既存MA Object Toggleは警告として報告し、意図した併用をブロックしないようにしました。
- 個別パーツプレビューとチェック対象表示をScene排他対象へ拡張しました。

## [0.2.2] - 2026-07-23

- ステップ4でチェックしたPrefab内ターゲットを、専用SceneView上のワイヤー枠とPrefabルートからの相対パスで確認できるようにしました。
- 個別トグル適用後に表示されているRendererだけを枠表示し、非表示中のRendererをチェック対象から除外しました。
- プレビューは従来どおりボタンから手動で開き、チェック対象は個別メニュー項目や生成計画へ反映しない一時選択として扱います。

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
