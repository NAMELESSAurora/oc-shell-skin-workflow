# Windows 原生运行时与角色接入

本目录保存从已验证的庄方宜便携皮肤抽出的 C# / PowerShell 源码。默认契约是庄方宜的完整单角色示例；大型图片和 56 条语音留在独立成品包中，代码仓库不会复制个人 Key、云端音色 ID、聊天记录、缓存或现成 EXE。

`Build-Runtime.ps1` 为每个输出目录生成独立的 `RoleCatalog.cs`，再编译两个原生程序。一个输出目录只注册一位角色。添加角色骨架确实可以生成、编译不同的角色 ID、主题和启动入口，但它不会自动研究人设、生图、录音、克隆声线或批准安装。

## 运行依赖

- Windows x64，Windows PowerShell 5.1，以及系统 .NET Framework 4.x 的 `csc.exe` / WPF。编译脚本使用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`，不需要 Visual Studio、NuGet、.NET SDK、Node.js 或 Python。
- 完整原生离线检查需要交互式 Windows 桌面和 STA 线程。无桌面的 CI 使用代码检查即可。
- 安装和实际运行终端皮肤需要 Windows Terminal。Claude 入口还需要用户另行安装并配置 Claude Code CLI。仓库不安装 Claude，也不携带它的账户。
- 气泡/面板使用自定义圆体 `ChillRoundFRegular.ttf`，其授权文件 `OFL-ChillRound.txt` 必须与字体一起提供；粗体文件可选。缺少系统终端字体时 Windows 会选择自己的备用字体。

原生运行时使用已经烘焙的透明毛绒 PNG、WPF 弹簧网格、人物变形和惯性碰撞。Three.js 用在素材制作阶段；实际挂件没有实时 Three.js / 浏览器依赖。材质制作、分层合成、超分和声音资料整理的工具与说明见仓库其他目录。

## 只编译代码：本地和 CI

在仓库根目录执行：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Runtime.ps1
powershell.exe -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File scripts/Validate-Runtime.ps1 -CodeOnly
```

默认输出是 `build/runtime/zhuangfangyi`。没有 `-AssetRoot` 时自动进入代码模式，生成启动脚本、角色代码、两个 EXE 和 `build-manifest.json`，不注入图片/语音，不安装、不启动终端、不请求 API。代码校验检查生成角色、默认声音配置、主题、状态栏和 PowerShell 语法。

Windows GitHub Actions 的默认 PowerShell 也可以直接运行 `./scripts/Build-Runtime.ps1`。检查 WPF 时建议显式运行上面的 `powershell.exe -STA` 命令。代码检查通过不代表角色的图片、气泡和声音已经完成。

## 使用审核后的庄方宜完整资源

先从成品 release 解压资源到独立本地目录。`-AssetRoot` 必须指向含有 `assets/`、`materials/`、角色数据 JSON 和 `voice-library/` 的根目录；可以使用已解压的完整便携包。下面的路径是用户自行选择的例子：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Runtime.ps1 `
  -AssetRoot C:\SkinAssets\ZhuangFangyi-Full-Skin `
  -OutputDirectory build\reviewed-zhuangfangyi

powershell.exe -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File scripts/Validate-Runtime.ps1 `
  -RuntimeRoot build\reviewed-zhuangfangyi -RenderPreview
```

构建只复制契约中的资源与当前角色的 WAV / 公共 sidecar，不复制原包的 `bin/`、设置目录、云端音色绑定、日志、聊天记忆或 Key。验证文件写入输出目录下 `checks/`；静态设计板在 `checks/design-preview/`，示例命令和 `12.34 USD` 余额均标为预览值。

完整检查包括背景尺寸、真实图片等比、透明裁边后可见像素保持、两种外观与本地记忆、右下角吸附、旧动画清理、四个气泡按钮和关闭按钮的命中区、原生 `WM_NCHITTEST` 实体/透明区、56 条本地 PCM / SHA256 / 台词匹配、无账户字段的 sidecar，以及只包含两个终端 profile 的安装预览。检查使用隔离设置目录，关闭声音和余额刷新，不读取个人凭据、不安装、不打开实际终端。

静态设计板检查布局和颜色；它不是实际终端截图，也不代替交互手感和声音试听。新角色仍需人工审查完整头发/发饰、表情、轮廓、文字对比和听感。

