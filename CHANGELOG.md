# Changelog

All notable changes to **DSH Whale Pet** are documented here.

## [v1.16] - 2026-10-01

### Fixed
- **桌面端探测不再依赖安装路径**(别人的安装位置可能完全不同)。v1.15 只查 4 个常见路径 + "运行中的进程反查";
  若桌面端装在**非常规位置且当前没在运行**,就会漏判 → 错误回落到 CLI 模式、另起一个 dsh 实例(双实例/双 GUI)。
  现在按 5 条途径探测,并保证"漏判"不会再发生:
  1. 用户配置的 `desktopExe`;
  2. **注册表卸载信息**(DisplayName 含 "DeepSeek Harness" → `InstallLocation` / `DisplayIcon` / `UninstallString`);
  3. **开始菜单快捷方式**(名字含 `Harness` 的 `.lnk` → 解析快捷方式目标);
  4. 常见安装位置(`Program Files`、`%LOCALAPPDATA%\Programs`、`C:\dsh`、`D:\dsh`);
  5. 从**正在运行的桌面端进程反查**可执行文件路径。
- **模式判定兜底**:即使一个 exe 都没探到,只要桌面端**进程在跑**、或存在**桌面端用户数据目录**
  (`%APPDATA%\@deepseek-ai\dsh-desktop`),也判定为桌面端模式——绝不会因为"路径不同"而另起 CLI 实例。
- **进程/窗口匹配放宽**:除默认的 `DeepSeek Harness` 外,还会用探测到的 exe 文件名(改名版、便携版也能认),
  并按可执行文件路径二次匹配;窗口识别(置顶/关闭)同步使用同一规则。
- **退出语义修正(桌面端模式)**:右键「退出」现在**只退出桌宠**,**不再关闭桌面端窗口**。
  v1.15 会顺手把你正在用的 DSH 界面一起关掉,既反直觉,也让"重启桌宠"变得很别扭。
  「关闭程序」的语义不变:桌面端模式只关窗口、不动服务进程。
- 未找到桌面端 exe 时,双击鲸鱼娘会弹气泡提示,指引在配置里手工设置 `desktopExe`。

### Verified
- 本机五条探测途径实测全部命中:进程反查 / 注册表卸载信息 / 开始菜单快捷方式 / 用户数据目录 / 进程状态
  (注册表命中的条目为 `DeepSeek Harness 0.2.0-rc.2`),说明探测不依赖具体盘符与目录。

## [v1.15] - 2026-10-01

### Added
- **适配 DSH 桌面端(Electron 应用)**:自动识别"桌面端"与"命令行"两种形态并切换行为(`mode=auto`)。
  - **桌面端模式**(检测到 `DeepSeek Harness.exe` 或它的进程):
    - 在线判定 = **桌面端进程是否在运行**(不再看 3080 端口);
    - 打开程序/双击 = 把桌面端**窗口置顶**(最小化了先还原);没在跑就先启动它,启动后自动置顶;
    - 关闭程序 = **只关桌面端窗口**,绝不杀它的服务进程;
    - **不再拉起 CLI 实例**——否则会出现两套 GUI、两份会话、两个端口;
    - 状态卡片显示桌面端进程 PID 与运行时长。
  - **CLI 模式**(找不到桌面端时自动回落,或 `mode=cli`):完整保留原有的服务管理(v1.11~v1.14 的令牌开窗、抢跑保护、关窗修复)。
- 新增配置:`mode=auto|desktop|cli`、`desktopExe`(默认自动探测 `D:\dsh\DeepSeek Harness.exe` 等常见位置,也会从正在运行的桌面端进程反查路径)。

### Why
- 桌面端自带 dsh 服务(端口由应用自己分配,实测 19387,且同样有令牌围栏)并自带窗口。桌宠原逻辑假设"服务由桌宠拉起、端口固定在 3080",在桌面端下会**再起一个 CLI 实例**,造成双实例、双 GUI、会话分裂,且状态灯误判离线。
- 桌面端模式下改为窗口级操作(置顶/关闭窗口)后,既避免了双实例,也不需要令牌 URL——因为根本不经过浏览器。

## [v1.14] - 2026-09-10

### Packaging(首次发布到 npm)
- **npm 包名改为 `@miku00039-01/dsh-whale-pet`**:npm 上 `dsh-whale-pet` 已被他人占用(eric0v0 的另一款鲸鱼桌宠),
  故改用作用域包名。同步改了 `cordis.patch.yml` 的 `name`(与包名一致)与文档里的安装命令;
  本地 profile 的依赖键也需随之改为作用域名。
