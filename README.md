# 随译 v0.1.1

个人使用的 Windows 聊天翻译客户端。C# / .NET 10 WPF，默认即时聊天风格，支持中文、English、Filipino / Tagalog。没有账号、历史、自动发送、剪贴板监听或后台翻译。

## 直接启动

解压 `artifacts/Suiyi-v0.1.1-win-x64.zip`，保持整个 `Suiyi-v0.1.1-win-x64` 目录完整，双击 **Suiyi.exe**。Windows x64 自包含版本，无需安装 .NET。便携版本是免安装程序；非敏感设置保存在 `%LOCALAPPDATA%\Suiyi\settings.json`。

升级时先从旧版托盘菜单「退出」，再启动新版；密钥只存在旧进程内存中，需要在新版重新输入。源码与非敏感设置不需要删除。

- **Ctrl+Alt+T**：全局唤起；在「设置」修改，保存时检测冲突，失败保留旧组合。
- **Ctrl+Enter**：翻译。Enter 正常换行；中文输入法组合过程中不触发翻译。
- **Esc / 关闭 / 最小化**：收回系统托盘并取消未完成请求。托盘双击打开，右键「退出」结束进程。
- 「取消」「清空」及修改输入 / 选项使旧请求失效；新请求不会被旧响应覆盖。「重试」使用当前文本和选项重新发起。
- 中文输入默认显示优化中文和英语；「复制译文」默认复制英语正文。手选其他目标时复制该目标语言。段序号是界面元素，不进入复制正文。
- 原文语言可自动或手选；目标为默认方向或中 / 英 / Filipino。自动检测采用本地字符和词表启发式，Taglish、中英混合和短 Filipino 消息建议手选。
- 「术语表」每行 `原文 = 首选表达`；最多 100 条，每项最多 100 字。不需要保存翻译历史。

## 模拟与真实 API

没有密钥时，顶部明确显示**模拟模式**。只为用户提供的四个固定样例及部分手选语言方向演示结果；其他文本显示模拟占位。这些结果不能作为真实翻译质量验收。单次输入上限 20,000 字符。

在客户端「设置」中填写完整 HTTPS 接口地址、服务提供商支持的模型名及 API Key。默认地址为 `https://api.openai.com/v1/chat/completions`，模型名留空，需按你账户的实际权限填写。**不要把密钥发给聊天助手。** 首版仅内存会话保存，退出后清除，不提供记住密钥。设置文件不包含密钥、输入正文或译文；程序不写正文和密钥日志。

首次真实请求、每次修改服务设置后，会显示完整目标地址、模型与 API 费用提示，需要确认后才发送。请求包含正文、源段及术语表。程序设置 `store=false`，服务方如何处理数据仍取决于其政策。客户端不复用浏览器 Cookie 或 ChatGPT 登录。

## 明确的接口协议

首版实现 [OpenAI Chat Completions](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create) 的 HTTPS POST / Bearer 认证及 [JSON object 输出](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=chat)。没有声明兼容所有服务。其他提供商需支持下面完整协议，真实连通性需在本地配置后验证。

请求包含 `model`、`store:false`、`response_format:{"type":"json_object"}` 与两条 messages（system 翻译规则、user JSON 数据）。user 数据包含源 / 目标语言、聊天或正式语气、`input_text`、连续编号 `source_segments` 和 `glossary_entries`。不会配置工具调用。输入中的问题、指令及术语内容均视为待翻译数据。

### DeepSeek 官方接口