## 新建角色草稿

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File scripts/New-Character.ps1 `
  -Id myrole -ProfileName MyRole -DisplayName 我的角色
```

默认位置 `workspaces/myrole` 已被 Git 忽略。脚本生成新的两枚 profile GUID、独立 `%LOCALAPPDATA%\OCShellSkinWorkflow-MyRole` 配置命名空间，以及以下骨架：

| 文件 | 需要完成的内容 |
| --- | --- |
| `runtime-contract.json` | 名字、主题、形状、路径、声音方向、资源列表与审核状态 |
| `materials/bubble-shapes.json` | 实际 SVG 轮廓、尺寸和内容区；默认借用示例轮廓，只是起点 |
| `companion-personas.json` | 有出处的人物设定、边界和自然回复逻辑 |
| `companion-dialogue.json` | 原创中文互动文案和配色；生成的 TODO 不可作为成品 |
| `interaction-lines-ja.json` | 经审核的日语触碰台词；新建时没有任何语音 |
| `AUTHORING-TODO.json` | 尚未生成素材/声音，不能安装的状态记录 |

草稿仍可验证代码接入：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Runtime.ps1 `
  -ContractPath workspaces\myrole\runtime-contract.json `
  -CodeOnly -OutputDirectory build\myrole

powershell.exe -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File scripts/Validate-Runtime.ps1 `
  -RuntimeRoot build\myrole -CodeOnly
```

这会替换编译输入中的 `RoleCatalog`、名字、配色、声音方向/模型/默认速率、配置命名空间、背景文件名和所有启动脚本 token，不需要手工编辑 C# 角色列表。代码目录仍有未审查的 TODO，不应安装。

完成素材制作与人工审核后，更新契约的 `reviewStatus` 为 `approved`，把所有成品文件放入资源根，再传入 `-AssetRoot` 构建并做完整离线检查。`draft` 的完整资源构建会被拒绝。脚本也拒绝覆盖非空角色草稿目录、非法 ID 和不属于本工具的输出目录。

## 文件契约

默认资源清单在 `templates/character-runtime-contract.json`；新角色的文件名前缀改为其 canonical ID。运行文件按以下路径放置：

```text
assets/<id>-doll.png
assets/<id>-corner-complete.png
assets/<id>-tab-avatar.png
assets/appearances/<id>-chibi.png
assets/appearances/<id>-bot.png
assets/backgrounds/<id>-terminal-full-v1-4k.png
assets/materials/<id>-gel.png
assets/fonts/ChillRoundFRegular.ttf
assets/fonts/OFL-ChillRound.txt
materials/bubble-shapes.json
oc-schemes.json
companion-personas.json
companion-dialogue.json
interaction-lines-ja.json
claude/oc-<id>.theme.json
voice-library/interaction-ja/<id>/<line-id>.wav
voice-library/interaction-ja/<id>/<line-id>.wav.json
```

角色和气泡需要真实透明 RGBA，不能把白/绿/棋盘背景烘焙进去。默认背景为 3840×2160，角色保持原比例；终端背景用 `UniformToFill`。两种右下角形象共用角色身份、聊天、娃娃和语音，切换不改主题或声音。

`oc-schemes.json` 中方案名应是 `OC <profileName>`，例如 `OC MyRole`；Claude 主题应匹配 `custom:oc-<id>`。`companion-personas.json` 顶层键、`companion-dialogue.json` 的 `characters` 键、`materials/bubble-shapes.json` 顶层键和 `interaction-lines-ja.json` 的 `Characters` 键必须有当前 `<id>`。契约里的 `bubble` 是制作参数和骨架来源；运行时实际读取 `materials/bubble-shapes.json`，两者应保持一致。

契约保存两个稳定 GUID。重建同一位角色时继续沿用，重新创建另一位角色时产生新的 GUID。`profileName` 用 ASCII 标识符，UI 中文名使用 `displayName`；`id`、路径、主题和设置命名空间必须保持一致。

语音默认是 7 个池：`touch / dollTouch / squeeze / drag / release / impact / repeatTouch`，示例每池 8 条。`headPat / cheek / greeting / dock` 映射到 `touch`。更改台词数量时同时更新 `voice.expectedOfflineClips`，不要用缺句自动请求云端来掩盖资源缺失。

