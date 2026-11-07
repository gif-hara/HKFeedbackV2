# HKFeedbackV2 Source Generator

`[FeedbackAction]` を付けたメソッドを呼び出す、`[Serializable]` な `IAction` 実装をコンパイル時に生成します。
Generator 自体は Unity に依存しない .NET Standard 2.0 プロジェクトで、Roslyn 4.3.1 を使用しています。
生成コードは既存の `IResolver<T>` と `SubclassSelector` を使用し、Inspector の `IAction` 配列から選択できます。

## ビルドとテスト

.NET SDK 8 以降で、Unity プロジェクトのルートから実行します。

```powershell
dotnet build SourceGenerator/HKFeedbackV2.SourceGenerator.sln -c Release
dotnet test SourceGenerator/HKFeedbackV2.SourceGenerator.sln -c Release
```

通常のビルドで `HKFeedbackV2.SourceGenerator.dll` を `Assets/HKFeedbackV2/Runtime/` に自動配置します。

Unity が生成するルートのソリューションとは独立しています。
Generator のビルドに Unity の起動や Unity アセンブリへの参照は必要ありません。
テストは既存の Runtime ソースをリンクし、Unity と UniTask の最小限の代替実装で生成コードのコンパイル・実行を検証します。

## Unity への導入・更新

このプロジェクトにはビルド済み DLL と Unity が生成したインポート設定も配置しています。
Generator のコードを変更した場合は、DLL を再ビルドして更新してください。
`[FeedbackAction]` を付けた Unity 側のメソッドを変更しただけなら、通常の Unity コンパイルで再生成されます。

Unity でこのプロジェクトを開き、UniCli が使用できる状態で実行します。

```powershell
./SourceGenerator/build.ps1 -InstallToUnity
# CLI の場所を明示する場合
./SourceGenerator/build.ps1 -InstallToUnity -UniCli C:/tools/unicli.exe
```

DLL を `Assets/HKFeedbackV2/Runtime/` に出力し、Unity にインポートさせ、
`RoslynAnalyzer` ラベル・通常のプラグインとして読み込まない設定を適用して、コンパイルを確認します。
Roslyn の DLL や Generator プロジェクトの `bin` フォルダー全体を Assets にコピーする必要はありません。

手動で導入する場合は Release の `netstandard2.0/HKFeedbackV2.SourceGenerator.dll` だけを `Assets/HKFeedbackV2/Runtime/` に配置し、
Plugin Inspector で Any Platform・Editor・その他の有効なプラットフォームを解除し、
`RoslynAnalyzer` ラベルを付けてください。
Unity で参照する `.meta` ファイルは Unity に生成させます。

## 基本的な使い方

```csharp
using HKFeedbackV2;
using UnityEngine;

namespace Game
{
    public static class Effects
    {
        [FeedbackAction]
        public static void SetActive(GameObject target, bool active = true)
        {
            target.SetActive(active);
        }
    }
}
```

同じ名前空間に `Game.Effects_SetActiveAction : IAction` が生成されます。
`target` は `Constant<GameObject>(default)`、`active` は `Constant<bool>(true)` が初期値の
`IResolver<T>` フィールドになり、Inspector で別の Resolver に差し替えられます。
引数名は Inspector のフィールドラベルに使用します。

生成する名前は `<型名>_<メソッド名>Action` です。
入れ子の型では `<外側の型名>_<内側の型名>_<メソッド名>Action` になります。
属性に引数やプロパティはありません。
オーバーロードや既存のクラスと名前が重なる場合は `HKFSG004` を報告します。
メソッド名または宣言先の型名を変更して衝突を解消してください。

生成名はシリアライズされた `SerializeReference` の型名になります。
元の型名やメソッド名を変更すると生成名も変わるため、保存済みの参照も更新してください。

## インスタンスメソッド

```csharp
public sealed class Actor : MonoBehaviour
{
    [FeedbackAction]
    public void ShowMessage(string message = "Hello") { Debug.Log(message, this); }
}

// 実行時に、呼び出し対象を登録します。
var container = new Container();
container.Register<Actor>(ContainerKey.Anonymous, actor);
await actions.InvokeAsync(container, cancellationToken);
```

`Actor_ShowMessageAction` の呼び出し対象は `IResolver<Actor>` で、既定では
`ContainerAnonymous<Actor>` です。Inspector で `Constant<Actor>` や `ContainerWithKey<Actor>` に変更できます。
Container は登録された型で検索するため、対象のメソッドが宣言された型で登録してください。
元のクラスを `partial` にする必要はありません。

## 非同期・実行コンテキスト

```csharp
[FeedbackAction]
public static UniTask Wait(float seconds, IContainer container, CancellationToken cancellationToken)
{
    return UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: cancellationToken);
}
```

- `void` は呼び出し後に `UniTask.CompletedTask` を返します。
- `UniTask` はそのまま返し、`Task` は `AsUniTask` で変換します。呼び出し元で完了・例外を受け取れます。
- `IContainer` と `CancellationToken` 型の引数は `InvokeAsync` の値を渡し、Inspector 用のフィールドは生成しません。
- 呼び出し直前にキャンセルを確認します。実行開始後のキャンセルは、元の関数が渡されたトークンを処理します。
- 通常の引数は `Constant<T>(default(T))`、省略可能な引数は宣言した既定値で初期化します。

`Assets/Test/FeedbackActionExamples.cs` に、ログと待機処理の例があります。
それぞれ `FeedbackActionExamples_LogAction` と `FeedbackActionExamples_WaitAction` が生成されます。
サンプルシーンの Controller の演出配列から選択できます。サンプルを追加しただけでは自動実行されません。

## 対応範囲と診断

同じアセンブリから呼び出せる `public` / `internal` メソッドに対応します。
インスタンスメソッドの宣言先はクラス、静的メソッドは非ジェネリック型である必要があります。
戻り値を伴う関数の結果を Container に登録する処理は行いません。

| 診断 | 内容 |
|---|---|
| HKFSG002 | アクセス不可、ジェネリック、抽象メソッドなど、呼び出せない宣言 |
| HKFSG003 | 戻り値が `void` / `UniTask` / `Task` 以外 |
| HKFSG004 | オーバーロードや既存クラスとの生成名の衝突 |
| HKFSG005 | `ref` / `in` / `out`、ポインター、`Span<T>` など Resolver にできない引数 |
| HKFSG006 | 完了を待てない `async void` |

対象アセンブリ自身で宣言されたメソッドだけを生成対象とし、参照先のメソッドを重複生成しません。
生成された `.g.cs` はコンパイラーに渡され、Assets にファイルとして書き出しません。

## 参考

- [Unity: Create and use a source generator](https://docs.unity3d.com/6000.3/Documentation/Manual/create-source-generator.html)
- [Roslyn incremental generators cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md)
- [UniCli](https://github.com/yucchiy/UniCli)
