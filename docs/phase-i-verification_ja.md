# Phase I：最初のTCP sliceの検証記録

対象は一般用途C# Client Libraryの`connect → hello → protocol互換判定 → 必要なら初回pairing → setBlock → getBlock → close`です。
申告protocolは`23.2.1`。この文書は実装・検証の記録であり、Protocolの新しい正本ではありません。

## 参照と取得元

- knowledge：`076a127a22eaf0188c1d13822bfd55bfeee9ec38`。
  [依頼票](https://github.com/Naohiro2g/mc-remote-knowledge/blob/076a127a22eaf0188c1d13822bfd55bfeee9ec38/00-hub/polyglot-csharp-start-instructions_ja.md)、
  runtime、18-csharp-client、wire-format §§2/3/3.4/4/6/6.5/7.1/7.3、versioning §8、block-value、Protocol Inventory、sample学習UXを参照。
- fixture：`Naohiro2g/minecraft-remote-tooling@36ebe9e63e4f0afcf949eae998832f68209b8119`の
  `packages/protocol/test/fixtures/`。同commitのREADMEでscripted peerの形を確認。
- copyのfile別bytes・SHA-256は[PROVENANCE.json](../tests/MinecraftRemote.Client.Tests/Fixtures/PROVENANCE.json)。
  MITのLICENSEを同じディレクトリへcopyし、実装codeには依存していない。
  schemaと`block-value-v22.json`もbyte単位で保持。block valueの`value_refs`は参照先を解決して確認する。
  block-valueの18値ベクトル全体を別suiteとして実行した、という意味ではない。

## 共有fixtureと追加テスト

| file（`client-bootstrap-…-v23.2.1.json`） | case数 | PASS | skip |
| --- | ---: | ---: | ---: |
| framing | 7 | 7 | 0 |
| hello | 10 | 10 | 0 |
| pairing | 10 | 10 | 0 |
| blocks | 12 | 12 | 0 |
| errors | 3 | 3 | 0 |
| close | 6 | 6 | 0 |
| 合計 | 48 | 48 | 0 |

fixtureごとに独立したxUnit caseを持つ。framingは実際のbyte decoderへ投入し、対話はloopback TCPのscripted peerで
送受信・id・結果・error・pending・credential操作を確認する。idの照合だけでなく実装したtransportを通す。

最初に未実装stubで48ケースが意図した未実装の理由で落ちることを確認した。初期suite全体は54失敗・2成功、
実装後56成功。追加検証で非有限数がJSONの文字列へ化ける問題を検出して修正した。
最終suiteは71成功・0失敗・0skip。共有48件のほかに、公開ConnectAsyncでの認証経路、型付きblock API、
互換でないhello後のclose、client側期限、EOFとcancelで全pendingの回収、credential分離・条件付き削除、
承認待ちTCP切断後のpoll継続などを含む。

再現コマンドはCIと同じ。

```sh
mise exec -- dotnet restore MinecraftRemote.Client.sln
mise exec -- dotnet build MinecraftRemote.Client.sln --configuration Release --no-restore
mise exec -- dotnet test MinecraftRemote.Client.sln --configuration Release --no-build
```

## 実McRemoteとのpulse（live-auto相当）

- 日付：2026-11-12（JST）。
- Paper：`1.21.11-132-ver/1.21.11@c5eb079`、Minecraft `1.21.11`。human ownerの起動logで確認。
- McRemote：公開版`2321.1.0`。human owner提供のplugin起動logで確認。
- script：[examples/FirstBlock/Program.cs](../examples/FirstBlock/Program.cs)。接続先と位置は環境変数で手元だけに設定。
- 認証：tokenなしhello → auth_required → session pairing。表示commandをhuman ownerがMinecraft内で承認。
  tokenやpairing_idを公開せず、`NoCredentialStore`で保存しない。
- 結果：hello → gold block設置 → IDと空stateの読み戻し一致 → closeを完走。

公開するsummaryは次のみ。

```text
HELLO_PASS
MC_VERSION=1.21.11
PULSE_PASS: hello / setBlock / getBlock
CLOSE_PASS
```

実worldの1ブロックを変更した。human ownerの「どこでもよい、復元不要」の指示で残した。
片付けはMinecraft内で金ブロックを取り除くか置換する。再実行時にIDとstateを戻すには`--restore`を付ける。
block entity等までの完全復元ではない。詳細は[作例の手順](../examples/FirstBlock/README_ja.md)。

最初の試行では承認待ちにpre-auth TCPが閉じ、pollを送る前に停止した。wire §6.5の、authで同一TCPを要求しない性質に従い、
同じpairing_idの未送信pollだけ別TCPへ送る処理と回帰テストを追加した。その後、新しい人の承認で上記pulseを完走した。
送信済みrequestの再送、二度目のpairBegin、通常の自動再接続は追加していない。

## APIとUnity

公開する型・method・例外・storeの一覧と選択理由は[Client API](client-api_ja.md)。
`async` / Task、CancellationToken、IAsyncDisposableで.NETの利用方法へ合わせた。
JSON copyと構造化BlockSpec/BlockValueで部分state入力と正準全state出力を分ける。

依存は`Newtonsoft.Json 13.0.4`、使用assetは`netstandard2.0`（推移依存なし）。
repo外のUnity `6000.6.8f1`へ実際の`netstandard2.1` library DLLとJSON DLLを渡し、
Editorのbatchmodeで別のloopback TCP peerに対するhello・setBlock・getBlock・async closeを実行した。
確認markerは`CSHARP_UNITY_TCP_COMPAT_PASS`、Editor終了code 0。
Unity ProjectやEditorのlogはこのrepoへ追加していない。

これはEditorでのDLL互換確認。IL2CPP / mobile / Web / UPM配布は対象外。
callbackのthread、main threadへ戻す必要、Unity Webの直接TCP不可についてはAPI文書へ記載した。

## TypeScriptからの確認項目

| 項目 | C#での扱い |
| --- | --- |
| `auth.pairBegin.client` | helloと同じname/version/localeを必ず送る |
| F1-AU-02とclientの期限 | fixtureは低水準requestで期限後pollのserver `pair_expired`をそのまま検証。公開ConnectAsyncはpoll前に単調時計を見てlocal `PairingExpiredException`。両方を別テストにし、remote errorを捏造しない |
| 行末CR | LFでframe化。CRLFのCRはJSONの末尾空白として受理。JSON文字列中のCRを削らず、CR単独は区切らない |
| credential key | applicationId + sessionId + tcp + 小文字化したliteral host + port。version/localeとDNS aliasの推定は含めない |
| 認証reason後のhello | F1-AU-08〜10を含め、invalid tokenの条件付き削除 → tokenなしhelloを検証。拒否されたpairing後helloで二度目のpairingをしない |
| client互換判定後close | HE-04/05を使うC#のunit testでEOFと追加requestなしを確認。共有fixtureの更新待ちでskipしない |

AU-02はこのsliceの指定どおり変更せず通した。clientによる期限切れの別経路を表す共通fixtureは今後のtooling追補と照合する。
今回、Python / Java / TypeScriptの実装codeを調べて翻訳していない。入力はcontract、共有fixture、knowledgeに返却された知見。
他言語実装を実査して新しいProtocol差分を発見した、という主張はない。

## 残した範囲と返却

通常の待ち時間の監視と、切断後の次の呼び出しでの自動再接続は今回未実装。
timeout設定・自動再接続の有効無効・再接続通知のAPI案を[Client APIの末尾](client-api_ja.md#待ち方再接続後続apiの提案)へ記載した。
具体的な既定秒数、Unity配布形式、OS store、Web経路、NuGet公開は決めていない。
Protocolで新たに決める必要があるcaseをclient独自の判断で確定していない。

E1の列の案は[polyglot-e1_ja.md](polyglot-e1_ja.md)。raw requestで呼べるだけのmethodは投影「あり」に数えない。
knowledge本文・Protocol Inventoryへの転記とfixture pinの更新はcoordinatorが行う。
