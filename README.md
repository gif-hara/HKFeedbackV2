# HK Feedback V2

## 別の Unity プロジェクトにインストールする

HK Feedback V2 は Unity Package Manager（UPM）からインストールできます。開発元の Unity バージョンは `6000.7.0b5` です。Inspector の右クリックメニューやグラフ編集を使う場合は、このバージョンを基準にしてください。

### GitHub からインストール

Git をインストールし、Unity から利用できる状態にします。UniTask、SerializeReferenceExtensions、uGUI はインストール先で指定する必要があります。

#### Package Manager で追加する

インストール先の Package Manager で **＋ → Install package by name** を選び、uGUI の名前に `com.unity.ugui`、バージョンに `2.7.0` を指定します。続けて **＋ → Install package from git URL** から、以下の URL を上から順に1件ずつ貼り付けます。[Unity の Git URL インストール手順](https://docs.unity.com/en-us/engine/6000.6/manual/packages-list/managing-packages-window/upm-ui-actions/upm-ui-giturl)

**UniTask**

```text
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
```

**SerializeReferenceExtensions**

```text
https://github.com/gif-hara/Unity-SerializeReferenceExtensions.git?path=Assets/MackySoft/MackySoft.SerializeReferenceExtensions#feature-generic-class
```

**HK Feedback V2**

```text
https://github.com/gif-hara/HKFeedbackV2.git?path=/Assets/HKFeedbackV2#main
```

#### manifest.json で追加する

インストール先の `Packages/manifest.json` を開き、既存の依存関係を残したまま、`dependencies` に以下の4項目を追加します。[Unity の Git 依存関係ガイド](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/upm-git)

```json
{
  "dependencies": {
    "com.gif-hara.hkfeedbackv2": "https://github.com/gif-hara/HKFeedbackV2.git?path=/Assets/HKFeedbackV2#main",
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.mackysoft.serializereference-extensions": "https://github.com/gif-hara/Unity-SerializeReferenceExtensions.git?path=Assets/MackySoft/MackySoft.SerializeReferenceExtensions#feature-generic-class",
    "com.unity.ugui": "2.7.0"
  }
}
```

保存して Unity に戻り、Package Manager の **In Project** に **HK Feedback V2** が表示されることを確認します。

### 利用側のアセンブリを設定する

HK Feedback V2 を使うスクリプトを Assembly Definition（`.asmdef`）の配下に置き、その **Assembly Definition References** に `HKFeedbackV2` を追加します。ライブラリ側の `Auto Referenced` が無効なため、この参照が必要です。`UniTask` 型を使う場合や `IAction.InvokeAsync` を呼び出す場合は、`UniTask` も追加します。[Unity のアセンブリ参照ガイド](https://docs.unity.com/en-us/engine/6000.7/manual/programming-environment/script-compilation/assembly-definition-files/assembly-definitions-referencing)

### ローカルの変更を別プロジェクトで試す

ローカル版を使う場合は、インストール先の `Packages/manifest.json` に UniTask、SerializeReferenceExtensions、uGUI の3項目を指定します。インストール先の Package Manager で **＋ → Install package from disk** を選び、開発元リポジトリの `Assets/HKFeedbackV2/package.json` を開きます。開発元のフォルダが参照されるため、コミット前の変更も試せます。[Unity のローカルインストール手順](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/managing-packages-window/upm-ui-actions/upm-ui-local)
