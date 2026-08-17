# 🐋 DSH 桌宠 (DSH Whale Pet)

一个 Windows 桌面宠物程序,把 **DeepSeek Harness** 的服务启动、停止、状态监测和 GUI 唤起整合到一个零依赖的 `.exe` 里。

**English**: [README.en.md](README.en.md) · **版本记录**: [CHANGELOG.md](CHANGELOG.md) · **安全**: [SECURITY.md](SECURITY.md)

![whale](assets/whale.png)

## ✨ 功能

| 功能 | 说明 |
|---|---|
| **一键启动** | 双击 exe = 检测 DSH 服务 → 未运行则后台拉起 → 就绪后打开 GUI → 鲸鱼娘出现 |
| **人形桌宠** | 逐像素透明悬浮窗,按图片原形状显示,可拖动、记住位置 |
| **双击唤起** | 双击鲸鱼娘打开 GUI(复用同一 PWA 窗口,不堆标签页) |
| **右键菜单** | 打开程序 / 关闭程序 / 查看状态 / 退出 |
| **关闭程序** | 只停 DSH 服务 + 关闭 GUI 应用窗口(按窗口标题精确匹配,**不影响其他浏览器页面**) |
| **退出** | 确认框后连服务一起退出 |
| **托盘图标** | 左键唤起,右键菜单(兜底入口) |
| **状态监测** | 仅绿/红两态(无黄色中间态),自适应频率:在线 5 秒 / 离线 2 秒 |
| **状态卡片** | 悬浮卡片显示服务状态/地址/PID/运行时长 + 桌宠内存/GDI 句柄(自检防泄漏) |
| **单实例** | 重复双击不会开第二个,只会唤起已有实例 |
| **崩溃日志** | 未捕获异常自动写入 `dsh-whale-pet-crash.log`,方便排查 |

## 📦 依赖(前置条件)

| 依赖 | 必选? | 说明 |
|---|---|---|
| **Windows 10 / 11** | ✅ 必选 | 使用了 WinForms / 分层窗口 / netstat 等 Windows 能力 |
| **.NET Framework 4.x** | ✅ 必选 | **系统自带,无需安装**;exe 由系统自带 csc.exe 编译,目标框架 4.x |
| **DeepSeek Harness** | ✅ 必选 | 需要**正确安装**:① Node.js(提供 node 运行环境)② dsh CLI 全局安装(`npm i -g @deepseek-ai/dsh`)③ 已配置 API 凭证(`~/.dsh/.credentials.yaml` 中的 `DEEPSEEK_API_KEY`)。桌宠通过 `node <dsh的bin.js> web` 拉起服务,监听 `127.0.0.1:3080` |
| **Chrome** | ⚪ 可选 | 用于 PWA 独立窗口模式(由 DSH GUI 生成 `DeepSeek Harness.lnk` 快捷方式)。未安装/未找到时自动回落到默认浏览器打开 |

> 简言之:**装好 DeepSeek Harness 就能跑**;装 Chrome 并获得 PWA 快捷方式可获得最佳体验。

## 🚀 快速开始

### 方式一:作为 DSH 插件安装(推荐)

```sh
dsh plugin --profile web add github:miku00039-01/dsh-whale-pet
```

安装后在 DSH 里输入 `/whalepet` 即可启动鲸鱼娘桌宠。(本插件已在 [awesome-dsh-plugin](https://awesome-dsh-plugin.com) 收录流程中。)

### 方式二:直接下载 exe

1. 从 Releases 下载 `DSH桌宠.exe`(或按下方"从源码构建")
2. 双击运行 → 鲸鱼娘出现在屏幕右下角
3. 首次运行会在 exe 同目录生成 `dsh-whale-pet.conf`(自动检测的路径),可按需修改

## ⚙️ 配置(`dsh-whale-pet.conf`,INI 格式)

| 键 | 默认 | 说明 |
|---|---|---|
| `workspace` | (exe 所在目录) | DSH 工作区目录(决定会话归属;建议设为你的工作目录) |
| `nodePath` | 自动检测 | node.exe 路径 |
| `dshBin` | 自动检测 | `@deepseek-ai/dsh` 的 `lib/bin.js` 路径 |
| `pwaShortcut` | 自动查找 | Chrome PWA 快捷方式路径;留空且找不到则用浏览器打开 |
| `pwaWindowTitle` | `DeepSeek Harness` | GUI 应用窗口标题前缀(用于"关闭程序"时精确关窗) |
| `port` | `3080` | DSH 服务端口 |
| `lastX` / `lastY` | -1 | 鲸鱼娘上次位置(自动记录) |

所有键留空 = 自动检测;修改后重启桌宠生效。示例见 `dsh-whale-pet.conf.example`。

## 🔨 从源码构建

```powershell
# 要求:Windows 10/11(自带 csc.exe 与 .NET Framework),PowerShell 7 或 5.1
pwsh -File build.ps1
```

构建脚本会:① 从 `assets/whale.png` 生成多尺寸 `pet.ico`;② 用系统 csc.exe 编译 `src/DSHPet.cs` → `DSH桌宠.exe`(零第三方依赖)。

## 🚀 发布新版本(CI 自动构建 Release)

仓库已配置 GitHub Actions:推送 `v*` 标签时,自动在 Windows 环境构建并上传 exe 到 Release。

```bash
git tag v1.0.0
git push origin v1.0.0
```

推送 `main` 分支也会触发一次构建校验(不发布)。

## 📁 目录结构

```
dsh-whale-pet/
├── package.json                # dsh 插件 manifest(dsh.bundle)
├── cordis.patch.yml            # 插件注册(/whalepet 命令)
├── lib/index.js                # 插件实现:注册命令并启动桌宠 exe
├── dist/DSH桌宠.exe            # 随插件分发的桌宠 exe
├── src/DSHPet.cs               # 桌宠全部源码(单文件,WinForms + P/Invoke)
├── assets/whale.png            # 桌宠形象素材(见下方授权)
├── assets/pet.ico              # 构建生成的 exe 图标
├── assets/WHALE_ATTRIBUTION.md # 素材授权说明
├── build.ps1                   # 一键构建脚本(支持 -OutDir)
├── .github/workflows/ci.yml    # CI:push 构建校验,tag 自动发 Release
├── dsh-whale-pet.conf.example  # 配置示例
├── CHANGELOG.md                # 版本记录
└── README.md
```

## 🖼️ 素材授权

桌宠形象(鲸鱼娘)素材的出处与许可详见 [`assets/WHALE_ATTRIBUTION.md`](assets/WHALE_ATTRIBUTION.md)。请在使用/分发前确认符合其许可要求。

## 📄 License

代码部分:MIT,见 [LICENSE](LICENSE)。素材部分遵循上述授权说明。
