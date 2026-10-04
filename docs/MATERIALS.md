# 软绒 / 果冻气泡材质

`materials/` 保留实际制作使用的 Three.js r185 ShaderMaterial、带弯曲纤维的软绒表层与距离场轮廓。默认庄方宜的双云纹气泡；改用自己的 shape JSON 可以制作樱花、星星或雪团子轮廓。

这是**材质制作阶段**：浏览器将形状烘焙为真正透明的 RGBA PNG。实际桌宠使用 Windows 原生窗口与弹簧网格处理拖动、捏压、释放回弹和碰撞；运行时不要求 Three.js、Node 或浏览器，也不会把终端文本、余额、聊天内容提交给材质制作页。

## 本地烘焙

需要 Node.js、Playwright 与其 Chromium。Three.js 已附源码与许可证，无需从 CDN 加载。

```powershell
Set-Location materials
npm ci
npx playwright install chromium
node bake.mjs --role zhuangfangyi --output ../build/materials
```

自己的气泡传入 `--shapes ../workspaces/myrole/materials/bubble-shapes.json --role myrole`。目标 PNG 与 manifest 已存在时默认停止；选择新输出目录，或在确认替换本地候选后显式传 `--replace`。

输出 `<role>-gel.png`（兼容历史文件名）与 `material-manifest.json`。原生 runtime 从 `assets/materials/` 读取该 PNG；先检查形状内文字安全区、关闭按钮、余额条与动作按钮，再把审核稿复制到角色资源目录。文件名中的 gel 不代表一定采用果冻表层，`surface: snow-plush` 对应毛绒。

## 形状契约

每个角色对象需有 `width`、`height`、十六进制 `color`、`surface`、`kind`、`margin` 和 SVG `path`。坐标单位与逻辑宽高一致；输出为 2 倍像素分辨率。`kind` 会参与确定性纤维种子；不要用密集尖角或极窄花瓣凹口承载 UI。

RGBA/边距检查不能替代交互实测。验收时需点击气泡中心、弧形边缘、余额、输入框及所有按钮；拖动标题/空白区应能移动，真实透明区应穿透鼠标，且不阻挡 Windows Terminal 的窗口边缘。
