# .NETコンソールで始める

FirstBlockは、C#から1ブロックを置いて読み戻す作例です。このチェックアウトのライブラリを参照します。
接続先にはprotocol `24.0.0`と互換性のあるMcRemoteまたは.NET箱庭を使います。

## 準備と変更範囲

リポジトリをVS Codeで開き、[開発を始める](../../README.md#開発を始める)の手順でmiseと.NET SDKを準備します。接続先でMcRemoteまたは.NET箱庭を起動します。
必要なのはMinecraftのゲーム用ポートではなく、McRemoteのTCPポートです。

実行すると、指定する位置の**実worldの1ブロックを金ブロックへ置換**します。playerだけに見える仮表示ではありません。
置換してよい空き地を選んでください。座標は接続の建築原点からの相対位置です。この作例ではhelloにbuildを渡さず、serverの既定を使います。

`--restore`を付けると先に元のBlockValueを読み、同じ接続が生きていれば最後にblock IDと全stateを戻します。
チェストの中身などのblock entity data、周囲の更新、液体や重力による変化まで保存する機能はありません。
完全な復元が必要な場所ではworldのbackupを取り、この作例は空き地で試してください。

## 実行

リポジトリの根で、次の値を手元の環境に置き換えて指定します。接続先や座標をsourceや公開logへ書かないでください。

```sh
export MC_REMOTE_HOST='McRemoteのホスト名'
export MC_REMOTE_PORT='McRemoteのTCPポート番号'
export MC_REMOTE_X='置換してよい位置のX'
export MC_REMOTE_Y='置換してよい位置のY'
export MC_REMOTE_Z='置換してよい位置のZ'
mise exec -- dotnet run --project examples/FirstBlock/FirstBlock.csproj --configuration Release -- --restore
```

画面に`/mcremote pair ...`が表示されたら、Minecraft内でそのコマンドを実行します。
承認したら作例の端末でEnterを押します。期限を過ぎたら作例を終了し、実行し直して新しいコマンドを使います。
承認は人が行い、作例が代理で承認することはありません。

読み戻しが一致すると`PULSE_PASS`を表示します。`PAIRING_PASS`はペアリングした場合、`RESTORE_PASS`は`--restore`を付けた場合に表示します。
下はペアリングしてMcRemote 2400.0.0rc1へ接続したときの表示例です。

```text
PAIRING_PASS
HELLO_PASS
HELLO_PROTOCOL=24.0.0
SERVER_VERSION=2400.0.0rc1
MC_VERSION=1.21.11
PULSE_PASS: hello / setBlock / getBlock
RESTORE_PASS
CLOSE_PASS
```

`--restore`なしでは金ブロックを残します。Minecraft内で取り除くか、必要なブロックに置き換えて片付けてください。
切断などで`RESTORE_INCOMPLETE`になった場合もMinecraft内で確認して片付けます。完了不明の設置を自動再送しません。
tokenは保存せず、再実行時は必要なら再びペアリングします。tokenやpairing_idは表示しません。

## 読み替えて試す

[Program.cs](Program.cs)は`ConnectAsync` → `SetBlockAsync` → `GetBlockAsync` → `CloseAsync`を順に呼びます。
まず`new BlockSpec("minecraft:gold_block")`のIDを変え、読み戻しの確認条件も合わせて変えると、同じ操作で結果を比べられます。
stateを指定する方法、例外の扱い、Unityのmain threadとの関係は[Client API](../../docs/client-api_ja.md)を参照してください。
