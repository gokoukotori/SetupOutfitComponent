# Setup Outfit Component

`Setup Outfit Component`は、VRChatアバター用の衣装Prefabを解析し、Modular Avatarを使った装着・メニュー・トグル・BlendShape Sync構成をScene上へ生成するEditor専用パッケージです。

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

配布用VPMリポジトリやGit URLからの導入は、バージョン0.2.4では提供していません。

## 使い方

Projectウィンドウで衣装Prefabのルートを選択し、右クリックメニューから次を実行します。

```text
Assets/Setup Outfit Component/衣装セットアップ...
```

ウィザードは次の6ステップで構成されています。

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
- `適用プレビューを開く`から専用SceneViewを開き、全体ON/OFFとScene表示設定の見え方を確認できます。

全体トグルは`ON = 衣装を表示`です。Scene対象は衣装ON時だけ指定した表示／非表示へ切り替わり、全体OFF時は元Sceneの`activeSelf`へ戻ります。

適用プレビューはNDMFのカメラ別PreviewSessionを使用し、専用SceneViewだけへ適用されます。元Scene、Prefab、Transform、Selection、Undo、通常のSceneViewやNDMFプレビュー設定は変更しません。プレビュー対象は衣装全体、Scene表示対象、個別パーツの表示状態です。MA標準セットアップ、Merge Armature、BlendShape Sync、最終NDMFビルド後のArmature統合結果は反映しません。

`表示`はGameObjectの`activeSelf`を有効化するため、対象に含まれるPhysBone、Constraintなども実際の生成結果では有効になり得ます。視覚プレビューは`MeshRenderer`／`SkinnedMeshRenderer`だけを対象とし、スクリプトの`OnEnable`やコンポーネントの実動作は再現しません。`Renderer.enabled=false`のRendererや、非表示の祖先の下にあるRendererは表示設定でも描画されません。

### 4. 個別パーツ

- 衣装Prefab内のRenderer GameObject、任意のGameObject、またはステップ3のScene表示対象を個別メニュー項目へまとめます。
- RendererをScene対象として指定した場合も、Componentではなく所属GameObjectの`activeSelf`を制御します。
- 各ターゲットについて、メニューON時に表示するか非表示にするかを指定します。個別項目OFF時は対象へ値を適用しません。
- 同じPrefab／Sceneターゲットを複数の個別項目へ指定できます。同時にONの場合は、メニューで最も下にある項目を優先します。
- 項目はウィザードに表示された上から下の順でメニューへ生成されます。▲／▼ボタンまたは左端のハンドルのドラッグ＆ドロップで並べ替えられ、新しい項目は末尾へ追加されます。
- 新しいPrefabターゲットのON時状態は既定で`非表示`、ステップ3のScene対象を個別項目へ追加したときは既定で`表示`です。どちらも対象行で変更できます。
- メニュー初期状態の`自動（OFF）`は常に初期OFFとして生成します。必要な項目だけ初期ONを明示できます。
- `個別パーツプレビューを開く`から専用SceneViewを開き、各項目を`メニューON`／`メニューOFF`へ切り替えて見え方を確認できます。ステップ4から開いた場合、衣装全体はプレビュー内だけONで開始します。
- プレビュー内の一時ON/OFFは項目IDごとに保持されるため、表示名や並び順を変更しても維持されます。`個別項目を初期状態に戻す`で全項目をウィザードの初期状態へ戻せます。
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

### 6. 確認

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
- 出力名の衝突
- 明示許可していない同一Prefabの重複配置

バージョン0.2.4では、次の機能は対象外です。

- 適用プレビューでのMA標準セットアップ、Merge Armature、BlendShape Sync、最終NDMFビルド結果の再現
- BlendShapeのRemap Curve編集
- MA Shape Changer、Material Swapの自動生成
- 元PrefabのComponent／GameObject削除
- Prefabアセット保存、再生成マニフェスト、設定プロファイル
- アバター固有の配置先、下着、靴などの自動推定
- 複数Prefabの一括セットアップ
- 英語UIと英語ドキュメント

## ライセンス

Copyright © 2026 Gokoukotori

このパッケージは[Mozilla Public License 2.0](LICENSE.md)で提供されます。MPL 2.0はファイル単位のコピーレフトライセンスです。利用や再配布に関する詳細は[Mozilla公式FAQ](https://www.mozilla.org/en-US/MPL/2.0/FAQ/)を参照してください。
