# E1：C#列の返却案

基準はknowledge `076a127a22eaf0188c1d13822bfd55bfeee9ec38`のProtocol Inventory。
申告protocol `23.2.1`、最初の直接TCP slice。対応の「あり」はAPIへの投影であり、全reason・全実機経路の検証を意味しない。
実装と共有48ケースの範囲は[検証記録](phase-i-verification_ja.md)、API一覧は[Client API](client-api_ja.md)。
一般の`RequestAsync(method, params)`だけで呼べるmethodは、個別の投影として数えない。

## method

| method | C#列案 |
| --- | --- |
| `hello` | あり：ConnectAsync。protocol/client/build/authを送る |
| `connection.flush` | なし |
| `build.setDimension` | なし |
| `build.setOrigin` | なし |
| `world.setBlock` | あり：SetBlockAsync。id付きrequest、成功はnull |
| `world.setBlocks` | なし |
| `world.getBlock` | あり：GetBlockAsync → BlockValue |
| `world.getBlocks` | なし |
| `world.getHeight` | なし |
| `world.getSign` | なし |
| `world.setSign` | なし |
| `world.updateSignLine` | なし |
| `player.getPos` | なし |
| `player.setPos` | なし |
| `player.getPose` | なし |
| `player.setPose` | なし |
| `player.getDirection` | なし |
| `player.setDirection` | なし |
| `world.spawnEntity` | なし |
| `world.getNearbyEntities` | なし |
| `entity.getPose` | なし |
| `entity.setPose` | なし |
| `entity.getDirection` | なし |
| `entity.setDirection` | なし |
| `entity.remove` | なし |
| `world.spawnParticle` | なし |
| `world.playSound` | なし |
| `world.playBlockSound` | なし |
| `world.strikeLightning` | なし |
| `chat.post` | なし |
| `events.poll` | なし |
| `catalog.get` | なし |

## auth

| method | C#列案 |
| --- | --- |
| `auth.pairBegin` | あり：ConnectAsync内。client必須、session token、prompt callback |
| `auth.pairPoll` | あり：ConnectAsync内。pending待機、server期限とclient期限を区別 |
| `auth.listCredentials` | なし |
| `auth.revoke` | なし |
| `auth.logout` | なし |

## methodでない能力

| 能力 | C#列案 |
| --- | --- |
| TCPのframing／JSON-RPC | あり：UTF-8+LF、分割と結合、id照合、非JSONと未知idの破棄、末尾断片を破棄 |
| WebSocket／browser | なし |
| protocol negotiation | あり：23.2.1申告、major一致・server minor以上・patch無視。不一致は追加request前にclose |
| authentication／pairing | あり：hello先行、auth_requiredだけpairing、1試行1回、invalid token後はtokenなしhello |
| session credentialのUX | あり：注入store、noneとin-memory、target分離、拒否tokenだけ削除。OS storeなし |
| request／notification／flushと順序 | 部分：id付きrequestを順に送信し、応答を待てる。notification / FAST / flushなし |
| events cursor／loss | なし |
| error reason | あり：code/message/data/未知reasonを保持。protocol_mismatchはreason判定。local errorを別型化 |
| build context | 部分：helloのBuildContextだけ。変更method未投影 |
| BlockValue／BlockSpec | あり：構造化ID/state。入力部分stateはserver補完、出力全stateをToSpecで再利用 |
| catalog／constants | なし：helloのhashはJsonで保持するだけ。catalog取得・cache・生成定数なし |
| helloの`world_constants` | 部分：HelloInfo.Jsonでopaqueに保持。専用propertyや生成定数なし |
| epoch／reconnect | 部分：close時に全pendingを完了不明で回収。明示した次のConnectAsyncは新しいhello。epoch依存APIと通常の自動再接続なし |
| WireScopeのobserver | なし |

## このsliceでのfixture接続

F1の6本・48ケースすべてをCIへ接続。schema、block-value（value_refsの範囲）、MIT LICENSEも同commitからcopy。
file別bytesとSHA-256は[PROVENANCE.json](../tests/MinecraftRemote.Client.Tests/Fixtures/PROVENANCE.json)。
ほかの静的fixtureはこのsliceの機能外で、copyしていない。
待ち方と再接続の後続API案は実装済みとして数えない。
