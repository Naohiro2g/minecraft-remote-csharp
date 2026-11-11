# Minecraft Remote for C#

[マイクラリモコン](https://mc-remote.com/)（Minecraft Remote / mc-remote）をC#から使うためのライブラリを開発しています。
コンソールアプリやUnityなど、さまざまなC#のプログラムから使えるものを目指します。

マイクラリモコンは、コーディングを通じて「学び方を学ぶ」ための道具です。
作例を動かして変え、結果を観察し、気づきを言葉にしながら、自分で確かめて次に試すことを考えます。
読み取る力、気づきを言葉にする力、自分で確かめて判断する力を育てる学びを支えます。
コーディングの文法や技法の習得は、こうした探究を支えるものと位置づけています。

**公式サイト**：[mc-remote.com](https://mc-remote.com/)

> [!NOTE]
> **言語方針について**
>
> 最新で正確な情報を保つため、このリポジトリの公開文書は日本語を正本としています。
> 質問や不具合報告（Issue）、変更提案（Pull Request）は、母国語での投稿も歓迎します。詳しくは[言語方針](https://github.com/Naohiro2g/mc-remote-knowledge/blob/main/LANGUAGE_POLICY.md)をご覧ください。

## 現在地

**ライブラリの開発の土台を整えました。Minecraftへ接続して建築できる機能は、まだありません。**

- ライブラリのひな形、開発環境、自動テストを用意しました。
- 通信・認証・建築操作は、多言語で共通に使う仕様と検証方法を踏まえて実装します。

## Unityとの関係

Unityの箱庭、通信なしのローカル建築デモ、設計メモ・図・観察記録は、
[mc-remote-unity-sandbox](https://github.com/Naohiro2g/mc-remote-unity-sandbox)へ移行しました。
移行先ではUnityの表示・操作からUnity Webへ育てていきます。

このrepoは一般用途のC# Client Libraryを担当します。Unityも、その利用先の一つです。
Unityへライブラリを渡す配布形式は、今後このrepoで扱います。

## 多言語クライアントの開発計画との関係

Minecraft Remoteでは、Python・Java・TypeScript・C#などから、共通の通信ルールでMinecraftを操作できる環境を目指しています。
それぞれの言語に合った書きやすさを保ちながら、操作の意味をそろえるため、次の流れで開発を進めます。

1. **使いやすさと仕様を整理する。** PythonとJavaの実装を比較し、共通にする部分と言語ごとの工夫を分けます。
2. **共通のテストを整える。** 既存のテスト用データと検証の仕組みを広げ、新しい言語の実装も同じ仕様に従って動くことを確かめられるようにします。
3. **ほかの言語や接続方法へ広げる。** TypeScript、ブラウザからの接続、C#へと展開します。

C#は、この計画のPhase I（C#とUnityへ展開する段階）に位置づけられています。
通信機能を実装するときは、先行する共通化で整えた仕様とテストを使います。
Unity内部の試作は別repoで並行して進め、体験の知見と共通化の成果を持ち寄って連携へつなげます。
詳しい段取りは[多言語クライアントのロードマップ](https://github.com/Naohiro2g/mc-remote-knowledge/blob/main/10-protocol/polyglot-client-roadmap_ja.md)を参照してください。

## 開発を始める

開発ツールのバージョンをそろえるため、[mise](https://mise.jdx.dev/getting-started.html)をインストールしてください。
使用する.NET SDK（C#のビルドやテストに使う開発ツール）の版は[mise.toml](mise.toml)で指定しています。

1. このリポジトリを手元に取得し、そのディレクトリで`mise trust`を実行します。
2. `mise install`で指定された.NET SDKを導入します。
3. 次のコマンドでビルドとテストを実行します。初回は依存ライブラリの取得にネットワークが必要です。

```sh
mise exec -- dotnet restore MinecraftRemote.Client.sln
mise exec -- dotnet build MinecraftRemote.Client.sln --configuration Release --no-restore
mise exec -- dotnet test MinecraftRemote.Client.sln --configuration Release --no-build
```

GitHub Actionsによる自動検証でも、変更を送信したときに同じ手順を実行します。

### 開発用のファイル構成

| 場所 | 内容 |
| --- | --- |
| `MinecraftRemote.Client.sln` | ライブラリとテストをまとめる開発用の構成ファイル |
| `src/MinecraftRemote.Client/` | ライブラリ本体のひな形 |
| `tests/MinecraftRemote.Client.Tests/` | ライブラリの名前空間を確認するテスト1件 |
| `.github/workflows/ci.yml` | GitHub上でビルドとテストを自動実行する設定 |

ライブラリは複数の.NET環境で使えるよう、.NET Standard 2.1（設定値は`netstandard2.1`）を対象にしています。
名前空間（C#の型をまとめる名前）は`Code2Create.MinecraftRemote.Client`です。
テストは.NET 10で動かします。

NuGet（.NETのパッケージ配布サービス）での公開方法は、今後決めます。

## 関連プロジェクトと設計資料

- [Python版クライアント](https://github.com/Naohiro2g/minecraft-remote-api)：Pythonから使う方法や、自動建築の作例を紹介しています。
- [学習支援の考え方](https://github.com/Naohiro2g/mc-remote-knowledge/blob/main/20-%E6%95%99%E6%9D%90/ai-learning-design_ja.md)：観察・言語化・検証を通じて、自分で学びを進める力を支える方針です。
- [共通の設計資料の案内](https://github.com/Naohiro2g/mc-remote-knowledge/blob/main/00-hub/INDEX_ja.md)
- [C#ライブラリの設計と開発の土台](https://github.com/Naohiro2g/mc-remote-knowledge/blob/main/18-csharp-client/README_ja.md)
- [このリポジトリで作業する開発エージェント向け手順](AGENTS.md)
