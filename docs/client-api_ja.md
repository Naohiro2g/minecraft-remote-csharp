# C# Client API：最初のTCP slice

これは実装したC# APIの案と利用上の境界です。Protocolの正本は
[knowledgeのwire contract](https://github.com/Naohiro2g/mc-remote-knowledge/blob/076a127a22eaf0188c1d13822bfd55bfeee9ec38/10-protocol/wire-format-design_ja.md)です。
namespaceは`Code2Create.MinecraftRemote.Client`、libraryのtargetは`netstandard2.1`です。

## 接続とブロック操作

```csharp
using Code2Create.MinecraftRemote.Client;
using Newtonsoft.Json.Linq;

// host、port、x、y、zはアプリケーションが手元で設定する。
var options = new ClientOptions(host, port,
    new ClientIdentity("my-building-app", "development"));
await using var client = await MinecraftClient.ConnectAsync(options,
    onPairing: async (prompt, ct) => await ShowApprovalPromptAsync(prompt.Command, ct));
await client.SetBlockAsync(x, y, z,
    new BlockSpec("minecraft:oak_log", new JObject { ["axis"] = "x" }));
BlockValue block = await client.GetBlockAsync(x, y, z);
// await usingを抜けると接続を閉じる。
```

`ShowApprovalPromptAsync`は呼び出すアプリケーションが用意する表示・承認待ちです。
動くコンソール版は[FirstBlock](../examples/FirstBlock/README_ja.md)にあります。

| 公開する型 | コンストラクタ・メソッド・主なproperty | 選択理由 |
| --- | --- | --- |
| `MinecraftClient` | static `ConnectAsync(options, credentialStore?, onPairing?, cancellationToken)`、`Hello`、`IsConnected`、定数`Protocol` | 接続できたinstanceだけを返す。helloと必要な初回認証をまとめる |
| 同上 | `Task SetBlockAsync(x,y,z,BlockSpec,ct)`、`Task<BlockValue> GetBlockAsync(x,y,z,ct)` | C#に自然なAsync suffixとTask。結果を待てるid付きrequestだけを送る |
| 同上 | `Task<JToken> RequestAsync(method,parameters,ct)` | 未投影のmethodを使う低水準の入口。型付きAPIの対応・conformanceを意味しない |
| 同上 | `ValueTask CloseAsync()`、`DisposeAsync()` | `IAsyncDisposable`と`await using`。closeはflushではない |
| `ClientOptions` | `host,port,identity?,build?,applicationId?,sessionId?,allowPairing=true,pairingPollInterval?`。各値は読み取り専用 | 接続と認証の意図を一度作り、call中の変更を避ける。poll間隔の既定は2秒 |
| `ClientIdentity` | `name,version,locale="ja"`、`Name/Version/Locale` | helloとpairBeginへ同じ情報を送る |
| `BuildContext` | `dimension,x,y,z`、`Dimension/X/Y/Z` | helloでだけ建築の次元と原点を指定。後の変更methodは未投影 |
| `HelloInfo` | `Protocol/MinecraftVersion/Dimension/Origin/Json` | よく使う情報をproperty化し、未知のfieldもJsonのcopyで保つ |
| `BlockSpec` | `blockId,state?`、`BlockId/State`、`ToJson()` | 入力の部分stateをそのまま送る。省略時は空object。catalogによる補完はserverが行う |
| `BlockValue` | `BlockId/State`、`ToJson()`、`ToSpec()` | serverが返した正準IDと全stateを保持し、全stateの設置へ渡せる |
| `PairingPrompt` | `PairCode/Command/ExpiresInSeconds` | 表示に必要な情報だけをcallbackへ渡す。pairing_idとtokenは渡さない |
| `CredentialTarget` | `applicationId,sessionId,host,port`、`ApplicationId/SessionId/Transport/Host/Port`、値の等価比較 | credentialを分離するkey。Transportはtcp |
| `ICredentialStore` | `LoadAsync(target,ct)`、`SaveAsync(target,token,ct)`、`DeleteAsync(target,rejectedToken,ct)` | アプリケーションがstoreを注入できる。削除は拒否されたtokenとの一致を条件にする |
| `NoCredentialStore` | 引数なし | 既定。読み込みはnull、保存・削除は何もしない |
| `InMemoryCredentialStore` | 引数なし。同じinterface | 共有したstoreの生存中だけ保持する。diskやOS storeは使わない |

`ct`は省略可能な`CancellationToken`です。待ちに`.Result`や`.Wait()`を使わず`await`してください。
JSONは`JObject/JToken`を使います。渡したstateと返したJSONはcopyし、呼び出し側の変更で内部を変えません。
stateは文字列・有限の数値・真偽値のみ。catalog照合、stateの補完、既存stateとのmergeはclientで行いません。

## 認証とcredential

最初に保存token付き、またはtokenなしでhelloします。`auth_required`のときだけpairBeginへ進み、
同じ接続試行では一度だけpromptを呼びます。pairBeginは`token_type: session`と`client`を送ります。
承認待ちのTCPが閉じても、同じpairing_idの未送信pollは別TCPから続けられます。応答を失ったpollは完了不明で終え、期限内なら同じpairing_idの次のpollを新しいTCPで続けます。二度目のpairBeginは行いません。
成功後は新しいTCPでtoken付きhelloを行い、それが拒否されても二度目のpairingへ入りません。

`token_expired/token_revoked/token_not_found/token_invalid`では、helloに使ったtokenだけを削除し、tokenなしhelloへ戻ります。
`permission_denied/protocol_mismatch`、network障害では削除しません。
`allowPairing=false`またはcallbackなしで認証が必要な場合は`PairingRequiredException`です。

credentialのkeyは`(applicationId, sessionId, tcp, lower-case literal host, port)`です。
applicationIdの既定はclient名、sessionIdの既定はプロセス内で共通のランダムIDです。
clientのversionとlocaleはkeyに含めません。DNS別名やIPとの同一性を推測してまとめません。
再利用には同じkeyと同じstoreを明示して渡します。作例は常に`NoCredentialStore`です。
custom storeの`DeleteAsync`も、保存値がrejectedTokenと一致したときだけ削除してください。

token・pairing_id・接続先をlogへ出さないでください。Options、Target、storeの`ToString()`はそれらを表示しません。
pair codeと表示用commandは画面へ出せますが、検証の公開記録には残していません。

## 例外と通信の完了

| 例外 | 読める情報・意味 |
| --- | --- |
| `RemoteErrorException` | serverの`Code`、元の`Message`、copyした`ErrorData`、未知の値も含む`Reason`。`IsProtocolMismatch`はreasonで判定。`ServerProtocol/ClientRequires`も参照可能 |
| `ProtocolCompatibilityException` | 成功したhelloの版が非互換。`ServerProtocol/ClientRequires`を保持し、次のrequestの前にcloseする。serverのerrorではない |
| `PairingExpiredException` | clientの単調時計でexpires_inを超えた。serverの`pair_expired`と区別でき、remote codeやreasonを作らない |
| `PairingRequiredException` | 承認を始めるcallbackがない、またはpairingを無効にした |
| `ConnectionFailedException` | TCP接続を確立できず、requestを送っていない |
| `RequestNotSentException` | 閉じた接続を検出し、今回のrequestを送っていない |
| `CompletionUnknownException` | 応答待ちでEOF・socket障害・close・cancelが起きた。全pendingを回収する。Minecraft側の実行有無は不明 |

接続前・送信待ちのcancelは`OperationCanceledException`になり得ます。送信処理に入ったrequestのcancelは接続を閉じ、
ほかのpendingも含めて`CompletionUnknownException`にします。入力不正は標準の引数例外、契約に合わない成功payloadは`InvalidDataException`です。
remote例外とlocal例外を分け、localな失敗にremoteのcode/reasonを付けません。

送信はUTF-8のcompact JSON + LF。受信はLFで区切り、CRLFのCRはJSONの行末空白として受けます。
CR単独では区切りません。非JSON行、知らないid、LFのない最後の断片は応答にしません。
応答の版はmajor一致・server minor以上・patch無視で判定し、申告は`23.2.1`です。
一度送ったrequestは自動再送しません。close時に未完了requestをflushする機能もありません。

## Unityで使う場合

追加依存は`Newtonsoft.Json 13.0.4`です。NuGetの`netstandard2.0` DLLに依存し、追加の推移依存はありません。
libraryの`netstandard2.1` DLLとこのJSON DLLを、repo外のUnity 6000.6.8f1へ渡し、Editorで
hello・設置・取得・async closeまで確認しました。UPM配布、IL2CPP、mobile、Webの検証ではありません。
利用先のJSON packageと二重にassemblyを置かないでください。配布形式は後続で決めます。

内部のawaitは`ConfigureAwait(false)`を使い、Unity APIを呼びません。pairing callbackはbackground threadで呼ばれ得ます。
UnityのUI更新はmain threadの`SynchronizationContext`等へ戻してください。利用側がmain threadから普通にawaitした
後の処理は利用側のcontextに従います。main threadを`.Wait()`で止めると描画や操作が止まります。
Unity Webは直接TCPを使えないため、今回のAPIだけでは接続できません。WebSocket / Bridgeは後の範囲です。

## 待ち方と自動再接続

E2で実装した設定・通知・例外・既定値は[再接続と待機制御](e2-reconnect_ja.md)を参照してください。
`ClientOptions`で接続ごとの`ResponseIdleTimeout/AutoReconnectEnabled/ReconnectTimeout/KeepAliveTime/KeepAliveInterval/KeepAliveRetryCount`を指定できます。
`RequestAsync(method,params,new RequestOptions(timeout),ct)`で呼び出しごとのidle上限を延ばせます。
`ConnectionRestored`イベント、`ConnectionGeneration`、`KeepAliveTuningSupported`を公開します。
再接続前に送ったrequestは再送せず、再接続で認証が必要ならpairingを始めず`RequestNotSentException`で止まります。
