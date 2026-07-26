# Setup Outfit Component

`Setup Outfit Component`は、VRChatアバター用の衣装Prefabを解析し、Modular Avatarを使った装着・メニュー・トグル・BlendShape Sync・Shape Changer Set構成をScene上へ生成するEditor専用パッケージです。

設定中はPrefabやSceneを変更せず、最終確認で「シーンに生成」を実行したときだけSceneへ出力します。元Prefabの保存や変更、独自Runtime Componentの追加は行いません。

## 必要環境

- Unity 2022.3.22f1
- VRChat SDK Avatars 3.10.4
- Modular Avatar 1.18.0-beta.1
- Non-Destructive Modular Framework (NDMF) 1.14.1

## 導入

このパッケージは埋め込みUPMパッケージです。Unityプロジェクトの次の場所へ配置し、必要環境に記載した依存パッケージを導入してください。

```text
Packages/com.gokoukotori.setup-outfit-component
```

配布用VPMリポジトリやGit URLからの導入は、バージョン0.2.7では提供していません。

## 使い方

Projectウィンドウで衣装Prefabのルートを選択し、右クリックメニューから次を実行します。

```text
Assets/Setup Outfit Component/衣装セットアップ...
```

ウィザードは次の7ステップで構成されています。
上部ナビゲーションはステップ1～4と5～7の二段表示です。

### 1. 衣装Prefab

- 選択した単一のRegular PrefabまたはPrefab Variantを入力として固定します。
- 出力オブジェクト名を設定します。
- Missing Script、AvatarDescriptorの混入、既存のModular Avatar構成などを検査します。

### 2. 配置先

- ロード済みSceneにあるAvatarDescriptorから対象アバターを選択します。
- 対象アバター自身またはその子孫にある配置Transformを指定します。
- AvatarDescriptorが1体だけの場合は自動選択されます。

### 3. 装着と全体動作

- 装着モードを選択します。
  - `自動（既存を優先）`: 有効なMA Merge Armatureがあれば保持し、それ以外はMA標準セットアップを実行します。
  - `MA標準セットアップを実行`: 常にModular Avatarの標準衣装セットアップを実行します。
  - `装着処理を行わない`: 小物などを想定し、配置とメニュー生成だけを行います。
- SubMenu名、全体トグル名、初期ON/OFFを設定します。
- 衣装ON時に表示または非表示へ切り替える既存Sceneオブジェクトを指定します。Hierarchyで複数選択してドラッグ＆ドロップすることもでき、新規対象は現行互換の`非表示`で追加されます。
- ここで指定したすべてのScene対象は、表示設定に関係なくステップ4で個別メニュー項目の候補としても選択できます。
- `適用プレビューを開く`から専用SceneViewを開き、全体ON/OFFとScene表示設定の見え方を確認できます。ステップ3・4・6のどこから開いても、各ステップで設定済みの内容を同じ累積プレビューへ反映します。

全体トグルは`ON = 衣装を表示`です。Scene対象は衣装ON時だけ指定した表示／非表示へ切り替わり、全体OFF時は元Sceneの`activeSelf`へ戻ります。

適用プレビューはNDMFのカメラ別PreviewSessionを使用し、専用SceneViewだけへ適用されます。元Scene、Prefab、Transform、Selection、Undo、通常のSceneViewやNDMFプレビュー設定は変更しません。初回はステップ3の全体トグル`初期ON`設定で開始し、同じウィザードから開き直した場合は衣装全体と個別項目の一時ON/OFFを維持します。プレビュー対象は衣装全体、Scene表示対象、個別パーツの表示状態、ステップ6で新規設定したShape Changer Set、および選択アバターに既に追加されているMA Shape Changerの`Set`です。衣装Renderer表示連動と既存Shape Changerでは、所有GameObjectと祖先の表示状態も反映します。MA標準セットアップ、Merge Armature、BlendShape Sync伝播、外部Animator、既存メニューの任意状態、Reaction Debugger、最終NDMFビルド後のArmature統合結果は反映しません。

`表示`はGameObjectの`activeSelf`を有効化するため、対象に含まれるPhysBone、Constraintなども実際の生成結果では有効になり得ます。視覚プレビューは`MeshRenderer`／`SkinnedMeshRenderer`だけを対象とし、スクリプトの`OnEnable`やコンポーネントの実動作は再現しません。`Renderer.enabled=false`のRendererや、非表示の祖先の下にあるRendererは表示設定でも描画されません。

