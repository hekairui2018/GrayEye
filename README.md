# 灰eye A6L只读采集发送端

当前版本：0.2.1-test.20261022。运行GrayEye.exe。使用Windows自带.NET Framework，源代码使用系统csc编译，无第三方运行依赖。

本版权威对接说明是《灰eye任务事件_接收端对接.md》；0.1.0旧Bearer整包协议文档只作为历史资料，不用于新接入。参考调试助手任务事件机制，但来源、专用凭据、业务主表准入及异常表必须独立对齐。

构建：在此目录执行 `powershell -NoProfile -File build.ps1`。生产构建仅包含Collector.cs、TaskEvents.cs与Target.cs，不包含ProtocolTests.cs或任何模拟接收端。

ProtocolTests.cs为独立测试入口，用csc将Collector.cs、TaskEvents.cs、Target.cs及该文件一起编译，指定 `/target:exe /main:ProtocolTests`，引用与build.ps1相同的框架程序集及fields.map资源；运行参数依次为全新隔离测试输出目录、--synthetic（生成纯本机模拟夹具），或用户自行提供的已保存回传目录（包含response_*.bin与result.json）。测试只回放离线证据和使用本机回环HTTP，不读真机、不连正式接口、不写飞书。

任务原件、收件回执、任务日志分开保存。同任务重试保持身份；新操作创建新任务。auto_send默认关闭，receiver_ready默认关闭，绝不内置其他助手地址或凭据。旧0.1.0原始包不自动转换补传。

当前A6L固定只读命令、地址、身份和133字段映射来自先前真机核对。未知数据原样保留，不生成可恢复SSR。硬件流程没有配置下发、升级、显示模式切换或鼠标模拟入口。

公开包不带任何设备绑定、密钥、真实参数或日志。首次使用先在本机配置授权A6L，参考target.example.json，保存为%LOCALAPPDATA%/A6LCollector/target.json，然后重启程序。未配置时不会读取任何设备。仅支持已验证的A6L网口1卡1读取，不做型号自动适配。
