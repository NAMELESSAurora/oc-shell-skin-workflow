# 可选本地超分

本仓库保留实际流程使用的 **waifu2x-ncnn-vulkan、models-cunet、2×、noise=-1** 路线。可执行程序和模型体积较大且需要 Vulkan，不附在工作流源码包；从 [上游项目](https://github.com/nihui/waifu2x-ncnn-vulkan) 获取兼容自己的平台版本，或使用已安装的工具。

```powershell
python scripts/Upscale-Layer.py --input workspaces/myrole/character.png --output workspaces/myrole/character-2x.png --executable ./local/waifu2x/waifu2x-ncnn-vulkan.exe --model-dir ./local/waifu2x/models-cunet --check-only
# 检查计划后，去掉 --check-only 执行本机推理
```

环境层也单独执行同样步骤，再把两个输出交给 `Compose-Background.py`。工具使用真实 RGB PNG 做推理；人物的原始 alpha 平面另外 bicubic 2×并还原，避开既有批次中 RGBA 推理输出异常的问题。它不声称上游问题已被修复，也不会对脸部额外加模糊或使用独立横纵比例拉伸。

输出保留来源/引擎 SHA、模型目录名、尺寸、参数与 alpha 处理说明；拒绝覆盖输入、现有输出或元数据。`--check-only` 只验证路径和图像格式，报告为 planned，不能当作超分成功。

模型推理仍需人工检查发丝边缘、半透明衣料、重复细纹、过锐化噪点和脸部一致性。测试中的合成夹具用于验证 RGB 推理与 alpha 保留，不能替代真实角色的美术验收。
