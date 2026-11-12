# E2：待ち方と自動再接続

knowledge `125812b47bae32120f56febc4d30058ab0fe8248`の
[依頼票](https://github.com/Naohiro2g/mc-remote-knowledge/blob/125812b47bae32120f56febc4d30058ab0fe8248/00-hub/polyglot-e2-reconnect-instructions_ja.md)の共通・C#節に対応する。
libraryはnetstandard2.1、namespaceとNewtonsoft.Json依存は従来のまま。

## 設定と既定値

| API | 既定 | 意味と選択理由 |
| --- | --- | --- |
| ClientOptions.ResponseIdleTimeout | 15分 | pendingがある間の最後の受信から測る。長い建築待ちで短時間のtimeoutを感じにくくする暫定値 |
| RequestOptions.ResponseIdleTimeout | 呼び出し側で指定 | RequestAsyncの第3引数。接続の既定より延ばせる。最初のpending時に時計を始め、受信byteごとに待ち直す |
| ClientOptions.AutoReconnectEnabled | true | 切断後の次の呼び出しが新しいhelloを開始。falseなら未送信として返す |
| ClientOptions.ReconnectTimeout | 5分 | 失敗後にretryを続ける時間の窓。dialやhello自体の所要時間を含む厳密なwall-clock deadlineではない |
| ClientOptions.KeepAliveTime | 2分 | TCPのidle probeを始めるまで。通常requestのidle timeoutとは独立 |
| ClientOptions.KeepAliveInterval | 30秒 | probe間隔 |
| ClientOptions.KeepAliveRetryCount | 10 | 一時的なWi-Fi切断に長く耐えるための回数。目安は約7分で15分の応答上限より短い |

値は運用実測済みの推奨値ではなく、長く待つ方針から選んだ初期値。実測したのはloopbackでの状態遷移と仮想時計の境界であり、
長時間の建築やWi-Fi切断の耐性を実機で測ったとは主張しない。接続ごとに延長できる。
通常idleの監視周期は1秒。pendingなしの無通信では応答timeoutを発火させない。

TCP keepaliveは有効。新しい詳細option名はnetstandard2.1の参照enumに無いので、実行環境のenumを解決して設定する。
.NET 10 / Linuxでは詳細設定まで成功した。runtime/OSが詳細を受け付けなければ基本keepaliveとOS既定値になり、
`MinecraftClient.KeepAliveTuningSupported`がfalseになる。browser経路はまだ実装しておらず、browserのpingは提供しない。

## 呼び出しと失敗

```csharp
var options = new ClientOptions(host, port,
    responseIdleTimeout: TimeSpan.FromMinutes(30), autoReconnectEnabled: true);
await using var client = await MinecraftClient.ConnectAsync(options, onPairing: ShowPairingAsync);
var result = await client.RequestAsync("world.getBlock", new JArray(x, y, z),
    new RequestOptions(TimeSpan.FromMinutes(45)), cancellationToken);
```

上のhost/port/座標とShowPairingAsyncはcallerが用意する。通常の型付きSetBlockAsync/GetBlockAsyncも自動再接続を使う。
応答を受ける前の切断・idle超過は全pendingを`CompletionUnknownException`で回収し、その接続を閉じる。
送ったrequestは再送しない。次の呼び出しが再接続を試みる。

再接続できなかった呼び出しには`RequestNotSentException`を返す。`Cause`は
ConnectionFailed / PairingRequired / IncompatibleProtocol / ReconnectDisabled / Closed。
これはlocalな分類で、wireのcode/reasonではない。再helloのremote errorや版不一致はInnerExceptionに保持する。
自動pairingは行わない。必要ならapplicationが新しいConnectAsyncを開始して承認を依頼する。
invalid tokenは対象を削除し、tokenなしhelloへ戻る。pairing済みtokenはnone storeを選んだ接続でも、そのclientの生存中だけ再接続へ使う。

## 再接続の状態とthread

- 最後に成功を確認したdimension/originを復元する。初期値はhello result。raw RequestAsyncでの成功したbuild.setDimension/setOriginの結果を追う。
  応答を失ったsetterは復元も再送もしない。
- helloのprotocol互換判定を再実施し、catalogHash/server/sessionの前後を比較する。
- 1つのclientを複数threadから共有できる。pendingのidと送信を同期し、重なる再接続は同じTaskを共有する。
  同じ接続のcommandは同じFIFOへ並ぶため、大きい建築の後ろの読み取りは待つ。
- retryの基準間隔は500msから指数的に30秒まで延長し、各回0.5〜1.5倍のjitterを付ける。実待ちは最大45秒。
  乱数注入の境界をテストし、250ms→1500msの異なる係数・増加を仮想時計で確認した。
- 何もしていない間のEOF/socket errorはreaderが接続状態へ反映する。keepaliveが検出したerrorも同じ経路。
- CloseAsync/DisposeAsyncは自動再接続を止める。閉じたclientは再利用しない。

`ConnectionRestored`はPrevious/CurrentのHelloInfo、Generation、EventsLost（true）、ServerChanged、SessionChanged、CatalogChangedを渡す。
`ConnectionGeneration`で、最後に確認した操作から接続が変わったかを見られる。通知は新helloの成功後、次のcommandを送る前。
通知handlerの同期例外は通信を壊さないよう伝播させない。handlerは短い処理にし、Result/Waitで止めない。

Unityではmain threadで取得したcontextへ戻す。library内部はUnity APIを呼ばない。

```csharp
var unityContext = SynchronizationContext.Current; // Unityのmain threadで取得
client.ConnectionRestored += (_, change) =>
    unityContext.Post(_ => UpdateConnectionUi(change.Generation, change.EventsLost), null);
```

UpdateConnectionUiは利用側のUI処理。pairing callbackにも同じ方法を使う。background threadからUnity APIへ直接触れない。
entity handle、event cursor、catalog cacheの専用APIは未実装。これらのepoch状態の失効管理は機能追加時に対応する。
FAST/notificationとflushも未実装で、旧接続のFASTを新flushが保証するAPIはまだ提供していない。

## fixtureと検証

取得元をtooling `d2691cdafb3b7548a4c26960184266c44bfcbd93`から
`335c604f859d6dfad04f0434bfa4eb6445761430`へ更新した。
11 filesのbytes/SHA-256は[PROVENANCE.json](../tests/MinecraftRemote.Client.Tests/Fixtures/PROVENANCE.json)。MIT LICENSEを含むbyte一致copy。
以前のglobへoperationsを混ぜず、v1とv2を明示して読む。追加されたAU-11/12も含めv1は54/54。

| suite | case | PASS | todo |
| --- | ---: | ---: | ---: |
| framing | 7 | 7 | 0 |
| hello | 12 | 12 | 0 |
| pairing | 12 | 12 | 0 |
| blocks | 12 | 12 | 0 |
| errors | 3 | 3 | 0 |
| close | 8 | 8 | 0 |
| operations v2 | 12 | 10 | 2 |

v2のPASSはOP-01〜05、07〜10、12。OP-06はFAST/flush、OP-11はhandle/cursor/catalog cacheの専用機能が無いためtodoとし、合格に数えない。
runnerはapplication callを開始するだけで応答を待たず、clientが発する接続・送信を実TCPで観測する。
仮想時計を進める操作と、network/taskをsettleする待ちを分ける。settleで仮想時間を勝手に進めない。
時計の境界、全pending回収、再送なし、build復元、未送信の原因、通知、再helloの互換判定を、fixtureに書かれた欄だけ比較する。
生存確認とsocket errorはtransportの判定境界を注入し、probeの実環境耐性を合格としない。

共有fixture外では、AU-12後に同じpairing_idでpoll→承認→token helloまで続ける流れ、OS threadのBarrierから同時に始める再接続、
呼び出しごとのidle延長、backoffの乱数境界を追加した。全suiteは92 PASS、skip0、build警告0。
旧動作相当へ戻す比較では再接続・idle回収・poll継続の不足で11件中8件が失敗し、実装を復元してgreenを確認。
これは比較実行の記録であり、実装前に全追加ケースを順にred確認したとの主張ではない。

## 実McRemote pulse

2026-11-13 JST。Paper 1.21.11-132-ver/1.21.11@c5eb079、McRemote公開版2321.1.0。
human ownerが前回と版は同じと確認。FirstBlock作例で、人がMinecraft内でsession pairingを承認し、
hello→gold block設置→ID/空stateの読み戻し→closeを完走（終了code0）。credentialは永続化しない。

```text
HELLO_PASS
MC_VERSION=1.21.11
PULSE_PASS: hello / setBlock / getBlock
CLOSE_PASS
```

片付けは前回の復元不要の指示に従って残した1ブロックを、Minecraft内で除去・置換する。
再実行時の部分復元は作例の--restore。block entity等の完全復元ではない。
Paperの再起動は行っていない。通常の切断後の再接続について、今回はdeterministic TCP testの観測を返し、実機で再起動後の再接続を見たとは主張しない。
EOF後の次pollは同じpairing_idの継続で、完了不明となったpollの送り直しにはしない。二度目のpairBeginは行わない。

Protocol Inventoryの共有fixture行は、以前の「v1 52のみ」から「v1 54すべて、v2 10 PASS/2 todo、schema2本・block値参照・LICENSE」へ更新する案。
新しいwireの判断や、機能の無いepoch cacheの仕様はこのclient独自に決めていない。
repo外のUnity 6000.6.8f1でも更新DLLによるTCP hello/set/get/closeを確認した（Editor終了0）。IL2CPP/mobile/Webの検証ではない。
