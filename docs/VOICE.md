# 角色语音工作流

这条流程把已审核的角色台词和 WAV 整理成原生挂件可读取的本地语音库，并保留原生 Qwen 克隆、Flash 烘焙与 realtime 朗读入口。离线整理、格式校验、试听页和回归测试使用 Python 3.10+ 标准库，不需要 Key，不发 API 请求，不收费。云端生成是另一条明确开启的路径；仓库不携带任何人的 Key、云端音色绑定或真人音频。

默认互动语音与轻触音效开启，音量 0.38；聊天朗读与 `EnableCloudSpeech` 关闭。模型名称 `qwen-audio-3.1-tts-flash`、`qwen-audio-3.1-realtime-plus` 是现有工程的使用快照，不代表最新型号、价格或免费额度承诺。运行前以自己账户中实际可用的地域、模型和额度为准；本工具不查询价格，也不切换计费策略。

## 先确认素材与人物

先建立角色 ID、人设与语气卡，再找该角色本人对应的日语语音。优先使用有明确来源与许可的官方短对白、游戏单句语音，或演员授权给你的录音。相同声优配过的其他角色不能自动当作这个人物的原音。只筛选必要的短素材，不下载整部动画、电影、整张专辑，也不读取浏览器 Cookie 或绕过登录权限。

公开可播放不等于允许转载或克隆。角色版权、录音权益、演员音色和你写的原创互动台词是不同的权利对象；不能把演员音色标成自己的原创产权。素材收集、清理、上传克隆与最终发布都需要人工核对许可及适用范围。私有仓库本身不提供这些许可。[来源与权利记录](SOURCES-AND-RIGHTS.md) 应与人物资料一起维护。

可选的 [来源模板](../templates/voice-source.example.json) 默认 `ApprovedForPreparation=false`。审核完成后记录真实来源、说话人、语言、许可依据和限制，再改为 `true`。整理工具仅记录你的审核声明，不替你判定法律权利、演员同意或声音身份。来源 JSON 的额外 `Key`、`VoiceId`、`WorkspaceId`、`cache` 等字段不会带入输出；含签名或凭据的 URL 会被拒绝。YouTube 来源只允许公开 `v` 参数，不接受其他查询令牌。

## 从资料到干净参考音

保留原素材和来源 SHA，所有加工写入新的目录。先逐句听辨身份、语言、背景音乐、其他说话人、战斗音效、喘息及尾音。优先选择正常日常语气，避免喊叫、唱歌、哭喊、密集笑声、多人叠说和强混响。参考音建议约 10–20 秒，由一到几句完整句子构成；原生输入校验范围为 5–60 秒、至少 16 kHz、16 bit PCM、10 MiB 以内，最终以服务端实际要求为准。

如果原本是干净游戏单句，不必反复降噪。背景音乐明显时，可在本地 UVR 或已有的分离器中得到人声候选；保留模型名称、原始输入、人声输出和处理参数，不将分离器的输出直接视为合格。人工 A/B 听辨有没有金属颤音、齿音破损、残余伴奏、丢字和呼吸被抹掉。分离器和模型需要单独准备，本仓库不会自动下载模型或上传音源。

以下是已获得使用许可素材的参数化 FFmpeg 命令，输出都是新的文件。时间只是示例，必须按实际完整句子边界调整。

```powershell
# 从已批准的本地素材取 12.5 秒，保留完整发声和自然尾音。
ffmpeg -n -ss 5.2 -i "approved-source.wav" -t 12.5 -map 0:a:0 -vn -ac 1 -ar 24000 -c:a pcm_s16le "reference-candidate.wav"

# 仅有轻微稳定底噪时生成一个温和降噪候选，保留未处理版本作 A/B。
ffmpeg -n -i "reference-candidate.wav" -af "afftdn=nr=6:nf=-50" -ac 1 -ar 24000 -c:a pcm_s16le "reference-denoise-candidate.wav"

# 已听辨选定的参考音最后统一格式；这一步不提高音源真实细节。
ffmpeg -n -i "approved-reference.wav" -ac 1 -ar 24000 -c:a pcm_s16le "assets/voice-references/sample.wav"
```