### 4. 個別パーツ

- 衣装Prefab内のRenderer GameObject、任意のGameObject、またはステップ3のScene表示対象を個別メニュー項目へまとめます。
- RendererをScene対象として指定した場合も、Componentではなく所属GameObjectの`activeSelf`を制御します。
- 各ターゲットについて、メニューON時に表示するか非表示にするかを指定します。個別項目OFF時は対象へ値を適用しません。
- 同じPrefab／Sceneターゲットを複数の個別項目へ指定できます。同時にONの場合は、メニューで最も下にある項目を優先します。
- 項目はウィザードに表示された上から下の順でメニューへ生成されます。▲／▼ボタンまたは左端のハンドルのドラッグ＆ドロップで並べ替えられ、新しい項目は末尾へ追加されます。
- 新しいPrefabターゲットのON時状態は既定で`非表示`、ステップ3のScene対象を個別項目へ追加したときは既定で`表示`です。どちらも対象行で変更できます。
- メニュー初期状態の`自動（OFF）`は常に初期OFFとして生成します。必要な項目だけ初期ONを明示できます。
- `個別パーツプレビューを開く`から同じ累積プレビューを開き、各項目を`メニューON`／`メニューOFF`へ切り替えて見え方を確認できます。ステップ4から開いても衣装全体を強制的にONにはしません。
- プレビュー内の一時ON/OFFは項目IDごとに保持されるため、表示名や並び順を変更した場合や同じウィザードから開き直した場合も維持されます。`個別項目を初期状態に戻す`を明示的に実行した場合だけ、既存の全項目をウィザードの初期状態へ戻します。
- プレビューウィンドウ内の操作は、ウィザードの初期状態、生成計画、元Prefab、Sceneへ書き戻しません。
- `Prefab内ターゲット`でチェックした対象は、専用SceneView上のワイヤー枠とPrefabルートからの相対パスで確認できます。対象自身またはその子孫にある、現在表示中のRendererだけが枠表示されます。
- ターゲットのチェック操作だけではプレビューを自動起動しません。先に`個別パーツプレビューを開く`を実行すると、その後のチェック変更が開いているプレビューへ反映されます。
- チェック対象は確認用の一時選択であり、個別メニュー項目の対象や生成計画には追加されません。

同じ対象を複数の項目で制御する場合は、次のメニュー順規則を使用します。

| 衣装全体 | 同じ対象を制御する個別項目 | 適用する状態 |
|---|---|---|
| OFF | OFF／ON | Prefab側は衣装全体を非表示、Scene側は元Sceneの状態 |
| ON | 全項目OFF | Prefab側はPrefabに保存された`activeSelf`、Scene側はステップ3の表示設定 |
| ON | 1件以上ON | ON中でメニューの最も下にある項目の`ActiveWhenOn` |

親子のScene対象を同時に指定できますが、非表示の祖先の下にある対象は`表示`を指定しても描画されません。同じScene対象を既存のMA Object Toggleも制御している場合は警告を表示しますが、意図した併用を許可するため生成はブロックしません。メニュー順による勝者保証は、このウィザードが生成した個別項目同士に限ります。外部のMA Object Toggleを含む最終状態はModular AvatarのHierarchy優先規則に従うため、確認画面と最終ビルド結果を確認してください。

個別パーツプレビューはPrefab／Scene対象のメニュー順判定、全体ON/OFF、個別メニューON/OFF、チェック対象のワイヤー枠と階層パスを反映します。表示対象は`MeshRenderer`／`SkinnedMeshRenderer`です。Animator遷移、MAの1フレーム遅延、Particle、PhysBone、Constraint、外部MA／NDMFとの競合、MA標準セットアップ、Merge Armature、BlendShape Sync、最終NDMFビルド結果は反映しません。

### 5. BlendShape Sync

- 衣装Prefab内の各`SkinnedMeshRenderer`について、衣装側BlendShape名を設定前から確認できます。
- 衣装Rendererごとに、対象アバター内の同期元Rendererを1つ指定します。複数の衣装Rendererへ別々の設定を追加できます。
- 同期元候補が1件だけの場合は自動選択されます。
- `同名BlendShapeを一括追加`で、同期元と衣装側に共通する名前を重複なく追加できます。
- 名前が異なる場合は、同期元BlendShapeと衣装BlendShapeをドロップダウンから個別に対応付けます。
- Remap Curveの編集には対応せず、生成時は`0 -> 0、100 -> 100`の恒等変換を使用します。
- 入力Prefabに既存のMA Blendshape Syncがある場合はその設定を保持し、同じRendererへの新規追加を拒否します。

