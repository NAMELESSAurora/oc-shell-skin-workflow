# v1.0.0 · 角色终端皮肤工作流

整理既有角色皮肤制作与接入流程，提供可复用的资料、人设、图像和语音模板，以及 Windows 原生桌宠源码、角色注册、安装预览、离线验证、Three.js 材质烘焙、可选本地超分和等比背景合成工具。

附件：

- `OC-Shell-Skin-Workflow-v1.0.0.zip`：工作流脚本、文档、模板、源码、有限预览与文件校验清单。
- `庄方宜_全套皮肤_2026-10-04.zip`：已完成的独立 PowerShell / Claude Code 皮肤，含三款造型、4K背景、软绒气泡、56条本地日语语音、安装启动入口及可重新编译源码。
- 两个 ZIP 各有一份 `.sha256`，可先核对归档，再检查包内 `PACKAGE-MANIFEST.json`。

运行成品需 Windows 10/11、Windows Terminal、Windows PowerShell 5.1 与系统 .NET Framework；Claude 模式另需已安装 Claude Code。离线互动语音无需 API Key。新角色生成的是草稿，需要独立制作和人工审核。

包内未携带 API Key、个人聊天记忆、账户音色绑定或原机运行状态。云端克隆/合成入口默认仅预览，显式启用后使用本机自己的设置；本次整理和验证没有请求聊天/TTS服务或消耗模型额度。

源码、第三方库与角色/音源资料分别记录权利范围，见 NOTICE 和来源说明。验证记录位于仓库 `verification/` 与完整皮肤包自己的 `verification/`。当前令牌缺少写入 Actions 的 `workflow` 权限，CI 配置保留为可启用的 `ci/validate.template.yml`；本次采用本地构建和测试。
