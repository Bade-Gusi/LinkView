# LinkView 信令协议 v1

传输：WebSocket 文本帧；UTF-8 JSON；单条最大 64 KiB；建议上限 40 条/秒/连接。
版本：`v = 1`。不兼容变更必须递增版本；服务端拒绝其他版本。

## 1. 信封

```json
{
  "v": 1,
  "id": "8d3f...（≤64 字符）",
  "type": "sdpOffer",
  "sessionId": "a629...",
  "ts": 1761220000000,
  "payload": { }
}
```

- `type` 使用 camelCase 字符串。
- `ts` 为 Unix 毫秒，仅用于观测；认证时间戳单独在 payload 中校验。
- `sessionId` 在会话内消息中必填；服务端会校验参与者身份。
- 未知字段被忽略；未知 `type` 返回 `invalidMessage`。

## 2. 消息

| type | 方向 | payload 关键字段 |
|---|---|---|
| `hello` | C→S | `deviceId, deviceName, deviceType("windows"/"android"), publicKey(Base64 SPKI), appVersion, protocolVersion` |
| `authChallenge` | S→C | `nonce, expiresAtUnixMs` |
| `authProof` | C→S | `signature(Base64 DER), clientTimestampUnixMs` |
| `authOk` | S→C | `self{deviceId,deviceName,deviceType,appVersion,observedAddress}, heartbeatSeconds` |
| `pairCreate` | C→S | `ttlSeconds`（服务端钳制 60–3600） |
| `pairCreated` | S→C | `sessionId, pairingCode, expiresAtUnixMs` |
| `pairJoin` | C→S | `pairingCode` |
| `joinRequest` | S→C(host) | `sessionId, guest{...}` |
| `pairDecision` | C(host)→S | `accept, reason?` |
| `pairJoined` | S→C | `sessionId, peer{...}, role("host"/"guest")` |
| `peerLeft` | S→C | `reason` |
| `sdpOffer` | C→S→C | `sdpType:"offer", sdp`（≤32 KiB） |
| `sdpAnswer` | C→S→C | `sdpType:"answer", sdp`（≤32 KiB） |
| `iceCandidate` | C→S→C | `candidate`（≤2048 字符）、`sdpMid?`、`sdpMLineIndex?`、`usernameFragment?` |
| `sessionEnd` | C→S→C | `reason` |
| `ping` / `pong` | 双向 | `clientTimeMs` / `clientTimeMs, serverTimeMs` |
| `error` | S→C | `code, message, retryAfterMs?` |

错误码：`invalidMessage, unsupportedVersion, unauthorized, authFailed, authExpired, rateLimited, payloadTooLarge, pairingInvalid, pairingExpired, pairingLocked, sessionNotFound, sessionFull, notInSession, peerUnavailable, internal`。

## 3. 认证

规范化待签名字符串（UTF-8）：

```
LinkView-Auth-v1
{nonce}
{deviceId}
{clientTimestampUnixMs}
```

- 算法：ECDSA P-256 + SHA-256；签名格式：DER，Base64（无换行）。
- `deviceId = "lv" + lowercase_hex(SHA-256(SPKI)[0..15])`。
- 服务端检查：
  1. `deviceId` 与公钥推导值一致；
  2. SPKI 可解析且长度 64–2048 字节；
  3. 时间偏差 ≤ 120 秒；
  4. 挑战未过期、未使用；
  5. 签名验证通过。
- 每个挑战只接受一次 `authProof`；失败后需要重新 `hello`。

## 4. 配对流程

```
Host                      Server                        Guest
 |-- hello -------------->|                              |
 |<-- authChallenge ------|                              |
 |-- authProof ---------->|                              |
 |<-- authOk -------------|                              |
 |-- pairCreate --------->|                              |
 |<-- pairCreated --------|                              |
 |                        |<------------- pairJoin ------|
 |<-- joinRequest --------|                              |
 |-- pairDecision(true) ->|                              |
 |<-- pairJoined ---------|----------- pairJoined ----->|
 |-- sdpOffer ----------->|----------- sdpOffer -------->|
 |<-- sdpAnswer ----------|<---------- sdpAnswer --------|
 |<-> iceCandidate (trickle, 双向转发)                     |
 |-- sessionEnd --------->|----------- sessionEnd ------>|
```

- 服务端在 `pairJoin` 成功后移除该配对码的可加入性；重复加入返回 `pairingInvalid`。
- 宿主拒绝：加入方收到 `peerUnavailable`，会话删除。
- 会话关闭后任何 SDP/ICE 转发返回 `notInSession`。

## 5. 服务端校验与限制

- 未认证连接只能发送 `hello`、`authProof`、`ping`。
- 已认证但不在会话中的 SDP/ICE 被拒绝。
- 会话消息只能由两个参与者发出，不能跨会话转发。
- `sdpType` 必须与消息类型一致；SDP 非空且 ≤32 KiB。
- ICE 候选非空且 ≤2048 字符。
- 消息大小由连接接收缓冲与解析双层限制；超限 → 关闭（1009）。
- 二进制帧 → 拒绝（1003）。
- 单连接令牌桶限流；超限返回 `rateLimited`。
- 会话最长 12 小时；配对/协商超时由后台清理器处理。
- 连接心跳 180 秒；清理周期 30 秒（可配置）。

## 6. 隐私

- 服务端不解析或记录 SDP 内容；日志只记录消息类型、会话 ID、结果。
- 服务端不转发/存储媒体；配对码不以明文写入日志。
- `observedAddress` 是服务端看到的来源 IP（无端口），仅发给对端用于展示/诊断。
- 信令日志不包含公钥、签名或配对码。

## 7. 版本兼容

- 协议字段保持向后兼容的追加式演进；破坏性变更需新版本端点或递增 `v`。
- 当前 Android 与 Windows 客户端都发送 `v=1`，消息名/字段逐一对应。