### 6. Shape Changer

- 衣装全体ONまたはステップ4の個別メニュー項目を制御元として、MA Shape Changerの`Set`設定を追加します。
- `衣装Renderer表示連動`では、Rendererが直接付いている衣装Prefab内GameObjectを付与先として選択し、そのGameObjectと祖先がアクティブな間だけSetを適用できます。PrefabルートもRendererが直接付いている場合だけ候補になります。
- 付与先候補はPrefab Hierarchy順で表示され、相対パス、元の`activeSelf`、Renderer種別、表示状態へ関係するステップ4項目を確認できます。付与先と、実際にBlendShapeを変更する`Shape対象`は別々に指定します。
- 対象アバター内のScene `SkinnedMeshRenderer`、または衣装Prefab内の`SkinnedMeshRenderer`を指定できます。
- Rendererを選ぶと、そのMeshに存在するBlendShape名をMesh上の順序で選択できます。衣装側のBlendShape名は設定前から表示されます。
- Set値は`0～100`です。各Shape設定で`条件を反転`を選択でき、`ChangeType=Set`、`Threshold=0.01`で生成します。
- 同じ制御元で通常と反転の設定が混在する場合は、同じGameObjectへ`Inverted=false`、`Inverted=true`の順で最大2個のMA Shape Changerを生成します。補助GameObjectは生成しません。
- 条件反転はBlendShape値を変換せず、所有GameObjectのactive階層とメニュー条件をすべて評価した後の適用条件を反転します。衣装全体OFFや祖先非アクティブ時にも反転Setが有効になる場合があります。
- 同じ制御元内の同一Renderer＋Shapeは重複指定できません。異なる個別項目が同じShapeを操作する場合は、同時ON中でメニューの最も下にある項目を優先します。
- 生成予定のBlendShape Syncの同期元Shapeを操作した場合は、Modular Avatarの最終処理で同期先へ伝播します。同じ同期先を直接操作する二重経路は生成できません。
- 入力Prefabに既存のMA Shape Changerがある場合は変更せず保持し、コンポーネント位置、対象、Shape、Set／Delete、値、Thresholdを読み取り専用で表示します。新規設定との競合は警告として生成を許可し、最終結果はMAのHierarchy順に依存します。
- 選択アバターに既に追加されているMA Shape Changerの`Set`は、専用プレビューへ読み取り専用で反映します。所有GameObjectと祖先の`activeSelf`、`Inverted`、最寄りのMA Menu Itemの初期状態を評価し、ステップ3・4の一時的な表示切り替えにも追従します。
- 解決できない既存`Set`はプレビューから除外して警告を表示します。既存`Delete`は、通常のNDMF／MAプレビューが有効な場合にMA公式プレビューの現在状態だけを表示します。
- `Shape Changerプレビューを開く`から同じ累積プレビューを開き、新規設定したSet値と全体／個別メニュー状態を確認できます。ステップ6から開いても衣装全体を強制的にONにはしません。
- Shape操作対象RendererまたはBlendShape名が未指定の行は専用プレビューから除外され、完成済みの設定と衣装表示だけを確認できます。未指定行は設定から削除・補完されず、ステップ7への移動と生成では引き続きエラーになります。

複数のShape Changerが同じShapeへSetする場合は、衣装全体、衣装Renderer付与先のPrefab Hierarchy順、個別メニュー順の順に評価され、Hierarchyで後ろにある有効な所有者が優先されます。反転なしでは所有者GameObjectまたは祖先が非アクティブになるとSet寄与を解放し、反転ありでは同じ基準条件が偽の間にSetを適用します。`Renderer.enabled=false`だけではMA Shape Changerは無効になりません。

専用プレビューは新規に設定したShape Changer Setとシェイプ単位の条件反転、衣装Renderer所有者のactive階層、選択アバター上の既存Shape Changer Setを反映します。同じRenderer＋Shapeを複数の有効な設定が操作する場合は、生成予定設定を含むHierarchy走査上で最後のSetを表示します。有効なSetがない場合は元のBlendShape Weightへ戻ります。`Renderer.enabled=false`は描画を抑制しますが、Shape Changerの有効条件としては扱いません。

