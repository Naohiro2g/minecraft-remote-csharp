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

**開発の土台と設計文書ができた段階です。Minecraftへ接続して建築できる機能は、まだありません。**

- ライブラリのひな形、開発環境、自動テストを用意しました。
- Unityを使った学習用の箱庭と、先に試せる表示・操作の範囲を文書にまとめました。
- 通信・認証・建築操作の実装は、多言語で共通に使う仕様と検証方法の整理を踏まえて進めます。

Unityの箱庭アプリも、現在は構想と設計の段階です。

## 構想：創造性を引き出すUnityの箱庭

この構想の最上位に置くミッションは、**人の創造性を引き出すこと**です。
コードで学ぶ過程でも、そこから生まれる作品でも、自分の発想を試し、表現できる場を目指します。

Unity箱庭を、MinecraftとScratchを結ぶ探究への入り口にし、それぞれの表現の幅を広げる新しい柱として早く形にしたいと考えています。

最初の題材はブロック建築です。Python・Scratch・C#からブロックを積んで、結果を目で確かめられる箱庭を用意する構想です。
学習者がMinecraftやUnity Editor（Unityアプリを制作するためのソフト）を用意しなくても、コードやScratchのブロックを書き換えて建築を試せることを目指します。
柱の高さや繰り返し回数などを変え、自分の予想と結果を比べながら、何が違ったか、次に何を試すかを考えられる場にします。

公開版では、URLを開いた複数人が同じ世界を見るUnity Web（ブラウザで動くUnityアプリ）を検討しています。
教室などのLAN内や、PC1台で完結する使い方も考慮します。
世界の広さ、区画、使うブロック、接続の構成案は[共有箱庭の設計メモ](docs/unity-shared-sandbox-draft_ja.md)にまとめています。

### 先に試せる表示と操作

共通の通信機能の整備に合わせて、Unity内部だけで動く小さな建築デモを先に試す方針です。
最初はUnity内のC#で柱や壁を表示し、通信なしで次の点を確かめます。

- 金・鉄・ガラスなどを見分け、隣り合うブロックの個数を数えやすいか。
- 建築の周りを歩き、足元と照準先の座標を読みながら、形や位置を理解できるか。
- ブラウザでも操作しやすく、学習者の端末で無理なく表示できるか。

実際に触りながら、材質・移動・照準・座標表示を少しずつ比べます。
「少し変えてみたい」「別の形や見せ方を試したい」と思えるかも、試作で観察したい点です。
そこで作った部品や、見やすさ・操作感・描画負荷についての観察結果を、後の連携へ引き継ぐ計画です。
詳しくは[Unity内部で先に進める作業](docs/unity-internal-preparation_ja.md)を参照してください。

### 3D空間の表現へ広げる

ブロック建築を入口に、3Dモデルへコードで働きかける表現にも広げていきたいと考えています。
3Dモデルへのスクリプティング（コードで動きや振る舞いを記述すること）や、外部のコードから操作するリモコンは、その先の探究候補です。
表現をどこまで広げるか、そのためにどんな道具がよいかは、小さな試作を通じて言葉と形を磨いていきます。

## 多言語クライアントの開発計画との関係

Minecraft Remoteでは、Python・Java・TypeScript・C#などから、共通の通信ルールでMinecraftを操作できる環境を目指しています。
それぞれの言語に合った書きやすさを保ちながら、操作の意味をそろえるため、次の流れで開発を進めます。

1. **使いやすさと仕様を整理する。** PythonとJavaの実装を比較し、共通にする部分と言語ごとの工夫を分けます。
2. **共通のテストを整える。** 既存のテスト用データと検証の仕組みを広げ、新しい言語の実装も同じ仕様に従って動くことを確かめられるようにします。
3. **ほかの言語や接続方法へ広げる。** TypeScript、ブラウザからの接続、C#へと展開します。

C#は、この計画のPhase I（C#とUnityへ展開する段階）に位置づけられています。
通信機能を実装するときは、先行する共通化で整えた仕様とテストを使います。
当面は開発計画の大枠を保ち、共通基盤の整備と、通信なしでできるUnity内部の試作を並行して進める方針です。
Unity側で得た体験の知見と共通化の成果を持ち寄り、連携へつなげます。
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
| `docs/` | Unity箱庭の構想と、先に進める作業の設計メモ |

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
