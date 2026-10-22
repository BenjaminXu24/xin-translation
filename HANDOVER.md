# 随译（Suiyi）交接说明

核对日期：2026-10-21。基准是当时的 `origin/main`：`db19833519942561daf1238b1b1d382bf5e5f724`（短哈希 `db19833`）。内容按仓库里的 README、`docs/需求与计划.md`、`docs/测试记录.md`、`build.ps1`、`tests/run-tests.ps1`、`tools/`、`.github/workflows/`、`global.json`、`Directory.Build.props` 和对应源码核对。下文只写变量名、配置项名和存放位置，不含密钥值。

## 1. 项目是什么

随译（Suiyi）是个人使用的 Windows x64 聊天翻译小窗口，面向 WhatsApp / Viber 里的支付、商户、订单、结算和 API 沟通。界面语言是中文、English、Filipino（README 也写 Filipino / Tagalog）。用户在设置窗口自己填写 API Key，程序没有账号。

首版明确不做的范围（`docs/需求与计划.md` 与 README）：账号、云历史、付费、OCR、更新、消息发送。同时没有翻译历史、自动发送、剪贴板监听或后台翻译。不复用浏览器 Cookie 或 ChatGPT 登录。没有声明兼容所有模型服务。

技术栈：

- C# / .NET 10。`global.json` 把 SDK 固定为 `10.0.401`，`rollForward` 为 `latestPatch`。因此 runner 上可能用到更新的补丁；界面冒烟调查时 `windows-latest` 报的是 SDK 10.0.402。
- `Suiyi.sln` 含两个产品工程和一个测试工程：
  - `src/Suiyi.Core`：`net10.0`，纯逻辑。
  - `src/Suiyi.App`：WPF，`net10.0-windows`，同时 `UseWindowsForms`（托盘）。程序集名 `Suiyi`，`<Version>` 为 `0.1.2`。
  - `tests/Suiyi.Tests`：`net10.0-windows`，引用 Core 和 App，无第三方测试包。
- 三个 csproj 都没有 `PackageReference`。普通构建不依赖第三方 NuGet。
- `Directory.Build.props`：可空引用、隐式 using、警告当错误、确定性编译。App 工程自己把 `ImplicitUsings` 关掉。
- 后端是 OpenAI Chat Completions 协议：HTTPS POST、Bearer 认证、`response_format` 为 JSON object。默认地址写在 `AppSettings.Endpoint`，是 `https://api.openai.com/v1/chat/completions`，模型名默认为空。主机是 `api.deepseek.com` 且端口为 443 时走 DeepSeek 分支：省略 `store`，发送 `thinking` 为 disabled。其他主机发送 `store:false`。所有主机都发送 `stream:false`。实现在 `src/Suiyi.Core/ChatCompletionsService.cs`。

目录结构：

```text
Suiyi.sln
global.json
Directory.Build.props
NuGet.Config
build.ps1
README.md
docs/需求与计划.md
docs/测试记录.md
src/Suiyi.Core/          ChatCompletionsService、TranslationRules、TranslationPrompt、
                         SentenceSegmenter、MockTranslationService、LatestRequestGate、
                         ProtectedTokenValidator、Models
src/Suiyi.App/           MainWindow、SettingsWindow、GlossaryWindow、AppSettings、
                         ServiceConsent、Platform/CredentialStore、GlobalHotkey、
                         ClipboardService、TrayHost
tests/Suiyi.Tests/       Program.cs（68 组断言）、UiSmoke.cs（17 项窗口检查）
tests/run-tests.ps1
tests/ui-smoke-results.json
tools/Get-DotNetSdk.ps1
tools/Copy-PortableNotices.ps1
tools/NuGet.Publish.Config
.github/workflows/ci.yml
.github/workflows/ui-smoke.yml
```

## 2. 当前状态

### 已完成

下面四项都已 squash 合并进 `main`（每个合并提交只有一个父提交，说明里带 `(#N)`）。`main` 的尖端就是 #4。

