// dsh-whale-pet 插件:注册 /whalepet 命令,启动鲸鱼娘桌面宠物
// 桌宠 exe 优先用随插件分发的 dist/DSH桌宠.exe;缺失时用 build.ps1 现场构建到 %LOCALAPPDATA%\DSHWhalePet
import { spawn, spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { homedir } from 'node:os';

const name = 'dsh-whale-pet';
const inject = ['commands'];

const PLUGIN_DIR = dirname(fileURLToPath(import.meta.url));
const SHIPPED_EXE = join(PLUGIN_DIR, '..', 'dist', 'DSH桌宠.exe');
const FALLBACK_DIR = join(homedir(), 'AppData', 'Local', 'DSHWhalePet');
const FALLBACK_EXE = join(FALLBACK_DIR, 'DSH桌宠.exe');
const BUILD_PS1 = join(PLUGIN_DIR, '..', 'build.ps1');

function pickExe() {
    return existsSync(SHIPPED_EXE) ? SHIPPED_EXE : FALLBACK_EXE;
}

/** 确保 exe 存在:分发版优先,缺失则用 build.ps1 构建到用户目录 */
function ensureExe() {
    if (existsSync(pickExe())) return true;
    if (!existsSync(BUILD_PS1)) return false;
    const r = spawnSync(
        'powershell',
        ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', BUILD_PS1, '-OutDir', FALLBACK_DIR],
        { timeout: 180000, stdio: 'ignore' }
    );
    return r.status === 0 && existsSync(FALLBACK_EXE);
}

function apply(ctx) {
    ctx.commands.register({
        name: 'whalepet',
        description: '启动 DSH 鲸鱼娘桌面宠物(双击唤起 GUI,右键管理服务)',
        input: { hint: '' },
        handler: (invocation) => {
            if (!ensureExe()) {
                return {
                    kind: 'error',
                    text: '未找到桌宠 exe 且构建失败,请检查插件目录(dist/DSH桌宠.exe 或 build.ps1)是否完整。'
                };
            }
            try {
                const child = spawn(pickExe(), [], { detached: true, stdio: 'ignore' });
                child.unref();
                return {
                    kind: 'success',
                    text: '🐋 鲸鱼娘桌宠已启动!双击她唤起 DSH GUI,右键可管理服务。'
                };
            } catch (e) {
                return { kind: 'error', text: '启动桌宠失败: ' + String(e) };
            }
        }
    });
}

export { apply, inject, name };
