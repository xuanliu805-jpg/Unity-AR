/**
 * 三端联调端到端测试（E2E）。
 *
 * 覆盖任务书要求的最小闭环：
 *   Unity 上报（这里用 HTTP 请求模拟真机上报） → Go 保存 → React 看到 → React 改状态 → Go 重启后数据仍在
 *   以及「后端不可用时页面不能白屏」。
 *
 * 运行前提：
 *   1. 已安装 Go：https://go.dev/dl/
 *   2. 已安装 Node 18+
 *   3. 安装 Playwright：
 *        cd web && npm i -D playwright && npx playwright install chromium
 *   4. 在仓库根目录执行：
 *        node scripts/e2e.mjs
 *
 * 脚本自己会：编译并启动 Go 后端 → 启动 Vite 开发服务器 → 用无头浏览器跑完整流程 → 关闭所有进程。
 * 截图会写到 docs/screenshots/ 目录。
 */

import { spawn, spawnSync } from 'node:child_process';
import { setTimeout as sleep } from 'node:timers/promises';
import { mkdirSync, rmSync, existsSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '..');
const webDir = join(repoRoot, 'web');
const shotsDir = join(repoRoot, 'docs', 'screenshots');

const API_PORT = Number(process.env.E2E_API_PORT || 18081);
const WEB_PORT = Number(process.env.E2E_WEB_PORT || 15173);
const API_BASE = `http://localhost:${API_PORT}`;
const WEB_BASE = `http://localhost:${WEB_PORT}`;
const DATA_FILE = join(repoRoot, 'server', 'data', 'e2e-issues.json');

const children = [];
let failures = 0;

function log(msg) {
  console.log(`[e2e] ${msg}`);
}

function check(name, condition, detail = '') {
  if (condition) {
    console.log(`  ✔ ${name}`);
  } else {
    failures += 1;
    console.log(`  ✘ ${name}${detail ? ` —— ${detail}` : ''}`);
  }
}

function startProcess(label, command, args, options = {}) {
  const child = spawn(command, args, {
    cwd: options.cwd || repoRoot,
    env: { ...process.env, ...(options.env || {}) },
    stdio: ['ignore', 'pipe', 'pipe'],
    shell: process.platform === 'win32',
  });
  child.stdout.on('data', (b) => process.stdout.write(`[${label}] ${b}`));
  child.stderr.on('data', (b) => process.stderr.write(`[${label}!] ${b}`));
  children.push(child);
  return child;
}

async function waitForHttp(url, timeoutMs = 30000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      const res = await fetch(url);
      if (res.ok) return true;
    } catch {
      // 还没起来，继续等
    }
    await sleep(300);
  }
  return false;
}

async function waitForExit(child, timeoutMs = 8000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (child.exitCode !== null || child.signalCode !== null) return true;
    await sleep(100);
  }
  return false;
}

function killTree(child) {
  if (!child || child.exitCode !== null) return;
  try {
    if (process.platform === 'win32' && child.pid) {
      // shell:true 时 child 是 cmd.exe，直接 kill 只会杀掉壳，真正的服务进程还活着，
      // 会导致下次启动报 "Only one usage of each socket address"。所以要按进程树同步杀。
      spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore', shell: true });
    } else {
      child.kill('SIGTERM');
    }
  } catch {
    /* ignore */
  }
}

/** 启动前先确认端口没被占用，避免上一次残留进程导致结果失真。 */
async function ensurePortFree(port, label) {
  try {
    await fetch(`http://localhost:${port}/`, { signal: AbortSignal.timeout(2000) });
  } catch {
    // 连不上 = 端口空闲，符合预期
    return;
  }
  throw new Error(
    `端口 ${port} 已被占用（可能是上一次的 ${label} 进程还在跑）。请先关闭它再运行本脚本。`,
  );
}

function stopAll() {
  for (const child of children) {
    killTree(child);
  }
}

// ---------------------------------------------------------------- 用例


/**
 * 解析 playwright 的位置。
 * ESM 的 import() 不认 NODE_PATH，所以这里用 createRequire 在若干候选目录里找，
 * 找不到时回退到标准的 import('playwright')（即项目本地 node_modules）。
 */
