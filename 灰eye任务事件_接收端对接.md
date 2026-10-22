# 灰eye发送端已适配：交给接收端开发助手

参考用户提供的 `调试助手 TASK_COLLECTION_LOGS.md`，本客户端从0.2.0起使用任务事件；0.1.0的Bearer＋整包请求草案已废止。不要再按旧文档实现。

请为灰eye接入“业务采集主表＋内部异常表”。只在独立环境联调，先不修改调试助手、远程助手现网，也不往正式表写模拟数据。

## 与发送端对齐

- POST HTTPS `/api/huidu/log`，请求头 `X-Upload-Key`。没有硬编码现网地址、既有助手密钥或飞书凭证。
- 固定来源 `source: "grayeye"`，业务协议 `business_protocol: "grayeye.collection.v1"`。请配置专用凭据，将凭据与来源绑定，按此路由到灰eye自己的两表；不能只信任客户端source，更不能默认写调试助手现有表。
- 基础事件含 `task_protocol:1`、`task_run_id`、`machine_id`、唯一 `event_id`、递增 `event_sequence`、`event_kind`、`log_date`、`content`。附 `client_ts`、`app_version`、`attempt`、`task_state`、`execution_mode`。
- `event_kind`沿用已存在的 `snapshot` 和 `exception`，没有伪造 `receiver_checkpoint`、`receiver_upgrades`、升级目标版本或升级成功字段。
- 新任务先生成task_run_id并落开始快照；显式“重试上次任务”沿用ID、序号递增；新一次独立采集使用新ID。每次尝试的独立参数包由attempt_id区分。
- machine_id为本机持久随机标识，不按设备、日期或机器合并不同任务。任务日志从本任务起点记录，不上传全天其他任务正文。

## 业务完成判据（接收端需要新增灰eye分支）

完成事件 `event_kind:"snapshot"`、`task_state:"completed"`，带：
```
collection: {
  attempt_id: "本次尝试ID",
  completion_verified: true,
  checkpoint_source: "grayeye_receiver_parameters_crc_and_archive",
  restoration_verified: false,
  manifest: {task_run_id: "同任务ID", success:true, received_blocks:11,
             crc_valid:true, verified_fields:133, parameters:{...}, ...},
  package_sha256: "ZIP的SHA256",
  package_base64: "包含原始回传和CSV的ZIP"
}
```
请校验本任务及尝试归属、包hash和包内manifest一致，重新检查11个不同的参数段，不能只信success布尔值：

- 每段533字节，签名字节为 `55 55 55 55 55 55 55 d5`。
- 字节15–16大端载荷长度512；CRC32覆盖原帧 `[8:-4]`，末尾4字节小端存放CRC。
- 键在字节12–14，须完整覆盖010000、020000、030000及040000至040007。
- 必须具备完整原始包、CRC证据和本次目标归属；缺段、CRC错或归属错不能准入主表。
- 原始包同时保存发送设备/连屏信息和接收卡表格；字段原值不等于已确认物理单位。
- 异常后的后续成功快照可以进入主表，先前自动异常继续留内部异常表。较旧序号晚到不能抹掉较新完成证据。不同尝试的未完成片段不能拼成一次完整采集。

当前真实范围只有本机配置并授权的A6L、网口1卡1。卡号是目标，不是全网在线数量。未读取的接收卡型号/版本、发送卡应用/FPGA版本均null且明确待确认。HDSet软件版本来自本机文件版本。网口2–4的现场状态由本机授权配置记录，未配置则待确认，不能当成本次自动探测结果。设备名/型号基线、IP/MAC核对、用户目标分别注明来源。`site_environment.customer_issue_resolved`为null，采集完成不等于客户问题解决。

## 内部异常及中断

- 自动读设备、解析或归档失败：`event_kind:"exception"`、`origin:"automation"`，携带 `node`、`message`、任务日志及设备快照，收到的部分原始数据尽量随collection保留。
- 用户停止：exception + origin human，仅留操作事实，不准算自动故障。
- 进程异常退出后的恢复：exception + origin unknown，不擅自归因自动故障。
- HDSet正在占用或身份未确认属于前置条件不满足：snapshot + task_state blocked，只归档，不写成程序自动故障。
- 日志上传失败只改变回传状态，不生成硬件自动异常，不更改已完成采集结果。

## 保存、补传与回执

每个事件先原子刷盘，落在 `%LOCALAPPDATA%/A6LCollector/tasks/<task_run_id>/events/0000000001.json`。原件保留不删，重发直接读取原JSON字节。接收凭据独立位于receipts目录，保存请求SHA256和服务端完整回执。

请延续既有两表存储规则：按机器/任务/事件ID/序号幂等；冲突409且不覆盖；先保存原件再响应durable；两表独立同步及重试；保护人工备注和人工清空。灰eye附加字段必须保留并进入有效内容指纹，不能被旧白名单丢弃。ZIP解析限制大小/数量、校验路径、防解压炸弹；附件失败不得丢原包。

客户端接受回执格式：
```
{
  "accepted": true,
  "durable": true,
  "task_run_id": "同任务ID",
  "event_id": "本次事件ID（可选，有则必须匹配）",
  "sync": {"success":"queued", "failure":"not-applicable"},
  "records": {"success":null, "failure":null}
}
```
sync键沿用现有success/failure，但success在灰eye中表示业务采集主表，不表示升级或客户问题已解决。状态沿用queued/synced/configuration-required/not-applicable，失败也可以failed。只有各适用表完成真实写入和回读后才能synced，并返回真实行ID。

客户端收到durable只标已接收；两表均synced才显示两表同步完成；仅一表适用则明确另一表不适用。积压任务按各任务状态汇总，不能拿最后一个成功回执代表全部完成。

对已接受但queued的任务，客户端会重复提交同一个最后事件查询最新回执，不生成新ID或新序号。接收端仍负责durable之后的飞书重试，重复提交必须幂等。缺durable、任务ID不匹配、非2xx或网络失败，客户端不写接受凭据并保留补传。

## 配置及联调要求

客户端 `%LOCALAPPDATA%/A6LCollector/settings.json`：
```
{"endpoint":"", "upload_key":"", "receiver_ready":false, "auto_send":false}
```
请完成来源、专用凭据和两表路由后，安全交付配置方式；不要在聊天、日志或仓库里展示密钥。endpoint须HTTPS且路径/api/huidu/log。只有双方确认后才设receiver_ready=true。旧token字段不会当成X-Upload-Key使用。

auto_send=true后，程序启动、每60秒及真实采集收尾尝试补传；失败事件也参与。默认false时可send手动补传。admin导出桌面日志副本并复制到剪贴板，源日志保留。旧0.1.0参数包保留本地，不自动伪造成新协议历史任务。

发送端已做本机隔离回归，包含正常、异常、异常后恢复、人工停止、重复、乱序、断网及重启补传、字节不变、HTTP头、错误回执、模拟禁发。它们使用模拟接收存储和回环HTTP，不代表你这边的实际双表准入已完成。

请接收端交付：灰eye路由及专用凭据配置方式、两张目标表、准入适配代码、上述场景的隔离验证，以及第一条真实灰eye任务的任务ID、原包hash、两表真实适用状态和飞书回读结果。没有真实回读证据前不要说飞书已打通。

公开包不包含目标设备信息。首次启动后通过admin的“配置授权A6L”设置本机target.json并重启。空白配置不会触发硬件通信。此文件和所有运行配置、日志、回传参数均禁止提交公开仓库。
