# 随译 v0.1.2

个人使用的 Windows 聊天翻译客户端。C# / .NET 10 WPF，默认即时聊天风格，支持中文、English、Filipino / Tagalog。没有账号、历史、自动发送、剪贴板监听或后台翻译。

## 直接启动

解压 `artifacts/Suiyi-v0.1.2-win-x64.zip`，保持整个 `Suiyi-v0.1.2-win-x64` 目录完整，双击 **Suiyi.exe**。Windows x64 自包含版本，无需安装 .NET。便携版本是免安装程序。`build.ps1 -Portable` 从 `src/Suiyi.App/Suiyi.App.csproj` 的 `Version` 生成这个目录名和 zip 名；当前版本是 0.1.2。

非密钥设置保存在 `%LOCALAPPDATA%\Suiyi\settings.json`（服务地址、模型、超时、快捷键、术语、是否记住密钥，以及已确认过费用提示的地址）。密钥不在这个文件里。

升级时先从旧版托盘菜单「退出」，再启动新版。源码目录和 `%LOCALAPPDATA%\Suiyi` 不必为升级删除。默认不勾选「记住 API Key」时，密钥只在本进程内存中，退出后清除，新版需要重新输入。已经勾选并成功保存时，同一 Windows 用户下次启动会从本机加密文件恢复，不必为了升级重新输入。

- **Ctrl+Alt+T**：全局唤起；在「设置」修改，保存时检测冲突，失败保留旧组合。
- **Ctrl+Enter**：翻译。Enter 正常换行；中文输入法组合过程中不触发翻译。
- **Esc / 关闭 / 最小化**：收回系统托盘并取消未完成请求。托盘双击打开，右键「退出」结束进程。
- 「取消」「清空」及修改输入 / 选项使旧请求失效；新请求不会被旧响应覆盖。「重试」使用当前文本和选项重新发起。
- 中文输入默认显示优化中文和英语；「复制译文」默认复制英语正文。手选其他目标时复制该目标语言。段序号是界面元素，不进入复制正文。
- 原文语言可自动或手选；目标为默认方向或中 / 英 / Filipino。自动检测采用本地字符和词表启发式，Taglish、中英混合和短 Filipino 消息建议手选。
- 「术语表」每行 `原文 = 首选表达`；最多 100 条，每项最多 100 字。不需要保存翻译历史。

## 模拟与真实 API

没有密钥时，顶部明确显示**模拟模式**。只为用户提供的四个固定样例及部分手选语言方向演示结果；其他文本显示模拟占位。这些结果不能作为真实翻译质量验收。单次输入上限 20,000 字符。

在客户端「设置」中填写完整 HTTPS 接口地址、服务提供商支持的模型名及 API Key。默认地址为 `https://api.openai.com/v1/chat/completions`，模型名留空，需按你账户的实际权限填写。**不要把密钥发给聊天助手。** 程序不写正文和密钥日志，设置文件也不包含密钥、输入正文或译文。

「记住 API Key（仅当前 Windows 用户）」默认不勾选。不勾选但输入了密钥时，密钥只留在当前进程内存里，底部状态为「密钥仅本次会话」；托盘「退出」会清掉这份内存副本。没有密钥时状态为「未设置密钥」，界面进入模拟模式。勾选并成功保存后，状态为「密钥已安全保存」。下次启动会解密进进程内存并进入 API 模式；打开「设置」时，密码框里是这份密钥。

