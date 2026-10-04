# 整理、校验与发布

源码包和运行成品分开保存。Git 仓库放脚本、模板、源代码、来源说明和小预览；体积大的背景、字体、游戏参考音、烘焙语音、预编译 EXE 放在经过校验的角色发布包中。完整庄方宜成品作为本仓库的 Release 附件。

## 工作流源码包

在仓库根目录运行：

```powershell
python scripts/Scan-Privacy.py
python -m unittest discover -s tests -v
python scripts/Package-Workflow.py --output dist/OC-Shell-Skin-Workflow-v1.0.0.zip --name OC-Shell-Skin-Workflow-v1.0.0
python scripts/Verify-Archive.py dist/OC-Shell-Skin-Workflow-v1.0.0.zip
```

打包器使用固定顶层目录/文件白名单；忽略 `build`、`dist`、临时工作区、缓存、依赖目录、EXE 构建输出与运行状态。每个文件都有大小和 SHA256，ZIP 内 CRC 与文件 SHA 会再检查一次，ZIP 外附 `.sha256`。已有归档不会被覆盖。

扫描器拒绝常见凭证字面量、私密状态文件和 JSON 中非空的账户音色/工作空间绑定；诊断只打印文件位置和规则，避免回显匹配值。它是发布前的一项检查，发布者仍需审核实际文件清单。`.gitignore` 只影响 Git，不能替代 ZIP 白名单。

## 角色运行成品

1. 根据 [runtime 契约](RUNTIME.md) 准备审核后的图像、shape、persona、dialogue 与语音库。
2. 构建独立输出目录，配置独立本地数据 namespace。
3. 先通过编译、资源路径、RGBA、台词/语音匹配和离线读取校验，再做拖动、窗口边缘、语音顺序、切换角色/外观与安装预览检查。
4. 仅列入已验证文件；不列入用户运行状态、API Key、账户音色ID、聊天记录、日志或备份。保留来源与字体许可。
5. 生成 `PACKAGE-MANIFEST.json` 与 SHA256，实际解压到包含中文和空格的另一目录，校验并运行安装预览。准备发布时再次用 `Verify-Archive.py` 检查压缩包。

`Verify-Archive.py` 同时兼容本工作流源码包和庄方宜角色包。它在解压前拒绝路径穿越、重复成员、未登记文件及校验不符；校验不会安装、启动 Claude 或请求任何 API。

## GitHub

当前仓库为私有仓库。`ci/validate.template.yml` 提供离线工具测试、Windows 原生源码编译和 Three.js 材质烘焙的 Actions 配置，不填云端 Key，不创建音色，不进行付费合成。当前 GitHub 登录令牌缺少写入工作流的 `workflow` 权限，所以模板尚未放进 `.github/workflows/`；本次采用本地检查。日后拥有该权限再复制模板启用即可。

Release v1.0.0 附工作流源码 ZIP、庄方宜完整成品 ZIP 及两份 SHA256。日后增加角色时可沿用同一流程，每个成品单独发布，避免将大图和音频不断塞进 Git 历史。手动变更仓库公开状态或分享角色资源时，应按来源说明处理第三方素材。