`afftdn` 的 `nr` 是降噪量，`nf` 是噪声底；上面的参数只是候选起点，不会自动保证干净人声。[FFmpeg 原生滤镜说明](https://ffmpeg.org/ffmpeg-filters.html#afftdn) 可用于核对本机版本。不要用强降噪或全段静音裁剪替代听辨：轻声尾音和自然停顿容易被切掉。异常尾噪应在确认最后有效音节结束后局部剪掉，保留少量自然停顿；不要加速、改调来掩盖克隆问题。

## 原创日语互动台词

使用 [台词模板](../templates/interaction-lines.example.json)。正式一套可以按七类场景各八句，共 56 句；工具允许较小的小样集合，不把条数当作质量证明。

| Context | 互动 | 文案重点 |
| --- | --- | --- |
| `touch` | 轻触 | 短短回应被叫到，带人物口吻 |
| `dollTouch` | 娃娃触碰 | 回应玩偶，区别于直接摸角色 |
| `squeeze` | 捏捏 | 意外、轻微吐槽、回弹感 |
| `drag` | 拖动 | 对位置移动的自然反应 |
| `release` | 放下 | 安稳停下，有完整句末 |
| `impact` | 碰撞 | 小惊讶，不尖叫或虚构受伤 |
| `repeatTouch` | 连续触碰 | 轻松打趣，也允许请用户听完一句 |

原生角色契约把摸头、脸颊、问候和吸附映射到适当的本地台词池。若扩展其他事件，应同步修改角色运行时的事件映射，而不是只增加未被调用的 JSON。

`Id` 必须以精确的 `role_` 开头，例如 `sample_touch_01`；只使用 ASCII 字母、数字和下划线。保留 `Japanese`、`Chinese`、`Delivery`。日语按官方人物的第一人称、称呼、礼貌程度和熟人语气写原创短句；不要把用户默认认成原作某个人，也不要靠反复口头禅、连续笑声、密集省略号或幼态撒娇表现人设。程序没有视觉的情形，不要声称已经看到用户的表情、房间或动作。

共同演绎基准是正常会话语速、清楚完整发声、适当的轻笑意与完整句末停顿。温柔不等于耳语或气喘，克制不等于机器人。先核对读音、角色感和触碰语境，再提交 TTS。ASR 可以辅助发现漏词或读错，但不能代替日语听辨及听感审核。

## 离线整理与校验

准备一个 WAV 目录，每个文件名与台词 `Id` 完全一致。互动 WAV 必须为真正的 **24,000 Hz、单声道、16 bit PCM**，时长 0.25–30 秒。模板里的 `sample` 是中性示例 ID，实际角色需在角色契约中注册后重新编译运行时，不能仅改 JSON 期待原生程序识别新角色。

```powershell
python scripts/Prepare-VoiceLibrary.py --role sample --lines templates/interaction-lines.example.json --wav-dir "workspaces/sample/reviewed-wavs" --output "workspaces/sample/voice-export"

# 来源元数据与审核后的 PNG 头像均可选；此命令也不会上传任何东西。
python scripts/Prepare-VoiceLibrary.py --role sample --lines "workspaces/sample/interaction-lines-ja.json" --wav-dir "workspaces/sample/reviewed-wavs" --approved-source-meta "workspaces/sample/approved-sources.json" --portrait "workspaces/sample/assets/approved-avatar.png" --model qwen-audio-3.1-tts-flash --output "workspaces/sample/voice-export-reviewed"

python scripts/Validate-VoiceLibrary.py --root "workspaces/sample/voice-export" --report "workspaces/sample/voice-validation.json"
```

输出目录必须是新目录或空目录。工具先验证全部输入，再原子提交整套输出，不覆盖已有库、项目或账户配置。`--model` 只是记录生成来源的标签，不调用模型。工具不降噪、不重采样、不改语速或音量；它保留 PCM 样本，将 WAV 重建为只有 `fmt`/`data` 的容器，去掉可能包含本机信息的 `LIST` 等非音频块。因此 PCM 样本 SHA 保持不变，而原文件带额外元数据时整个 WAV 的 SHA 会改变。

输出布局与原生 `InteractionVoices.Exported` 一致：

```text
voice-export/
  interaction-lines-ja.json
  voice-library/interaction-ja/
    manifest.json
    listen.html
    sources.json                  # 可选，白名单来源记录
    sample/
      sample_touch_01.wav
      sample_touch_01.wav.json
  assets/voice-avatar.png          # 可选，审核后的 PNG
  verification/voice-preparation.json
```

sidecar 只含 `role/id/ja/zh/delivery/model/sha256`。清单与试听页用相对路径，没有 Key、音色 ID、Workspace ID 或缓存绑定；试听页不调用网络，按场景筛选和逐句试听，播放新条目会暂停其他音频。

校验覆盖格式、PCM 长度、全静音、SHA、角色与完整台词一致性、重复 ID、清单数量、相对路径和所有试听资源。异常峰值和过低信号只提供听辨提示，不擅自修正。通过校验说明资源契约正确，不能证明已去除背景音乐、音色身份正确、台词自然、克隆已获许可或听感合格。

把审核后的 `interaction-lines-ja.json` 与 `voice-library` 放入自己的角色 `AssetRoot`，可选头像也放入其 `assets`，再通过 [Build-Runtime.ps1](../scripts/Build-Runtime.ps1) 生成原生运行时。不要把离线语音库直接覆盖到旧项目。完整工程的创建、构建与打包见仓库主流程和 [打包说明](PACKAGING.md)。

## 实际 Qwen 克隆与烘焙入口

原生实现已经保留：[QwenService](../runtime/native/QwenService.cs)、[QwenRealtime](../runtime/native/QwenRealtime.cs)、[VoiceBakeJob](../runtime/native/VoiceBakeJob.cs) 和 [InteractionVoices](../runtime/native/InteractionVoices.cs)。流程是参考音校验 → 本人账户创建或验证音色 → 查询就绪状态 → 烘焙台词 → 本地缓存与安全导出。Flash 与 realtime-plus 的音色绑定属于对应账户、模型及业务空间，不能把某一模型的音色 ID 复制给另一模型或他人账户。

先用构建后的挂件声音设置窗口，或者原生 `--voice-settings <role>` 窗口，填写自己的 Key、业务空间、参考音和模型，并明确开启联网合成。Key 使用当前 Windows 用户的 DPAPI 加密，配置目录由生成的 `RoleCatalog.ConfigurationNamespace` 决定，例如某角色使用 `%LOCALAPPDATA%/OCShellSkinWorkflow-<ProfileName>/`。不能提交该目录、`qwen-key.bin`、`qwen-voice.json`、usage 原始记录、云端绑定或上传签名到 Git。

使用 [Invoke-VoiceJob.ps1](../scripts/Invoke-VoiceJob.ps1) 的默认行为只是计划，不读取 Key、不启动任务：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-VoiceJob.ps1 -RuntimeRoot "build/runtime/sample" -Role sample -Operation Clone

# 已完成来源/参考音人工审核，并配置自己的账户后，才明确开启云端。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-VoiceJob.ps1 -RuntimeRoot "build/runtime/sample" -Role sample -Operation Clone -EnableCloud -ReferenceReviewed

# Bake 会先建立/验证音色，再生成这个角色当前台词文件内的全部句子，可能收费。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-VoiceJob.ps1 -RuntimeRoot "build/runtime/sample" -Role sample -Operation Bake -EnableCloud -ReferenceReviewed
```

包装器从编译后的角色契约读取真实配置 namespace，核对注册角色、当前用户自己的本地设置与 Key、`EnableCloudSpeech`、北京 Qwen Audio 模型和参考 WAV 后，才调用真实 `--voice-clone <role>` 或 `--voice-bake <role>`。它不另造 HTTP 层，不显示 Key、音色 ID、Workspace ID 或签名 URL。`-WhatIf` 和缺少 `-EnableCloud` 都不进入账户读取或请求阶段；失败不会自动再启动一批。

正式 56 句前，先在独立角色工作区把 `interaction-lines-ja.json` 收敛成两条审核过的小样，执行克隆与小批量 Bake，人工听完再扩为全套。现有 CLI 没有 `samples-only` 参数；实际批量大小是当前台词文件中的句数。保持已合格小样的 ID、日语、Delivery、模型、音色和语速不变，后续可复用个人缓存。试听按钮可能优先播放已导出的旧录音，单改音色 ID 不代表已有 WAV 已换声；更换音色须明确重烘焙，并核对新导出 SHA。

烘焙过程会保存 `voice-library/interaction-ja/bake-progress.json` 和已成功音频。网络暂忙由原生有限重试处理；额度、账户、模型或音色不兼容时停止检查，不通过反复换参数烧请求来掩盖失败。记录实际成功请求与 usage，未知余额或免费剩余额度不能凭音频数量推测。云端原始响应可能含签名 URL，不应放进调试输出或仓库。

## 实时 TTS 与本地播放

静态互动依赖预烘焙 WAV，聊天朗读通过 `CompanionAudio.Reply` → 文本分段 → `QwenVoice.Bake`，根据角色实际模型走 Flash HTTP 或 realtime WebSocket；本地缓存以模型、音色、台词、语言与演绎设置构成身份。换 Key 或声纹绑定不应伪装成可移植缓存。

默认 `Chat=false`，没有自己的凭据与音色时，未收录句子继续显示文字，不发云端请求。以后配置自己的账户、开启联网合成并选择聊天朗读即可使用保留的 TTS 管线。预烘焙触碰播放保持 500 ms 节流、上一句结束后的 330 ms 停顿、最多一次最新 pending，不让下一句抢话；捏捏音效和人声使用独立输出。实时生成与人工听感仍需单独验收，格式校验不能代替这一步。

## 离线回归与交付边界

```powershell
python -m unittest discover -s tests -p test_voice_workflow.py -v
```

测试只在临时目录合成短 PCM 正弦波，不带真人资料，也不调用 TTS。覆盖 CLI 整理/校验、PCM 保留与非音频元数据清除、来源审核和白名单、签名 URL 拒绝、错误格式与损坏文件、越界及重复 ID、现有目录保护、sidecar/清单不一致、HTML 转义、远程资源拒绝，以及 Windows 包装器默认 dry-run 和失败闭锁。发布只带工具、原创文本模板和审核记录；真实参考音及完整角色成品按对应许可单独交付，不能因为放入 ZIP 或私有仓库就省略审核。