保存位置是 `%LOCALAPPDATA%\Suiyi\credentials\`。文件名是规范化服务地址 UTF-8 的 SHA-256 十六进制，扩展名 `.bin`，内容不是明文。加密使用 Windows DPAPI 的 `DataProtectionScope.CurrentUser`。附加熵是 UTF-8 字节 `Suiyi.CredentialStore.v1`、一个换行，再接该规范化地址，然后做 SHA-256。因此密文绑定当前 Windows 用户和这一条服务地址。把密文复制到另一个服务地址对应的文件名下，不能解密出密钥。规范化地址要求完整 HTTPS，不能带用户名、查询参数或片段；主机名小写，默认 443 端口省略，路径保留。密钥不能为空白、不能含控制字符，不超过 8192 个字符，且 UTF-8 不超过 8 KiB。写入时先写 `.bin.tmp` 再替换 `.bin`。

要删除已保存的密钥：打开「设置」，地址保持为该服务，取消勾选后点「保存并应用」。这会删除该地址的 `.bin` 和 `.bin.tmp`。密码框里仍有的密钥在这次退出前还可以用于翻译；退出后内存副本清除，磁盘上的该地址文件也不再存在。更换地址时，窗口会清空密钥框并取消勾选。保存只对当前输入的地址调用保存或删除，不会顺带删掉旧地址已经写下的文件。要清除旧地址，把地址改回那个服务（窗口会再次清空密钥框）、取消勾选并保存。这样会删掉旧地址的文件，同时这次保存后的进程内存里也不再留着密码框中的密钥，除非重新输入。也可以直接删除 `credentials` 目录中的文件。若设置里仍勾着记住，但对应文件不存在，下次启动会提示「此服务尚无保存的密钥，请在设置中输入并勾选记住密钥。」文件在但无法解密或内容无效时，提示「记住的密钥无法读取或解密，请在设置中重新输入。」两种情况都不会把文件内容当作密钥使用。勾选记住时必须先输入密钥。密钥留空并取消记住，即回到模拟模式。若加密文件已经写入或删除，但 `settings.json` 没能保存，设置窗口会提示「密钥存储选项已处理，但普通设置无法保存。请检查本地目录权限后重试。」

第一次向某个规范化后的服务地址发送真实请求时，会显示完整目标地址、当前模型与 API 费用提示，确认后才发送。确认按地址记在 `settings.json` 的 `AcceptedServiceEndpoints` 里，与模型和密钥无关；同一地址在重启或只改模型后不再提示。路径或非默认端口不同，视为另一个地址，会再提示一次。已确认地址最多保留 256 个，超出时丢掉最早的一条。若这次确认没能写入设置文件，则只在本次进程有效，重启后可能再次提示。请求包含正文、源段及术语表。除下面的 DeepSeek 官方主机外，请求设置 `store:false`；所有主机都发送 `stream:false`。服务方如何处理数据仍取决于其政策。客户端不复用浏览器 Cookie 或 ChatGPT 登录。

## 明确的接口协议

首版实现 [OpenAI Chat Completions](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create) 的 HTTPS POST / Bearer 认证及 [JSON object 输出](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=chat)。没有声明兼容所有服务。其他提供商需支持下面完整协议，真实连通性需在本地配置后验证。

请求包含 `model`、`stream:false`、`response_format:{"type":"json_object"}` 与两条 messages（system 翻译规则、user JSON 数据）。非 DeepSeek 主机另含 `store:false`。user 数据包含源 / 目标语言、聊天或正式语气、`input_text`、连续编号 `source_segments` 和 `glossary_entries`。不会配置工具调用。输入中的问题、指令及术语内容均视为待翻译数据。

### DeepSeek 官方接口

完整地址填写 `https://api.deepseek.com/chat/completions`，模型填写 `deepseek-flash`（按 2026-10-10 [官方调用文档](https://api-docs.deepseek.com/)）；不能仅填写品牌名 `deepseek`。客户端在 `/v1/chat/completions` 路径也会选择同样的参数分支，服务路径以官方文档为准。API Key 在本地设置输入。