既存メニューをプレビュー内で任意に操作する機能、外部Animator、Reaction Debugger、BlendShape Syncによる伝播、既存Shape Changerの`Delete`に対するステップ3・4の一時状態追従、最終NDMF競合は再現しません。NDMF／MAプレビューが有効な場合、既存`Delete`の現在状態はMA公式プレビューへ委ねます。

### 7. 確認

- 生成階層、Prefab接続、参照、Override、警告とエラーを確認します。
- エラーがなくなるまで生成は実行できません。
- 同一Prefabの重複配置だけは、この画面で明示的に許可できます。
- `シーンに生成`を押すと、すべての変更を1つのUndoグループとしてSceneへ適用します。

## 生成される構成

```text
<配置先>
└─ <出力名>                     MA Menu Item: SubMenu
   ├─ <全体トグル>             MA Menu Item + MA Object Toggle
   └─ <元Prefab instance>       Prefab接続を維持 + MA Menu Group
      └─ メニュー               MA Menu Group
         ├─ <個別項目A>         MA Menu Item + MA Object Toggle
         └─ <個別項目B>         MA Menu Item + MA Object Toggle（下側ほど優先）
```

個別項目がない場合、Prefabインスタンス上のMenu Groupと`メニュー`階層は生成されません。個別項目はウィザードと同じ上から下の順で生成され、同じ対象を制御するON項目では下側の項目が優先されます。個別項目OFF用の補助GameObjectやObject Toggleは生成しません。

BlendShape Syncを設定した場合は、対象となる衣装RendererのGameObjectへMA Blendshape Syncが追加されます。

Shape Changerを設定した場合は、全体設定を既存の全体トグルGameObjectへ、個別設定を対応する個別Menu Item GameObjectへ追加します。衣装Renderer表示連動は生成したPrefabインスタンス内の選択GameObjectへAdded Component Overrideとして追加し、元Prefabは変更しません。Shape Changer専用の補助GameObjectは生成しません。

## 非破壊動作とUndo

- 元Prefab、Prefab Variant、依存アセットを変更しません。
- 元PrefabインスタンスのPosition、Rotation、Scaleを`0`や`1`へ補正せず、Prefabが持つTransformを維持します。
- ステップ3または個別項目へ追加したScene対象の`activeSelf`、`Renderer.enabled`、Transform、親子関係を変更しません。
- 生成物はSceneだけに作成し、新規Prefabとして保存しません。
- Sceneを自動保存しません。
- Prefab接続と必要なPrefab Overrideを維持します。
- 生成中に例外、MA装着失敗、参照不成立、元Prefabのdependency hash変更などを検出した場合、変更全体をロールバックします。
- 生成に成功した場合も、UnityのUndoを1回実行すると生成物全体を削除できます。

## 入力と制限

次の条件では生成できません。

- Play Mode中またはPrefab Stage内
- Project上のRegular Prefab／Prefab Variant以外の入力
- 複数Prefab、FBX、PSDの入力
- AvatarDescriptorまたはMissing Scriptを含む衣装Prefab
- 対象アバター外の配置先や参照先
- 同一項目内の重複ターゲット、個別パーツの祖先・子孫競合、未解決の参照
- 衣装Renderer表示連動の付与先に、Rendererが直接付いていないGameObjectを指定した場合
- 出力名の衝突
- 明示許可していない同一Prefabの重複配置

バージョン0.2.7では、次の機能は対象外です。

- 適用プレビューでのMA標準セットアップ、Merge Armature、BlendShape Sync伝播、外部Animator、既存メニューの任意状態、Reaction Debugger、最終NDMFビルド結果の再現
- 既存MA Shape Changerの`Delete`に対する、ステップ3・4の一時状態追従
- BlendShapeのRemap Curve編集
- MA Shape Changerの`Delete`、Threshold編集
- MA Material Swapの自動生成
- 元PrefabのComponent／GameObject削除
- Prefabアセット保存、再生成マニフェスト、設定プロファイル
- アバター固有の配置先、下着、靴などの自動推定
- 複数Prefabの一括セットアップ
- 英語UIと英語ドキュメント

## ライセンス

Copyright © 2026 Gokoukotori

このパッケージは[Mozilla Public License 2.0](LICENSE.md)で提供されます。MPL 2.0はファイル単位のコピーレフトライセンスです。利用や再配布に関する詳細は[Mozilla公式FAQ](https://www.mozilla.org/en-US/MPL/2.0/FAQ/)を参照してください。
