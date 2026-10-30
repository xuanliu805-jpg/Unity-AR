import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.PLAYWRIGHT_PATH ?? 'playwright');
import { mkdirSync } from 'node:fs';

const OUT = process.env.SHOT_DIR ?? 'E:/简历题目/web/screenshots';
mkdirSync(OUT, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1440, height: 960 }, deviceScaleFactor: 2 });
page.on('console', (m) => {
  if (m.type() === 'error') console.log('PAGE ERROR:', m.text());
});
page.on('pageerror', (e) => console.log('PAGE EXCEPTION:', e.message));

await page.goto('http://127.0.0.1:5174/', { waitUntil: 'networkidle' });
await page.waitForTimeout(1500);

await page.screenshot({ path: `${OUT}/01-overview.png`, fullPage: true });

// 智能分诊：填表 + 点分诊
await page.fill('input[name="title"]', '入口墙面破损');
await page.fill('textarea[name="description"]', '左侧墙体存在裂缝');
await page.fill('input[name="x"]', '0.42');
await page.fill('input[name="y"]', '0.03');
await page.fill('input[name="z"]', '1.26');
await page.click('button:has-text("智能分诊")');
await page.waitForSelector('[data-testid="triage-report"]');
await page.waitForTimeout(400);
await page.screenshot({ path: `${OUT}/02-triage.png`, fullPage: true });

// 采纳建议优先级
await page.click('button:has-text("采纳建议优先级")');
await page.waitForTimeout(300);
await page.screenshot({ path: `${OUT}/03-adopted.png`, fullPage: true });

// 模拟实时推送：提交一条新问题，观察 toast 与高亮
await page.click('button:has-text("提交问题")');
await page.waitForTimeout(1200);
await page.screenshot({ path: `${OUT}/04-live-toast.png`, fullPage: true });

// 详情：列表区域特写
await page.locator('.ledger').screenshot({ path: `${OUT}/05-ledger.png` });

// 窄屏
await page.setViewportSize({ width: 720, height: 1000 });
await page.waitForTimeout(500);
await page.screenshot({ path: `${OUT}/06-narrow.png`, fullPage: true });

await browser.close();
console.log('screenshots written to', OUT);