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

配布用VPMリポジトリやGit URLからの導入は、バージョン0.2.1では提供していません。

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
- 衣装ON時に非表示にする既存Sceneオブジェクトを指定します。Hierarchyで複数選択してドラッグ＆ドロップすることもできます。
- `適用プレビューを開く`から専用SceneViewを開き、全体ON/OFFと排他対象の見え方を確認できます。

全体トグルは`ON = 衣装を表示`です。指定した排他対象は衣装ON時に非表示になります。

適用プレビューはNDMFのカメラ別PreviewSessionを使用し、専用SceneViewだけへ適用されます。元Scene、Prefab、Transform、Selection、Undo、通常のSceneViewやNDMFプレビュー設定は変更しません。プレビュー対象は衣装全体、排他対象、個別パーツの表示状態です。MA標準セットアップ、Merge Armature、BlendShape Sync、最終NDMFビルド後のArmature統合結果は反映しません。

### 4. 個別パーツ

- 衣装Prefab内のRenderer GameObject、または任意のGameObjectを個別メニュー項目へまとめます。
- 各ターゲットについて、メニューON時に表示するか非表示にするかを指定します。
- 新しいターゲットのON時状態は既定で非表示です。
- メニュー初期状態はPrefabの`activeSelf`から自動判定するか、初期OFF／初期ONを明示できます。
- 複数ターゲットの状態が混在し、自動判定できない場合は初期状態の明示が必要です。
- `個別パーツプレビューを開く`から専用SceneViewを開き、各項目を`メニューON`／`メニューOFF`へ切り替えて見え方を確認できます。ステップ4から開いた場合、衣装全体はプレビュー内だけONで開始します。全体OFFでは衣装全体が非表示になり、全体ONへ戻すと選択中の個別状態が再び反映されます。
- プレビュー開始時はウィザードで設定した各項目の初期状態を使用します。`個別項目を初期状態に戻す`で全項目をその状態へ戻せます。
- 初期状態を自動判定できない項目もプレビュー上では仮にOFFから確認できますが、Scene生成前には従来どおり初期OFFまたは初期ONを明示する必要があります。
- プレビューウィンドウ内で切り替えた個別状態は確認用の一時状態です。ウィザードのメニュー初期状態、生成計画、元Prefab、Sceneには書き戻しません。

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
         └─ <個別項目>          MA Menu Item + MA Object Toggle
```

個別項目がない場合、Prefabインスタンス上のMenu Groupと`メニュー`階層は生成されません。BlendShape Syncを設定した場合は、対象となる衣装RendererのGameObjectへMA Blendshape Syncが追加されます。

## 非破壊動作とUndo

- 元Prefab、Prefab Variant、依存アセットを変更しません。
- 元PrefabインスタンスのPosition、Rotation、Scaleを`0`や`1`へ補正せず、Prefabが持つTransformを維持します。
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
- 重複ターゲット、個別パーツ間の祖先・子孫競合、未解決の初期状態
- 出力名の衝突
- 明示許可していない同一Prefabの重複配置

バージョン0.2.1では、次の機能は対象外です。

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
