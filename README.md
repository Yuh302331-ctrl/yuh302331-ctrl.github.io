# Yuh302331-ctrl.github.io

深色赛博风个人主页 —— PowerShell / Git / 自动化折腾者的数字名片。
**纯静态页面，无前端框架和 CDN**，托管在 GitHub Pages，手机电脑都适配。

🔗 在线访问：<https://yuh302331-ctrl.github.io>

## 功能

- 🟢 **矩阵雨背景**：Canvas 手写动画，页面隐藏自动暂停，省电自适应
- ⌨️ **打字机开场 + 模拟终端演出**：Hero 区循环打字，终端逐字"表演"PowerShell
- 📊 **GitHub 数据自动更新**：公开仓库数 / Star / 关注者 / GitHub 天数
- 🗂️ **项目卡片自动生成**：由 GitHub Actions 定时更新静态数据，主页无需现场请求 API
- 🎭 **动效细节**：滚动进度条、滚动显现、卡片 3D 倾斜、鼠标光斑跟随
- 📱 **响应式**：移动端汉堡菜单；尊重系统"减弱动画"设置，无障碍友好

## 技术栈

- 原生 **HTML / CSS / JavaScript**（无框架、无 CDN）
- **GitHub Pages** 静态托管
- **GitHub Actions**：定时预生成同源 `data/github.json`，避免访客触发 API 限流

## 本地预览

在仓库目录启动一个本地静态服务器：

```bash
python3 -m http.server 8000
```

然后访问 <http://localhost:8000/>。直接双击 `index.html` 时，浏览器会阻止读取 `data/github.json`。

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
├── 404.html        # 轻量 404 页（noindex）
├── preview.jpg     # 社交分享预览图
├── data/           # Actions 自动更新的 GitHub 数据
├── scripts/        # 数据生成脚本
└── skills/         # AI Agent 技能包（system-report 系统体检等）
```
