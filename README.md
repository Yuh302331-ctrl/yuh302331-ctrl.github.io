# Yuh302331-ctrl.github.io

深色赛博风个人主页 —— PowerShell / Git / 自动化折腾者的数字名片。
**纯静态、零外部依赖**，托管在 GitHub Pages，手机电脑都适配。

🔗 在线访问：<https://yuh302331-ctrl.github.io>

## 功能

- 🟢 **矩阵雨背景**：Canvas 手写动画，页面隐藏自动暂停，省电自适应
- ⌨️ **打字机开场 + 模拟终端演出**：Hero 区循环打字，终端逐字"表演"PowerShell
- 📊 **GitHub 数据自动拉取**：公开仓库数 / Star / 关注者 / 折腾天数
- 🗂️ **项目卡片自动生成**：调 GitHub API 实时拉取仓库列表，新增仓库后主页自动更新
- 🎭 **动效细节**：滚动进度条、滚动显现、卡片 3D 倾斜、鼠标光斑跟随
- 📱 **响应式**：移动端汉堡菜单；尊重系统"减弱动画"设置，无障碍友好

## 技术栈

- 原生 **HTML / CSS / JavaScript**（无框架、无 CDN、无构建步骤）
- **GitHub Pages** 静态托管
- **GitHub REST API**：localStorage 缓存 5 分钟，限流时自动降级提示

## 本地预览

双击 `index.html` 即可打开（GitHub 数据部分需联网）。

## 更新发布

改完代码推送即可，约 1 分钟自动发布：

```bash
git add -A
git commit -m "update homepage"
git push
```

## 目录结构

```
├── index.html      # 主页（全部样式与脚本内联在单文件）
├── 404.html        # 404 页
├── preview.png     # 社交分享预览图
└── skills/         # AI Agent 技能包（system-report 系统体检等）
```