- **声明宿主要求 `engines.dsh: ">=0.1.0-rc.6"`**:插件市场(dsh-market)卡片上的"宿主要求"读的是 npm manifest 的
  `engines.dsh`(或 lockstep 的 `@deepseek-ai/dsh-*` peer 依赖);未发布到 npm 的 GitHub-only 插件读不到 manifest,
  只会显示"宿主要求未知"。只写下限是为了避免以后新版 dsh 被误判为不兼容。
- 发布到 npm 后,目录站(awesome-dsh-plugin)每日自动重探未发布插件,`npm`/`version`/`downloads` 字段会自动补齐,
  市场随即显示宿主要求与每周下载量(下载量口径 = npm last-week downloads)。

### Added
- **右键菜单新增「🗕 最小化至托盘」**:单击后鲸鱼娘隐藏(仅保留托盘图标),托盘提示变为"已最小化(单击图标显示)"并弹出一次气泡提示。
  - **单击托盘图标即可唤起**:已最小化时单击还原鲸鱼娘(回到原位置、状态点立即重绘);未最小化时单击仍是原有行为(打开 GUI)。
  - **再次运行 exe 也能唤回**:单实例互斥原有逻辑从"打开 GUI"改为"已最小化则先还原,否则打开 GUI"。
  - 最小化时暂停分层窗口重绘(还原时重绘一次),并保存当前坐标,还原后位置不变。

## [v1.13] - 2026-09-10

### Fixed
- **"关闭程序/退出"关不掉 GUI 窗口**(现象:服务已被停掉,但 Harness 窗口不关,页面停留在"正在自动重连")。
  - 根因:dsh 新版前端的窗口标题格式变了。实测当前窗口标题为 **`<会话标题> — DeepSeek Harness`**(应用名在后),而桌宠自 v1.4 起用的是 `title.StartsWith("DeepSeek Harness")`(只匹配"应用名在前"的旧格式,如 `DeepSeek Harness - 127.0.0.1`),因此匹配必然失败 → `WM_CLOSE` 从未发出。
  - 修正:改为**包含应用名**匹配(同时兼容新旧两种格式),并**排除标题里带浏览器名的普通浏览器窗口**(Google Chrome / Edge / Firefox / Brave / Opera / Vivaldi / Safari / 360),保持"只关 GUI 窗口、不误关用户正在浏览的窗口"的原有意图。
  - 已用 10 项用例验证匹配逻辑(含实测真实标题、旧格式、重连态、浏览器标签页排除等)全部通过。

## [v1.12] - 2026-09-10

### Fixed
- **首次开窗抢跑导致的 404/401**(现象:启动桌宠后第一次打开 GUI 显示"找不到网页 404"或"authentication required 401",而桌宠指示灯是绿色;右键"关闭程序"再"打开程序"后就正常了)。
  - 根因:服务的**端口 listen 早于 Web 路由与信任围栏就绪**——桌宠一探测到端口在线就开窗,此刻会先拿到 404(路由尚未挂上),稍后拿到 401(围栏就绪但浏览器没有 cookie)。"真正就绪"的可靠信号是服务打印的令牌行(`dsh web: http://...?token=...`)。
  - 修正:桌宠启动服务后,**在读到令牌行之前不急着开窗**,短暂等待(最多 10 秒,100ms 粒度)后再用令牌地址开窗;始终读不到则回落到原有 PWA 快捷方式。等待在后台线程进行,不阻塞界面。
  - 新增:每轮启动服务时**清空上一轮的令牌状态**(旧进程的令牌必然失效),避免拿旧令牌开窗。
  - 新增:令牌只在"桌宠自己启动的服务进程仍存活"时使用(`serverProc` 已退出 / 服务由外部启动时一律走原有路径)。

## [v1.11] - 2026-09-10

### Fixed
- **开窗不再依赖 30 天 cookie**:新版 dsh(0.1.x)对裸地址 `http://127.0.0.1:<port>/` 要求浏览器持有 `dsh-auth-*` cookie(默认 30 天有效),cookie 缺失/过期时裸地址只返回 **401**(纯文本 `dsh web authentication required`)。桌宠此前只会打开 PWA 快捷方式(裸地址),一旦 cookie 失效便无法自愈。
  - 现在桌宠会从服务输出里**捕获带进程令牌的地址**(`dsh web: http://...?token=...`),启动后首次开窗优先使用它——浏览器访问该地址即换取新 cookie 并 303 跳回干净地址,随后回落 PWA 快捷方式复用同一窗口。
  - 新增配置 `openMode`(默认 `auto`):`auto` = 令牌优先(仅本次令牌未用过时)/ `pwa` = 只走快捷方式 / `token` = 始终用令牌地址。
  - 新增配置 `chromeProfile`(默认从 PWA 快捷方式的 `--profile-directory` 自动读取):令牌地址以 Chrome 应用窗口模式打开,保持与 PWA 同一 profile,才能共用 cookie。
  - `dsh-launch.log` 新增开窗方式与令牌捕获记录,便于排查。

