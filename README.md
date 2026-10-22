> [!NOTE]
> このリポジトリの公開文書は日本語を正本とします。[言語方針](https://github.com/Naohiro2g/mc-remote-knowledge/blob/main/LANGUAGE_POLICY.md)を参照してください。

# Minecraft Remote for C#

現行Minecraft Remote ProtocolをC#から使うための一般用途Client Libraryです。
現在は開発の土台を用意したbootstrap段階です。接続やコマンドを実行する機能はまだありません。
Unityは将来の利用先の一つです。

## 開発を始める

前提として[mise](https://mise.jdx.dev/getting-started.html)をインストールしてください。
SDKの版は[mise.toml](mise.toml)で選びます。

1. リポジトリをcloneして、そのディレクトリで`mise trust`を実行します。
2. `mise install`で.NET SDKを導入します。
3. 次のコマンドでbuildとtestを実行します（初回は依存packageの取得にネットワークが必要です）。

```sh
mise exec -- dotnet restore MinecraftRemote.Client.sln
mise exec -- dotnet build MinecraftRemote.Client.sln --configuration Release --no-restore
mise exec -- dotnet test MinecraftRemote.Client.sln --configuration Release --no-build
```

GitHub ActionsのCIも同じ手順で検証します。

## 構成と現在の範囲

| 場所 | 内容 |
| --- | --- |
| `MinecraftRemote.Client.sln` | libraryとtestをまとめるsolution |
| `src/MinecraftRemote.Client/` | `netstandard2.1`のlibrary。根のnamespaceは`Code2Create.MinecraftRemote.Client` |
| `tests/MinecraftRemote.Client.Tests/` | .NET 10（LTS）で動く、libraryのroot namespaceを確認するunit test 1件 |
| `.github/workflows/ci.yml` | push／pull request時のbuildとtest |

SDKはmiseで固定し、testは[現行LTSの.NET 10](https://dotnet.microsoft.com/en-us/platform/support/policy)で動かします。
libraryのtargetは`netstandard2.1`のみです。

Protocol実装は、knowledgeで多言語比較とconformanceの一般化（Phase E／F）を具体化してから始めます。
このbootstrapにはtransport、hello、authentication、NuGetのpack／publish設定、release workflow、Unity projectを含めません。

## 設計と作業の入口

- [knowledge INDEX](https://github.com/Naohiro2g/mc-remote-knowledge/blob/main/00-hub/INDEX_ja.md)
- [C# Client Libraryの設計とbootstrap](https://github.com/Naohiro2g/mc-remote-knowledge/blob/main/18-csharp-client/README_ja.md)
- [多言語Client Libraryロードマップ](https://github.com/Naohiro2g/mc-remote-knowledge/blob/main/10-protocol/polyglot-client-roadmap_ja.md)
- [このリポジトリで作業するagent向け手順](AGENTS.md)