台词 JSON 的每条使用字段 `Id / Japanese / Chinese / Context / Delivery`。公共音频 sidecar 字段为 `role / id / ja / zh / delivery / model / sha256`；不能包含 API Key、账户、云音色 ID 或缓存路径。WAV 是单声道 PCM16、24 kHz；运行时检查角色、台词、PCM 范围和文件 SHA256，验证不通过的导出不会播放。仓库声音工具按相同契约输出。

## 预览与安装是不同操作

构建和 `Validate-Runtime` 永远不会安装。可以单独生成安装文件预览：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File build\reviewed-zhuangfangyi\Install-CharacterSkins.ps1 `
  -PreviewDirectory build\install-preview -WorkingDirectory C:\workspace
```

只在完整资源检查和人工预览通过、用户决定使用该输出后，才执行生成目录中的安装脚本或双击 `安装皮肤.cmd`：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File build\reviewed-zhuangfangyi\Install-CharacterSkins.ps1 `
  -WorkingDirectory C:\workspace
```

安装会备份 Windows Terminal 的用户设置与同角色已有 fragment/主题，只增量更新契约的两个 GUID 和一个配色方案，并生成桌面入口。**同 GUID 的已有庄方宜 profile 会改为指向本次输出目录**；其他角色、默认 profile、既有运行终端和 Claude 全局 Key 保留。移走输出目录后需要在新位置再次安装，以重新生成绝对启动路径和本包 `claude/<id>.json` 的状态栏命令。

双击 `启动PowerShell.cmd` 或 `启动Claude.cmd` 是显式打开终端的入口。原生挂件由启动脚本附着到终端的父 PID；直接运行 `OCCompanion.exe` 不能视为独立桌宠启动方式。CLI 的主题功能也依赖用户当前 Claude Code 版本，代码校验不联网验证 CLI 兼容性。

## 配置、Key 和未来声音管线

实际个人数据在契约的 `%LOCALAPPDATA%\<configurationNamespace>`。庄方宜示例使用 `OCShellSkinWorkflow-ZhuangFangyi`，与原有生产皮肤和便携包目录隔离；每次重新生成同一角色应保持其命名空间以保留外观、面板位置和聊天记忆。

新配置默认 `Reactions=true / Sfx=true / Chat=false / EnableCloudSpeech=false / Volume=0.38`。互动优先播放经过检查的本地导出，500 ms 节流、单个待播、句末 330 ms 间隔；缺句且没有明确云许可时不会合成。

对话界面保留用户自行配置 DeepSeek、保存本机上下文/记忆的入口。记忆会随用户主动发送的聊天带给服务商。Qwen 设置入口可填写用户自己的 Key、参考音、模型和工作区；Key 由 Windows DPAPI 当前用户保护。仓库没有预填凭据或克隆绑定，契约模型名称是已有实现快照，不承诺当前价格、额度或永久接口可用。

原生支持 `--voice-clone <id>` 和 `--voice-bake <id>`，声音脚本封装见其他声音文档。**bake 可能先创建或确认音色**，并非保证仅执行 TTS；它要求用户自行打开 `EnableCloudSpeech` 和设置自己的 Key。不要在离线验证中调用这些入口。源码编译或放入参考音不会触发上传或扣费。

Claude 密钥小管家是显式用户入口：保存的配置库与恢复备份在上述隔离目录加密保存；点击“应用到 Claude”时，会按 CLI 现有机制改写用户 `.claude/settings.json` 中对应认证字段。安装脚本和离线验证不执行该操作。余额读取只支持已经实现的 GPTEAM `/usage` 接口，实际终端使用时可能访问用户当前服务商；预览使用示例值并停止余额刷新。

## 已运行的检查与适用边界

小型证据文件在 `runtime/verification/runtime-test-evidence.json`，大截图和完整报告留在被忽略的 `build/`。本次实际使用 Windows PowerShell 5.1 / 系统 C# 编译器运行了默认无素材编译、默认代码验证、独立新角色骨架编译/验证、非法输入/覆盖保护、外部成品注入和完整原生离线检查。

这些证据不等于新机器安装、新角色图像自动完成或在线服务响应已经验证。Cloud clone / bake、DeepSeek 聊天、真实账户余额、当前 Claude Code 兼容性以及新角色人工听感不属于该离线构建测试。
