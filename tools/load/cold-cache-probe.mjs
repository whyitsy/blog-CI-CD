#!/usr/bin/env node
// ============================================================================
// 冷缓存探针：**每轮请求前清空 Redis**，于是每一次测的都是"缓存未命中"的真实成本
//
// 用法：
//   node tools/load/cold-cache-probe.mjs                 # 默认 20 轮
//   node tools/load/cold-cache-probe.mjs --rounds 10 --endpoints config,archives,list,detail
//   node tools/load/cold-cache-probe.mjs --json tools/load/results/raw/cold-cache.json
//
// 为什么不能用 k6 测冷缓存（learn/03 §7.2 坑 4）：
//   恒定载荷下**只有第一个请求是冷的**，后面全是热缓存，p95 会被热缓存淹没，
//   得出严重乐观的结论。k6 本身不能执行外部命令（清 Redis），
//   所以冷缓存必须由"能同时操作 Redis 和 HTTP 的探针"来测 —— 就是这个脚本。
//
// 为什么用 20 轮而不是 1 次：
//   单次测量包含 JIT 预热、连接建立、操作系统缓存等一次性成本。
//   20 轮清缓存后的**首次**请求，才能给出"冷路径"的 p50/p95。
//
// ⚠️ 它会 FLUSHDB 整库缓存。压测环境专用；别对着有人正在用的环境跑。
// ============================================================================

import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import fs from 'node:fs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HERE, '..', '..');
const BASE_URL = process.env.BASE_URL || 'http://127.0.0.1:8080';
const REDIS_SERVICE = process.env.LOADTEST_REDIS_SERVICE || 'redis';

const args = process.argv.slice(2);
const getArg = (name, dflt) => {
  const i = args.indexOf(name);
  return i >= 0 && args[i + 1] ? args[i + 1] : dflt;
};
const ROUNDS = Number(getArg('--rounds', '20'));
const JSON_OUT = getArg('--json', 'tools/load/results/raw/cold-cache.json');
const ONLY = getArg('--endpoints', 'config,archives,list,detail,search').split(',');

/** 每次都清库，所以每个 key 都必须重新回源 */
function flushRedis() {
  return new Promise((resolve, reject) => {
    const child = spawn('docker', ['compose', 'exec', '-T', REDIS_SERVICE, 'redis-cli', 'FLUSHDB'], {
      cwd: REPO_ROOT,
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    let err = '';
    child.stderr.on('data', (d) => (err += d));
    child.on('close', (code) => (code === 0 ? resolve() : reject(new Error(`FLUSHDB 失败：${err}`))));
  });
}

/** 单次请求 + 读完整响应体（只测到响应头是不够的：慢可能慢在传输/序列化） */
async function timedGet(url) {
  const t0 = performance.now();
  const res = await fetch(url);
  const text = await res.text();
  const ms = performance.now() - t0;
  let code = null;
  try {
    code = JSON.parse(text).code;
  } catch {
    code = null;
  }
  return { ms, status: res.status, code, bytes: Buffer.byteLength(text) };
}

function percentile(sorted, p) {
  if (sorted.length === 0) return null;
  const idx = Math.min(sorted.length - 1, Math.max(0, Math.ceil((p / 100) * sorted.length) - 1));
  return sorted[idx];
}

const round2 = (v) => (v === null ? null : Math.round(v * 100) / 100);

async function main() {
  // 详情接口需要一篇真实存在的文章 id
  const listRes = await fetch(`${BASE_URL}/api/posts?page=1&pageSize=1`);
  const listBody = await listRes.json();
  const postId = listBody?.data?.items?.[0]?.id;
  if (!postId) throw new Error('取不到文章 id：先跑 tools/load/seed-posts.mjs 造数据');

  const targets = {
    // site/config：5 个聚合查询（含两个全表 SUM），冷缓存是首屏最慢的一环
    config: { label: 'GET /api/site/config', url: `${BASE_URL}/api/site/config` },
    // archives：无分页 + 全量内存分组，响应体随文章数线性增长
    archives: { label: 'GET /api/posts/archives', url: `${BASE_URL}/api/posts/archives` },
    // list：第一页
    list: { label: 'GET /api/posts?page=1', url: `${BASE_URL}/api/posts?page=1&pageSize=12` },
    // detail：详情（会触发浏览量写入）
    detail: { label: `GET /api/posts/{id}`, url: `${BASE_URL}/api/posts/${postId}` },
    // search：全文检索（GIN + ts_rank + CTE）
    search: { label: 'GET /api/posts/search?keyword=缓存', url: `${BASE_URL}/api/posts/search?keyword=${encodeURIComponent('缓存')}` },
  };

  const results = {};
  console.log(`# 冷缓存探针：每轮 FLUSHDB 后请求一次，共 ${ROUNDS} 轮`);
  console.log(`# 目标 ${BASE_URL}｜Redis 服务 ${REDIS_SERVICE}`);
  console.log('');

  for (const key of ONLY) {
    const t = targets[key];
    if (!t) continue;
    const samples = [];
    let failures = 0;
    for (let i = 0; i < ROUNDS; i++) {
      // 每轮都清：不清的话第二次起就是热缓存，测到的是另一个东西
      await flushRedis();
      const r = await timedGet(t.url);
      if (r.status !== 200 || r.code !== 0) failures++;
      samples.push(r.ms);
    }
    const sorted = [...samples].sort((a, b) => a - b);
    const rec = {
      endpoint: key,
      label: t.label,
      rounds: ROUNDS,
      failures,
      min_ms: round2(sorted[0]),
      p50_ms: round2(percentile(sorted, 50)),
      p95_ms: round2(percentile(sorted, 95)),
      p99_ms: round2(percentile(sorted, 99)),
      max_ms: round2(sorted[sorted.length - 1]),
      avg_ms: round2(samples.reduce((a, b) => a + b, 0) / samples.length),
    };
    results[key] = rec;
    console.log(
      `  ${rec.label.padEnd(38)} p50 ${String(rec.p50_ms).padStart(7)}ms ｜ p95 ${String(rec.p95_ms).padStart(7)}ms ｜ p99 ${String(rec.p99_ms).padStart(7)}ms ｜ 失败 ${failures}`,
    );
  }

  if (JSON_OUT) {
    const outPath = path.resolve(REPO_ROOT, JSON_OUT);
    fs.mkdirSync(path.dirname(outPath), { recursive: true });
    fs.writeFileSync(outPath, JSON.stringify({ base_url: BASE_URL, rounds: ROUNDS, results }, null, 2));
    console.log(`\n# 明细已写入 ${JSON_OUT}`);
  }
}

main().catch((err) => {
  console.error(`❌ ${err.message}`);
  process.exit(1);
});
