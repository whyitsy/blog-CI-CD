#!/usr/bin/env node
// ============================================================================
// 把 k6 的 summary JSON（tools/load/results/raw/*-summary.json）
// 与冷缓存探针结果，汇总成一份可以直接贴进报告的 markdown。
//
//   node tools/load/summarize-baseline.mjs --out tools/load/results/baseline-xxx.md
//   node tools/load/summarize-baseline.mjs --raw tools/load/results/raw --env tools/load/results/env-xxx.md
//
// 为什么要有这一步：k6 的默认输出是给终端看的，散在六个日志里；
// 报告要的是**一张能横向比较的表**，而且必须同时带上环境规格 ——
// 没有环境的性能数字是不可复现的（learn/03 §11.1 纪律 3）。
// ============================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HERE, '..', '..');

const args = process.argv.slice(2);
const getArg = (name, dflt) => {
  const i = args.indexOf(name);
  return i >= 0 && args[i + 1] ? args[i + 1] : dflt;
};

const RAW_DIR = path.resolve(REPO_ROOT, getArg('--raw', 'tools/load/results/raw'));
const COLD = path.resolve(REPO_ROOT, getArg('--cold', 'tools/load/results/raw/cold-cache.json'));
const ENV_FILE = getArg('--env', '');
const OUT = path.resolve(REPO_ROOT, getArg('--out', 'tools/load/results/baseline.md'));

/** 报告里的固定顺序 = docs/06 §3.1 的优先级顺序 */
const ORDER = [
  ['smoke', 'B0 冒烟（首页列表）'],
  ['posts-list', 'GET /api/posts 首页列表'],
  ['posts-detail', 'GET /api/posts/{id} 详情（触发浏览量写入）'],
  ['posts-search', 'GET /api/posts/search 中文全文检索'],
  ['posts-archives', 'GET /api/posts/archives 归档'],
  ['site-config', 'GET /api/site/config 站点配置'],
  ['posts-create', 'POST /api/posts 写文章'],
];

function readSummaries() {
  const out = {};
  if (!fs.existsSync(RAW_DIR)) return out;
  for (const f of fs.readdirSync(RAW_DIR)) {
    if (!f.endsWith('-summary.json')) continue;
    const rec = JSON.parse(fs.readFileSync(path.join(RAW_DIR, f), 'utf8'));
    out[rec.endpoint] = rec;
  }
  return out;
}

const n = (v, d = 2) => (v === null || v === undefined ? '—' : Number(v).toFixed(d));

function main() {
  const sums = readSummaries();
  const lines = [];

  lines.push('# 本地性能基线（B1）');
  lines.push('');
  lines.push(`> 生成时间：${new Date().toISOString()}｜由 \`tools/load/run-baseline.sh\` + \`tools/load/summarize-baseline.mjs\` 自动生成`);
  lines.push('> 数字全部来自实测，未做任何估算。');

  if (ENV_FILE) {
    const p = path.resolve(REPO_ROOT, ENV_FILE);
    if (fs.existsSync(p)) {
      lines.push('');
      lines.push(fs.readFileSync(p, 'utf8').trim());
    }
  }

  lines.push('');
  lines.push('## 六接口实测（p50 / p95 / p99）');
  lines.push('');
  lines.push('| 接口 | 请求数 | 实际 RPS | p50 | p90 | p95 | p99 | max | HTTP 失败 | 业务失败 | SLO p95 | 判定 |');
  lines.push('|---|---|---|---|---|---|---|---|---|---|---|---|');

  for (const [key, label] of ORDER) {
    const r = sums[key];
    if (!r) continue;
    lines.push(
      `| ${label} | ${r.requests} | ${n(r.rps, 1)} | ${n(r.p50_ms)} ms | ${n(r.p90_ms)} ms | ${n(r.p95_ms)} ms | ${n(r.p99_ms)} ms | ${n(r.max_ms)} ms | ${n(r.http_failed_rate, 3)}% | ${n(r.business_error_rate, 3)}% | ${r.slo_p95_ms ?? '—'} ms | ${r.passed ? '✅' : '❌'} |`,
    );
  }

  const missing = ORDER.filter(([k]) => !sums[k]).map(([k]) => k);
  if (missing.length) lines.push(`\n> ⚠️ 缺少这些接口的结果：${missing.join(', ')}`);

  if (fs.existsSync(COLD)) {
    const cold = JSON.parse(fs.readFileSync(COLD, 'utf8'));
    lines.push('');
    lines.push(`## 冷缓存（每轮 FLUSHDB 后单次请求，共 ${cold.rounds} 轮）`);
    lines.push('');
    lines.push('| 接口 | p50 | p95 | p99 | max | 失败 |');
    lines.push('|---|---|---|---|---|---|');
    for (const rec of Object.values(cold.results)) {
      lines.push(
        `| ${rec.label} | ${n(rec.p50_ms)} ms | ${n(rec.p95_ms)} ms | ${n(rec.p99_ms)} ms | ${n(rec.max_ms)} ms | ${rec.failures} |`,
      );
    }
  }

  lines.push('');
  lines.push('## 原始明细');
  lines.push('');
  lines.push('```');
  for (const [key] of ORDER) {
    const r = sums[key];
    if (!r) continue;
    lines.push(
      `${key.padEnd(16)} reqs=${String(r.requests).padStart(5)} rps=${n(r.rps, 1).padStart(5)} dropped=${r.dropped_iterations} ` +
        `p50=${n(r.p50_ms).padStart(7)} p95=${n(r.p95_ms).padStart(7)} p99=${n(r.p99_ms).padStart(7)} checks=${n(r.checks_rate)}%`,
    );
  }
  lines.push('```');

  fs.mkdirSync(path.dirname(OUT), { recursive: true });
  fs.writeFileSync(OUT, lines.join('\n') + '\n');
  console.log(lines.join('\n'));
  console.log(`\n# 已写入 ${path.relative(REPO_ROOT, OUT)}`);
}

main();