完整地址填写 `https://api.deepseek.com/chat/completions`，模型填写 `deepseek-flash`（按 2026-10-10 [官方调用文档](https://api-docs.deepseek.com/)）；不能仅填写品牌名 `deepseek`。客户端在 `/v1/chat/completions` 路径也会选择同样的参数分支，服务路径以官方文档为准。API Key 在本地设置输入。

当 HTTPS 主机严格为 `api.deepseek.com` 且端口为 443 时，v0.1.1 使用明确的 DeepSeek 参数分支：省略其 Chat Completions 文档未列出的 `store` 字段，发送 `thinking:{"type":"disabled"}`、`stream:false` 和 JSON object 格式。其他主机保持原协议，不通过模型名或域名后缀猜测供应商。非思考模式用于直接输出译文，不代表已证明某次 400 必由思考模式造成。[官方思考模式参数](https://api-docs.deepseek.com/guides/thinking_mode/)

HTTP 400 / 422 的错误响应最多读取 8 KiB，只在内存中匹配已知参数与类别，显示固定中文提示；不会显示或保存可能回显正文或密钥的原始服务错误。未知格式仍保留通用状态码提示。

响应必须有一条 `choices`、`finish_reason:"stop"`，且 `message.content` 是这样的 JSON 文本：

```json
{
  "source_language": "english",
  "optimized_chinese": null,
  "segments": [{"id": 1, "text": "完整的目标语言正文"}]
}
```

`source_language` 为 `chinese` / `english` / `filipino`，须与请求相符。中文源必须有非空 `optimized_chinese`；其他源为 null。每个输入源段须有同 ID 的一个译段，按顺序返回，不加序号、套话或解释。客户端校验段数、ID、结束状态和关键字面信息，再按完整句子显示。超长单句不从中间切开。金额、币种、常见订单号、URL、API 字段等缺失或变更时拒绝展示；该检查无法证明所有姓名或否定 / 条件语义均正确，真实语义验收仍需模型实测。

连接超时默认 45 秒，可设 5–180 秒；401 / 403、限流、网络故障、无效或截断响应有中文提示，不展示服务器错误正文。自动重定向被关闭，避免把凭据转交至重定向目标。

## 源码与构建

`src/Suiyi.Core`：规则、检测、分段、模拟、请求隔离与 HTTP 接口。

`src/Suiyi.App`：WPF 窗口、设置、托盘、快捷键、手动剪贴板操作。

`tests/Suiyi.Tests`：无第三方测试包的断言程序。`docs/需求与计划.md` 和 `docs/测试记录.md` 记录范围与验收结果。

使用 [微软官方 .NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)，当前项目内便携 SDK 是 10.0.401，下载后已核验官方 SHA512。源码包不包含 SDK。可用 `tools/Get-DotNetSdk.ps1` 获取并校验固定版本，它会拒绝覆盖已存在的 SDK 目录。若已安装该 SDK，在普通 PowerShell 中运行：

```powershell
.\build.ps1
.\tests\run-tests.ps1 -Windows
.\tests\run-tests.ps1 -UiSmoke
.\build.ps1 -Portable
```

脚本优先 `tools/dotnet/dotnet.exe`，否则使用 PATH 的 dotnet。普通构建无需在线第三方依赖；便携发布首次需要从官方 nuget.org 获取微软运行库包。构建缓存和 NuGet 配置隔离于项目目录，不读取其他项目的密钥或用户 NuGet 配置；脚本完成后还原环境变量与当前目录。不应为脚本修改安全策略；若当前终端禁止脚本，可按脚本中的 dotnet 命令手动执行。`-UiSmoke` 会显示自己的 WPF 窗口，用固定样例检查交互并生成测试截图，不向其他软件输入或读取实际剪贴板。

## 验收状态

v0.1.1 Release 构建通过；61 组自动化断言、17 个实际 Shown WPF 窗口流程检查通过。首版另有真实 Windows 全局快捷键冲突 / 保留旧组合 / 释放后再注册及便携启动检查通过。故障与 DeepSeek 协议测试使用内存 HTTP 假响应，未产生真实 API 费用。用户已反馈首版在将模型名从 deepseek 改为具体模型名后调用成功；v0.1.1 新参数分支尚未用真实凭据复验。详细记录及截图路径见 `docs/测试记录.md`，源码包包含截图和检查 JSON。

待验证：真实服务连接与三语翻译语义（含否定、条件、不确定性及输入指令防护）、实际中文输入法候选操作及物理按键的 Ctrl+Enter / 全局唤起、其他机器 / 不同 DPI 的布局。无真实凭据不影响离线首版交付。