当 HTTPS 主机的 IDN 主机名是 `api.deepseek.com`（不区分大小写）且端口为 443 时，自 v0.1.1 起使用明确的 DeepSeek 参数分支：省略其 Chat Completions 文档未列出的 `store` 字段，发送 `thinking:{"type":"disabled"}`、`stream:false` 和 JSON object 格式。其他主机不进入该分支，包括带 deepseek 字样的模型名或域名，以及 `api.deepseek.com` 的非 443 端口：它们仍发送 `store:false`，不发送 `thinking`。`stream:false` 对所有主机发送。非思考模式用于直接输出译文，不代表已证明某次 400 必由思考模式造成。[官方思考模式参数](https://api-docs.deepseek.com/guides/thinking_mode/)

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

连接超时默认 45 秒，可设 5–180 秒；401 / 403、402（余额或计费）、限流、网络故障、无效或截断响应有中文提示，不展示服务器错误正文。自动重定向被关闭，避免把凭据转交至重定向目标。

## 源码与构建

`src/Suiyi.Core`：规则、检测、分段、模拟、请求隔离与 HTTP 接口。

`src/Suiyi.App`：WPF 窗口、设置、托盘、快捷键、手动剪贴板操作，以及可选的当前用户 DPAPI 密钥存储。

`tests/Suiyi.Tests`：无第三方测试包的断言程序。`docs/需求与计划.md` 和 `docs/测试记录.md` 记录范围与验收结果。交接说明见 [HANDOVER.md](HANDOVER.md)。

使用 [微软官方 .NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)，当前项目内便携 SDK 是 10.0.401，下载后已核验官方 SHA512。源码包不包含 SDK。可用 `tools/Get-DotNetSdk.ps1` 获取并校验固定版本，它会拒绝覆盖已存在的 SDK 目录。若已安装该 SDK，在普通 PowerShell 中运行：

```powershell
.\build.ps1
.\tests\run-tests.ps1 -Windows
.\tests\run-tests.ps1 -UiSmoke
.\build.ps1 -Portable
```

脚本优先 `tools/dotnet/dotnet.exe`，否则使用 PATH 的 dotnet。普通构建无需在线第三方依赖；便携发布首次需要从官方 nuget.org 获取微软运行库包。构建缓存和 NuGet 配置隔离于项目目录，不读取其他项目的密钥或用户 NuGet 配置；脚本完成后还原环境变量与当前目录。不应为脚本修改安全策略；若当前终端禁止脚本，可按脚本中的 dotnet 命令手动执行。`-UiSmoke` 会显示自己的 WPF 窗口，用固定样例检查交互并生成测试截图，不向其他软件输入或读取实际剪贴板。

## 验收状态

版本以 `src/Suiyi.App/Suiyi.App.csproj` 的 `<Version>` 为准，当前是 **0.1.2**。

`tests/Suiyi.Tests/Program.cs` 当前注册 **68** 组自动化断言：每个 `Add` / `AddAsync` 一组，HTTP 401 / 403 / 429 / 500 的循环展开为 4 组。这 68 组不含 `--windows` 的真实快捷键检查，也不含 `UiSmoke` 的窗口检查。构成是：2026-10-10 记录过的 51 组，v0.1.1 增加的 10 组 DeepSeek 与 400/422 诊断，以及 v0.1.2 增加的 7 组费用确认与记住密钥断言。51/51 与 61/61 是那两次执行的通过记录。本次文档核对没有在 Windows 上重新运行测试，**不把 68 组写成新的通过结果**。记住密钥用到 Windows DPAPI，界面检查用到 WPF，都要在 Windows 上跑。

`tests/Suiyi.Tests/UiSmoke.cs` 里仍有 17 个实际 Shown 窗口检查。v0.1.1 的执行记录和仓库里的 `tests/ui-smoke-results.json` 是 17/17 通过。其中「真实设置窗口」检查仍在查找「首版不提供记住密钥」，而当前 `SettingsWindow` 已经改成记住密钥的说明，源码里不再有那句。因此不能把那次 17/17 当作当前界面的通过结果。本次也没有重跑界面检查，没有新的截图。

此前记录的 Windows 全局快捷键冲突 / 保留旧组合 / 释放后再注册，以及便携启动检查，仍见 `docs/测试记录.md`；本次未重做。故障与 DeepSeek 协议测试按源码是内存 HTTP 假响应，那些历史记录未产生真实 API 费用。用户曾反馈首版在将模型名从 deepseek 改为具体模型名后调用成功。v0.1.1 的参数分支和 v0.1.2 的记住密钥，都没有在本次文档核对中用真实凭据复验。

待验证：真实服务连接与三语翻译语义（含否定、条件、不确定性及输入指令防护）、实际中文输入法候选操作及物理按键的 Ctrl+Enter / 全局唤起、其他机器 / 不同 DPI 的布局，以及在真实 Windows 用户配置下重新跑当前 68 组断言和界面检查。无真实凭据不影响离线交付；本次没有做真实 API 验收。