async function loadPlaywright() {
  const { createRequire } = await import('node:module');
  const { pathToFileURL } = await import('node:url');
  const require = createRequire(import.meta.url);

  const candidates = [];
  if (process.env.PLAYWRIGHT_MODULE_DIR) candidates.push(process.env.PLAYWRIGHT_MODULE_DIR);
  for (const p of (process.env.NODE_PATH || '').split(process.platform === 'win32' ? ';' : ':')) {
    if (p) candidates.push(p);
  }
  candidates.push(join(repoRoot, 'web', 'node_modules'));

  for (const base of candidates) {
    try {
      const resolved = require.resolve('playwright', { paths: [base] });
      const mod = await import(pathToFileURL(resolved).href);
      return mod.chromium ? mod : mod.default;
    } catch {
      // 继续试下一个
    }
  }

  const mod = await import('playwright');
  return mod.chromium ? mod : mod.default;
}

async function main() {
  mkdirSync(shotsDir, { recursive: true });
  if (existsSync(DATA_FILE)) rmSync(DATA_FILE);

  log('1/6 编译 Go 后端');
  const goBin = process.env.GO_BIN || 'go';
  await new Promise((res, rej) => {
    const c = spawn(goBin, ['build', '-o', join(repoRoot, 'server', 'server.exe'), './cmd/server'], {
      cwd: join(repoRoot, 'server'),
      stdio: 'inherit',
      shell: process.platform === 'win32',
    });
    c.on('exit', (code) => (code === 0 ? res() : rej(new Error(`go build 失败: ${code}`))));
  });

  await ensurePortFree(API_PORT, 'Go 后端');
  await ensurePortFree(WEB_PORT, 'Vite');

  log('2/6 启动 Go 后端');
  let backend = startProcess('go', join(repoRoot, 'server', 'server.exe'), [
    '-addr', `:${API_PORT}`,
    '-data', DATA_FILE,
  ]);
  if (!(await waitForHttp(`${API_BASE}/api/health`))) {
    throw new Error('Go 后端启动超时');
  }
  log(`   后端就绪：${API_BASE}`);

  log('3/6 启动 Vite 开发服务器');
  const npx = process.platform === 'win32' ? 'npx.cmd' : 'npx';
  startProcess('vite', npx, ['vite', '--port', String(WEB_PORT), '--strictPort'], {
    cwd: webDir,
    env: { VITE_API_BASE_URL: API_BASE, VITE_API_TIMEOUT_MS: '2000' },
  });
  if (!(await waitForHttp(WEB_BASE, 60000))) {
    throw new Error('Vite 开发服务器启动超时');
  }
  log(`   前端就绪：${WEB_BASE}`);

  // --- 模拟 Unity 真机上报 ---
  log('4/6 模拟 Unity 上报问题');
  const reportRes = await fetch(`${API_BASE}/api/issues`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      title: '入口墙面破损',
      description: '左侧墙体存在裂缝',
      priority: 'high',
      position: { x: 0.42, y: 0.03, z: 1.26 },
    }),
  });
  const reportBody = await reportRes.json();
  const issueId = reportBody?.issue?.id;
  check('Unity 上报返回 201', reportRes.status === 201, `实际 ${reportRes.status}`);
  check('后端生成了问题 ID', Boolean(issueId), JSON.stringify(reportBody));
  check('默认状态为 open', reportBody?.issue?.status === 'open');

  // --- React 页面 ---
  const { chromium } = await loadPlaywright();
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  const consoleErrors = [];
  const pageErrors = [];
  page.on('console', (m) => {
    if (m.type() !== 'error') return;
    const text = m.text();
    // 「后端不可用」阶段浏览器必然打印 net::ERR_CONNECTION_REFUSED / Failed to load resource，
    // 这是本用例故意制造的场景，不算前端缺陷；这里只关心真正的 JS 报错。
    if (/net::ERR_|Failed to load resource/i.test(text)) return;
    consoleErrors.push(text);
  });
  page.on('pageerror', (err) => pageErrors.push(String(err)));

  log('5/6 打开 React 管理端并验证');
  await page.goto(WEB_BASE, { waitUntil: 'networkidle' });
  await page.waitForSelector('table.issue-table', { timeout: 15000 });

  const titleCell = page.locator('tbody tr').first().locator('.issue-title');
  check('React 列表里看得到 Unity 上报的标题', (await titleCell.innerText()) === '入口墙面破损',
    await titleCell.innerText());
  check('顶部显示问题总数 1', (await page.locator('[data-testid="total-count"]').innerText()) === '1');
  const bodyText = await page.locator('tbody').innerText();
  check('列表里显示了优先级「高」', bodyText.includes('高'));
  check('列表里显示了状态「待处理」', bodyText.includes('待处理'));
  check('列表里显示了位置坐标', bodyText.includes('0.42') || bodyText.includes('1.26'));
  await page.screenshot({ path: join(shotsDir, '01-列表显示Unity上报的问题.png'), fullPage: true });

  // 改状态
  const statusSelect = page.locator('tbody tr').first().locator('select');
  await statusSelect.selectOption('resolved');
  await page.waitForTimeout(800);
  const afterPatch = await (await fetch(`${API_BASE}/api/issues`)).json();
  check('React 改状态后后端真的变成 resolved',
    afterPatch.issues.some((i) => i.id === issueId && i.status === 'resolved'),
    JSON.stringify(afterPatch.issues.map((i) => [i.id, i.status])));
  const rowText = await page.locator('tbody tr').first().innerText();
  check('界面上的状态同步变成「已解决」', rowText.includes('已解决'), rowText);
  await page.screenshot({ path: join(shotsDir, '02-React修改状态为已解决.png'), fullPage: true });

  // 后端不可用 → 不能白屏
  log('6/6 停掉后端，验证页面不白屏');
  killTree(backend);
  await waitForExit(backend);
  await sleep(500);
  await page.locator('button:has-text("刷新")').click();

  // 后端被杀掉时，浏览器复用的 keep-alive 连接可能既不报错也不返回，
  // 前端靠请求超时（VITE_API_TIMEOUT_MS=2000）兜底，所以这里要等错误条出现而不是死等固定时长。
  const banner = page.locator('.error-banner').first();
  const bannerAppeared = await banner
    .waitFor({ state: 'visible', timeout: 15000 })
    .then(() => true)
    .catch(() => false);
  const bannerText = bannerAppeared ? await banner.innerText() : '';
  check('后端不可用时页面显示错误提示', bannerAppeared && bannerText.length > 0, bannerText);
  check('后端不可用时表头仍然渲染（没有白屏）',
    await page.locator('table.issue-table thead').isVisible());
  check('后端不可用时仍能看到错误条上的重试按钮',
    await page.locator('button:has-text("重试")').first().isVisible().catch(() => false));
  await page.screenshot({ path: join(shotsDir, '03-后端不可用时显示错误提示.png'), fullPage: true });

  // 重启后端 → 数据仍在
  log('   重启后端，验证数据持久化');
  backend = startProcess('go', join(repoRoot, 'server', 'server.exe'), [
    '-addr', `:${API_PORT}`,
    '-data', DATA_FILE,
  ]);
  if (!(await waitForHttp(`${API_BASE}/api/health`))) {
    throw new Error('Go 后端重启超时');
  }
  await page.locator('button:has-text("重试")').first().click();
  await page.waitForTimeout(1200);

  const titleAfterRestart = await page.locator('tbody tr').first().locator('.issue-title').innerText();
  check('Go 重启后数据仍然存在', titleAfterRestart === '入口墙面破损', titleAfterRestart);
  const rowAfterRestart = await page.locator('tbody tr').first().innerText();
  check('Go 重启后状态修改也保留（仍是已解决）', rowAfterRestart.includes('已解决'), rowAfterRestart);
  await page.screenshot({ path: join(shotsDir, '04-重启后数据仍然存在.png'), fullPage: true });

  check('浏览器控制台没有 JS 报错', consoleErrors.length === 0, consoleErrors.join(' | '));
  check('页面没有未捕获的异常', pageErrors.length === 0, pageErrors.join(' | '));

  await browser.close();
}

main()
  .then(async () => {
    console.log(failures === 0 ? '\n[e2e] 全部通过 ✅' : `\n[e2e] 有 ${failures} 项未通过 ❌`);
    stopAll();
    await sleep(800);
    process.exit(failures === 0 ? 0 : 1);
  })
  .catch(async (err) => {
    console.error('[e2e] 执行失败:', err);
    stopAll();
    await sleep(800);
    process.exit(1);
  });