| PR | 合并提交 | 内容 |
| --- | --- | --- |
| [#1](https://github.com/BenjaminXu24/xin-translation/pull/1) | `0d4953e` | Windows CI：`push` main、`v*` tag、pull request 上跑构建和测试 |
| [#2](https://github.com/BenjaminXu24/xin-translation/pull/2) | `3abe191` | 文档按 v0.1.2 更正版本、密钥存储和测试数量 |
| [#3](https://github.com/BenjaminXu24/xin-translation/pull/3) | `1b3977d` | 界面冒烟改为检查「记住 API Key」复选框默认未勾选 |
| [#4](https://github.com/BenjaminXu24/xin-translation/pull/4) | `db19833` | 界面冒烟改用隔离的临时设置和凭据目录；增加只能手动触发的 `ui-smoke.yml` |

没有 tag，也没有 GitHub Release。

`db19833` 的 main CI（Actions run [39427232658](https://github.com/BenjaminXu24/xin-translation/actions/runs/39427232658)）是成功的。日志里 `./build.ps1` 和 `./tests/run-tests.ps1 -Windows` 都打印了 `RESULT total=68 passed=68 failed=0`，并且「Windows 实际 RegisterHotKey 冲突、失败保留与释放」通过。这条必选 CI 不跑界面冒烟。

`Program.cs` 的 `RegisterCases` 注册 68 组：语言 / 模拟 / 分段 / 请求生命周期 20，HTTP 24（401 / 403 / 429 / 500 的循环算 4 组），DeepSeek 10，记住密钥与费用确认 7，剪贴板与快捷键解析 7。`--windows` 的真实热键检查和 `UiSmoke` 的 17 项都不在这 68 组里。

### 进行中 / 卡住

Ethan 还没有在他自己的 Windows 虚拟机上，用合并后的 `main` 重跑界面冒烟。

此前他在那台虚拟机上是 6/17（11 项失败）。对照 PR #4 的记录和现在的 `tests/Suiyi.Tests/UiSmoke.cs`：

- 8 项是测试读到了当前 Windows 用户已经记住的密钥。v0.1.2 的 `MainWindow` 构造函数会在 `RememberApiKey` 为真时用 DPAPI 载入密钥，窗口进入 API 模式，空模型名下模拟翻译不会发出去。这 8 项是：真实 WPF 窗口与模拟模式可见；英文付款样例；中文样例；Filipino 样例；中英文手动目标 Filipino；连续请求；英文问句；重试按钮。当时通过的 6 项不依赖这次模拟翻译：设置窗口、DeepSeek 裸模型名拒绝、术语窗口、取消、清空、中文 IME。
- #4 已修这条路径。冒烟窗口用测试专用构造，凭据和设置放在 `%TEMP%\suiyi-ui-smoke-<guid>\`，不再读 `%LOCALAPPDATA%\Suiyi`。在线服务工厂在冒烟里会直接抛错，避免弹出费用确认框把 Dispatcher 卡住。断言没有放宽。
- 修复后，GitHub `windows-latest` 上对 PR #4 分支的界面冒烟是 17/17，包括植入已记住密钥的对照（SDK 10.0.402，WPF 10.0.0.0，屏幕 1024×768）。证据：
  - 干净配置 17/17：[run 39400407265](https://github.com/BenjaminXu24/xin-translation/actions/runs/39400407265)
  - 修复前、植入密钥，9/17（对上上述 8 项）：[run 39401878980](https://github.com/BenjaminXu24/xin-translation/actions/runs/39401878980)
  - 修复后、含植入密钥，17/17：[run 39402673284](https://github.com/BenjaminXu24/xin-translation/actions/runs/39402673284)
- 这些 run 发生在合并前的 `cursor/ui-smoke-failures-7429` 上。合并后的 `main` 没有再用手动工作流跑过界面冒烟。仓库里的 `tests/ui-smoke-results.json` 也没有被这次 17/17 覆盖。

另外 3 项在同一台 runner 上复现不了。干净配置和「已经记住密钥」的对照里它们都通过，所以不是记住密钥那条路径，也没有为此改应用代码或放宽断言。疑似 Ethan 那台虚拟机的显示、DPI 或窗口布局。检查名与源码一致：

- `多段 fixture：真实 WPF 逐段控件与可滚动布局`
- `主窗口最小尺寸：结果区可滚动且保留完整正文`（把窗口设到 `MinWidth` / `MinHeight` 后断言实际尺寸 600×620；`MainWindow.xaml` 的最小值就是 600×620，设计尺寸是 700×790）
- `关闭收回托盘：取消请求，重新显示后没有迟到结果`

需要他用合并后的 `main` 重跑确认。重跑命令见第 4 节。

### 文档和代码已经不一致的地方

README「验收状态」和 `docs/测试记录.md` 开头是 PR #2 写的，早于 #3 / #4。它们仍说设置窗口冒烟在查找「首版不提供记住密钥」，并因此不能把仓库里那份 17/17 当成当前界面结果。当前 `UiSmoke.cs` 的检查名是「真实设置窗口：默认接口、协议、空密钥与记住密钥默认未勾选」，断言该复选框未勾选、密码框为空。`SettingsWindow` 里的说明已是记住密钥的用法，源码中没有「首版不提供记住密钥」。

仓库里的 `tests/ui-smoke-results.json` 仍是旧记录，检查名还是「真实设置窗口：默认接口、协议与空密钥提示布局」。`docs/测试记录.md` 也按文档核对时的口径，不把 68 组写成作者在本地重跑的通过数。上面 main CI 的 68/68 是 GitHub `windows-latest` 的结果，和那份文档的「未在本地重跑」可以同时成立。

真实服务的三语语义（含否定、条件、不确定性）、真实中文输入法、物理按键的 Ctrl+Enter / 全局唤起、另一台未安装 SDK 的机器上的便携启动，文档里仍是待验收。这次交接没有跑测试，也没有使用真实凭据。

## 3. 分支和 PR

写这份说明时，除本交接 PR 外没有未合并的 PR。没有 tag，`gh release list` 为空。

远端还留着四条已合并分支。可以删，但要 Ethan 同意：

- `cursor/github-actions-ci-ce31`（#1）
- `cursor/docs-match-code-7c75`（#2）
- `cursor/ui-smoke-remember-key-f9b9`（#3）
- `cursor/ui-smoke-failures-7429`（#4）

这个仓库曾经有人用另一个 AI 工具同时修改。接手任何任务前先 `git fetch origin main`，核对最新 `main` 再开分支。不要在过期快照上改。

## 4. 如何构建、测试、发布

测试只能在 Windows 上跑。`tests/Suiyi.Tests` 的目标框架是 `net10.0-windows`，并引用 WPF 的 `Suiyi.App`。这次交接所在的环境不是 Windows，没有执行下面的脚本。

SDK 选择：脚本优先用 `tools/dotnet/dotnet.exe`。源码包默认不含这份 SDK。`tools/Get-DotNetSdk.ps1` 从微软公开的 release metadata 下载 10.0.401 的 win-x64 zip，核对官方 SHA512 后再解压；`tools/dotnet` 已存在时脚本拒绝覆盖。否则使用 PATH 里的 `dotnet`，由 `global.json` 做 `latestPatch` 前滚。

### `build.ps1`

只有一个开关：`-Portable`。没有 `-Windows`，也没有 `-UiSmoke`。

无参数时：

1. 把 `DOTNET_CLI_HOME`、`NUGET_PACKAGES`、`APPDATA` 指到项目内的 `tools/cli-home`、`tools/packages`、`tools/build-appdata`，并设置 `DOTNET_CLI_TELEMETRY_OPTOUT=1`、`DOTNET_GENERATE_ASPNET_CERTIFICATE=false`。这样不读取用户 NuGet 配置。
2. `restore Suiyi.sln --configfile NuGet.Config --disable-parallel -m:1`
3. `build Suiyi.sln -c Release --no-restore -m:1`
4. `run --project tests/Suiyi.Tests -c Release --no-build`。不传 `--windows`，也不传 `--ui-smoke`，只跑 68 组断言。
5. `finally` 还原上述环境变量和当前目录。

`-Portable` 在上面成功之后继续：

- 从 `src/Suiyi.App/Suiyi.App.csproj` 读取 `Version`。
- `publish src/Suiyi.App -c Release -r win-x64 --self-contained true`，还原配置是 `tools/NuGet.Publish.Config`，输出目录 `artifacts/Suiyi-v<版本>-win-x64`。
- 把 `README.md` 和 `docs/` 拷进该目录，再跑 `tools/Copy-PortableNotices.ps1`，把微软运行库许可证和第三方声明放到发布目录的 `licenses/`。
- 打 zip：`artifacts/Suiyi-v<版本>-win-x64.zip`。当前版本是 0.1.2，所以文件名是 `artifacts/Suiyi-v0.1.2-win-x64.zip`，目录名是 `Suiyi-v0.1.2-win-x64`。

### `tests/run-tests.ps1`

参数是两个开关，互斥时优先 `-UiSmoke`：

| 调用 | 实际命令 | 覆盖范围 |
| --- | --- | --- |
| `./tests/run-tests.ps1` | 构建后运行测试 dll，无额外参数 | 68 组断言 |
| `./tests/run-tests.ps1 -Windows` | `dotnet <dll> --windows` | 68 组，外加真实 `RegisterHotKey`（临时组合上的冲突、失败时保留旧组合、释放后再注册） |
| `./tests/run-tests.ps1 -UiSmoke` | `dotnet <dll> --ui-smoke` | 17 项实际 Shown 的 WPF 窗口检查 |
| 两个开关一起给 | 走 `-UiSmoke` | 不跑 `--windows` |

构建使用仓库根的 `NuGet.Config`，输出在 `tests/artifacts`，同样把 `APPDATA` / `DOTNET_CLI_HOME` / `NUGET_PACKAGES` 隔离到 `tools/`，结束时还原。`-Windows` 会真实注册临时热键，需要允许 `RegisterHotKey` 的正常 Windows 会话。`-UiSmoke` 会 `Show()` 自己的窗口，用固定样例检查交互，截图写到 `tests/ui-evidence.png`、`tests/ui-settings-evidence.png`、`tests/ui-glossary-evidence.png`、`tests/ui-long-evidence.png`、`tests/ui-min-evidence.png`，结果写 `tests/ui-smoke-results.json`。不向其他软件输入，不读真实剪贴板。临时设置和凭据在 `%TEMP%\suiyi-ui-smoke-<guid>\`，进程结束时删除。超过 3 分钟看门狗以退出码 3 结束，并写 `tests/ui-smoke-hang.txt`。

在 Ethan 的 Windows 虚拟机上，对合并后的 `main` 重跑冒烟：

```powershell
git checkout main
git pull
./tests/run-tests.ps1 -UiSmoke
```

不需要先删掉已经记住的密钥。期望是 17/17。若失败，留下控制台里的 `UI FAIL` 行和 `tests/ui-smoke-results.json`。

### CI

`.github/workflows/ci.yml`：

- 触发：`push` 到 `main`、`push` 标签 `v*`、以及所有 `pull_request`。
- `permissions` 只有 `contents: read`。
- `windows-latest` 上用 `actions/setup-dotnet` 读取 `global.json`。
- 必选 job `Build and test`：`./build.ps1`，然后 `./tests/run-tests.ps1 -Windows`。工作流注释写明故意不跑 `-UiSmoke`：它要 `Window.Show()`、可见性和 600×620 布局，并截图；托管 runner 没有可靠的合成桌面，失败的 `Show()` 还可能停在 `Dispatcher.Run()`。
- job `Portable win-x64 zip` 只在 `push` 且 ref 以 `refs/tags/v` 开头时运行：`./build.ps1 -Portable`，再用 `actions/upload-artifact` 上传名为 `Suiyi-portable-win-x64` 的 zip，路径 `artifacts/Suiyi-v*-win-x64.zip`。工作流里没有创建 GitHub Release 的步骤。当前没有任何 tag，所以这个 job 还没在 `main` 上跑过。

`.github/workflows/ui-smoke.yml`：

- 名字是 UI smoke。当前文件只有 `workflow_dispatch`，不随 push 或 pull request 自动跑。调查 #4 时，那条分支上的工作流曾对 pull request 触发过；合并进 `main` 的版本只保留手动触发。
- 在 Actions 页面用 “Run workflow” 执行 `./tests/run-tests.ps1 -UiSmoke`。无论成败都上传 artifact `ui-smoke-evidence`（结果 json、控制台输出、hang 文件和上面五张 png；缺文件只警告）。

发布必须经 Ethan 确认。不要自行打 `v*` tag，不要建 Release。打 `v*` tag 会让 CI 打出便携 zip 并上传 artifact，不会自动变成 GitHub Release。

## 5. 外部依赖和配置项

只记录名字和位置。

用户运行时配置不在仓库里：

- API Key 由用户在设置窗口的密码框填写。复选框文案是「记住 API Key（仅当前 Windows 用户）」，对应 `AppSettings.RememberApiKey`，默认 false。不勾选时密钥只留在当前进程，托盘「退出」后清除。
- 勾选并成功保存后，密文目录是 `%LOCALAPPDATA%\Suiyi\credentials\`。`CredentialStore` 用 `Environment.SpecialFolder.LocalApplicationData` 拼出这个路径。文件名是规范化 HTTPS 地址的 SHA-256 十六进制，扩展名 `.bin`；写入时先写 `.bin.tmp` 再替换。加密是 Windows DPAPI 的 `DataProtectionScope.CurrentUser`，附加熵与该规范化地址绑定。取消勾选并保存会删除该地址的 `.bin` 和 `.bin.tmp`，不会顺带删除其他地址的文件。
- 非密钥设置在 `%LOCALAPPDATA%\Suiyi\settings.json`，由 `SettingsStore.FilePath` 拼出。字段是 `Endpoint`、`Model`、`TimeoutSeconds`、`Shortcut`、`RememberApiKey`、`AcceptedServiceEndpoints`、`Glossary`。这个文件不保存密钥、输入正文或译文。`.gitignore` 忽略 `settings.json` 和 `*.key`。
- 代码里的默认值：`Endpoint` 为上面的 OpenAI Chat Completions 地址，`Model` 为空，`TimeoutSeconds` 为 45（允许 5–180），`Shortcut` 为 `Ctrl+Alt+T`。费用确认按规范化地址记在 `AcceptedServiceEndpoints`，最多 256 条。

构建与 NuGet：

- 根目录 `NuGet.Config` 只有 `<packageSources><clear /></packageSources>`。普通 restore / build / 测试不访问任何包源。
- `tools/NuGet.Publish.Config` 只在 `build.ps1 -Portable` 时使用。它清空包源后再添加公开的 `nuget.org`（`https://api.nuget.org/v3/index.json`），用来取微软自包含运行库。没有私有源，也没有凭据节点。
- `tools/Get-DotNetSdk.ps1` 读取 `https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json`，下载并核对 10.0.401 的 win-x64 SDK。

脚本和 CI 使用的环境变量名（都不是密钥）：`DOTNET_CLI_HOME`、`DOTNET_CLI_TELEMETRY_OPTOUT`、`DOTNET_GENERATE_ASPNET_CERTIFICATE`、`DOTNET_NOLOGO`（仅 CI）、`NUGET_PACKAGES`、`APPDATA`。`Copy-PortableNotices.ps1` 还会读 `NUGET_PACKAGES`。

CI secrets：`.github/workflows/ci.yml` 和 `ui-smoke.yml` 都没有 `secrets` 引用。没有需要配置的 Actions secret。权限只有 `contents: read`。

测试用的临时目录（不是用户的密钥库）：

- 记住密钥断言：`tests/.privacy-fixtures/<guid>/`
- 界面冒烟：`%TEMP%\suiyi-ui-smoke-<guid>/` 下的 `settings.json` 和 `credentials`

## 6. 待 Ethan 决定的事项

1. 用合并后的 `main` 重跑 `-UiSmoke` 之后，若「多段 fixture」「主窗口最小尺寸」「关闭收回托盘」仍失败，怎么处理。在看到新的 `UI FAIL` 行（含内部异常、实际尺寸和 `keyLen`）之前，不要放宽 600×620、滚动或可见性断言，也不要改托盘实现。
2. 是否删除第 3 节列出的四条已合并远端分支。
3. 是否以及何时打 `v0.1.2` tag。版本号已经是 0.1.2，但没有 tag、没有 Release，CI 也还没有产出便携 zip。打 tag 只会上传 artifact。发布须经他确认。
4. 后续功能方向。首版范围之外仍然是：账号、云历史、付费、OCR、自动更新、自动发送、剪贴板监听、后台翻译。真实服务上的翻译质量也还没有在这次交接里验收。

## 7. 下一步建议

1. 在他的 Windows 虚拟机上执行第 4 节的三条命令，确认合并后的 `main` 是不是 17/17。
2. 若是 17/17，把那 3 项记成他那台虚拟机的显示 / DPI 差异，再决定要不要打 `v0.1.2` tag。确认之前不要打 tag，不要建 Release。
3. 若那 3 项仍失败，保留 `UI FAIL` 行和 `tests/ui-smoke-results.json`，再决定是记录环境差异，还是改窗口 / 测试。不要为了变绿放宽断言。
4. 另开一个小的文档 PR，更正 README「验收状态」和 `docs/测试记录.md` 里已经过时的冒烟描述。本交接只新增说明，不改那两份文档，避免混在一起。
5. 删除残留分支、打 tag、建 Release 都等他点头。本 PR 保持草稿。
6. 以后改这个仓库，先拉取最新 `main`。曾经有另一个 AI 工具同时在改。