### Notes
- 若服务由外部启动(例如 CMD 里执行 `dsh web`),桌宠拿不到该进程的令牌,此时回落为原有的 PWA 快捷方式行为。

## [v1.10] - 2026-08-23

### Fixed
- 服务启动加 `--no-open`:新版 dsh(0.1.1+)启动默认会自动打开浏览器,导致"先弹网页再开 PWA 窗口"的双窗口问题;现在开窗完全由桌宠控制(PWA 窗口),不再多开 Chrome 标签页。
- `start-dsh.ps1` 同步加 `--no-open`。

### 开关说明(入口)
- **dsh 侧开关**:`dsh web` 默认自动打开浏览器;加 `--no-open` 则不开(`dsh web --no-open`)。想自己手动开浏览器时,不加该参数即可。
- **桌宠侧行为**:桌宠固定以 `--no-open` 启动服务,开窗由桌宠统一控制——优先打开 Chrome PWA 独立窗口(`pwaShortcut` 配置项),未找到快捷方式时回落到默认浏览器。
- **自定义**:修改 `dsh-whale-pet.conf` 的 `pwaShortcut` 可控制用哪个 PWA/浏览器打开;置空则始终用默认浏览器打开。

## [v1.9] - 2026-08-23

### Changed
- 服务进程**完全隐藏控制台窗口**(`CreateNoWindow`),不再弹出空窗口;输出全部写入 `dsh-server.log` 日志。

## [v1.8] - 2026-08-18

### Fixed / Added (diagnostics for the "first launch after boot hangs" issue)
- 服务启动改为**输出重定向到日志**(`dsh-server.log`),窗口空白也能看到服务实际输出。
- 新增启动日志 `dsh-launch.log`(记录启动命令与时间)。
- 启动等待超过 30 秒时托盘弹出"启动较慢"提示(常见于开机首次启动被安全软件扫描)。
- 服务进程已退出但未就绪时,弹窗明确报错并指向日志,不再静默失败。
- 已知环境提示:开机首次启动如被 Windows Defender 云扫描拖慢,建议将 node/dsh 目录加入 Defender 排除项。

## [v1.7.0] - 2026-08-16

### Added
- **DSH 插件化**:仓库现在是一个可安装的 dsh 插件(`dsh.bundle` manifest)。
  - 安装:`dsh plugin --profile web add github:miku00039-01/dsh-whale-pet`
  - 安装后输入 `/whalepet` 命令即可启动鲸鱼娘桌宠
  - 桌宠 exe 随插件分发(`dist/`);缺失时自动用 `build.ps1` 构建到 `%LOCALAPPDATA%\DSHWhalePet`
- `build.ps1` 新增 `-OutDir` 参数(指定输出目录)。

## [v1.6] - 2026-08-16

### Changed
- **Remove all hardcoded paths**: workspace / node / dsh / PWA shortcut are now configured via `dsh-whale-pet.conf` (INI) with automatic detection on first run; graceful warning when Node.js or DSH is missing.
- **Publish-ready packaging**: added `build.ps1` (icon generation + compile with built-in csc.exe), `README.md` / `README.en.md`, `LICENSE` (MIT), `.gitignore`, GitHub Actions CI (`build on push`, `release on tag`).

## [v1.5] - 2026-08-16

### Added
- Custom exe icon: whale artwork embedded via `/win32icon` (multi-size ICO generated from the transparent PNG).

## [v1.4] - 2026-08-16

### Fixed
- "Close program" now closes the GUI window by **matching the window title** (`DeepSeek Harness ...`). The PWA window is hosted inside the main Chrome process with no `--app-id` in its command line, so the previous process-command-line matching never matched.

## [v1.3] - 2026-08-16

### Added
- "Close program" also closes the DeepSeek Harness PWA window (initial app-id based approach).

## [v1.2] - 2026-08-16

### Changed
- Opening the GUI now launches the **Chrome PWA shortcut** (standalone window, reuses the same window instead of stacking tabs in the browser).

## [v1.1] - 2026-08-16

### Fixed
- Service startup now invokes `node <dsh lib/bin.js> web` directly instead of `npx` (no more npm/npx dialog in automated contexts).
- Added `WS_EX_LAYERED` window style — fixed the pet rendering as a black square (UpdateLayeredWindow had been failing silently).

## [v1.0] - 2026-08-16

### Added
- Initial release: transparent always-on-top whale pet.
- Start / stop / monitor the DSH service on `127.0.0.1:3080`.
- Double-click to summon the GUI; drag to move (position remembered).
- Right-click menu (Open / Close / Status / Exit), tray icon, green/red adaptive status monitoring (5s online / 2s offline).
- Floating status card with service info + pet memory/GDI self-check.
- Single instance (named Mutex); crash log (`dsh-whale-pet-crash.log`).
