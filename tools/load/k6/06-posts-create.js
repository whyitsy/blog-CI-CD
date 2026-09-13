// ============================================================================
// 06 · POST /api/posts —— 写路径（**低并发、高成本**）
//
// 【这个脚本回答什么问题】
//   写文章是六个接口里唯一会**改数据**的：它要跑完整业务链路
//   （鉴权 → 归属校验 → 摘要规则 → 写库 → 失效多组缓存），
//   并且创建即发布时还会让**归档、列表、站点统计**的缓存一起失效。
//   要回答：
//     ① 单篇创建的 p50 / p95 / p99 是多少（SLO 草案 500ms）？
//     ② 写操作对**读路径**的连带影响有多大（写完立刻读，缓存已失效）
//        —— 这是"发布一篇文章会不会让全站抖一下"的直接证据。
//
// 【为什么必须低并发】
//   写路径的载荷不该按读路径的 RPS 设定：真实个人博客一天也发不了几篇。
//   默认 0.5 RPS（2 秒一篇），30 秒约 15 篇 —— 足以测出单次成本与缓存失效代价，
//   又不会把数据库写成写入基准。**用写 RPS 去衡量容量是常见的误用。**
//
// 【怎么跑】
//   k6 run tools/load/k6/06-posts-create.js
//   k6 run -e RATE=2 -e DURATION=10s tools/load/k6/06-posts-create.js   # 故意加压看拐点
//
// ⚠️ 本脚本**会真的新增文章**，标题统一带 `[LOADTEST]` 前缀。清理：
//      node tools/load/seed-posts.mjs --only-clean
//    （注意 --only-clean 会连造数据一起删；只想删它们就照 README 里的 SQL）
// ============================================================================

import http from 'k6/http';
import { baseOptions, expectOk, reqParams, summarize, login, BASE_URL, SLO } from './lib/common.js';

const EP = 'posts-create';
const RATE = Number(__ENV.RATE || 0.5); // 每 2 秒一篇：写路径刻意用低载荷
const DURATION = __ENV.DURATION || '30s';

const base = baseOptions({ endpoint: EP, p95: SLO.create, rate: RATE, duration: DURATION, preVUs: 2, maxVUs: 20 });
// 写后立刻读的观测点也要有阈值，否则它不会出现在摘要里（k6 只导出被 thresholds 引用的子指标）
base.thresholds['http_reqs{endpoint:posts-list-after-write}'] = ['count>=0'];
export const options = base;

/** 正文用一段有真实长度的中文 Markdown：写路径的成本与正文长度正相关 */
const CONTENT = `# 压测写入样本

这是由 tools/load/k6/06-posts-create.js 创建的压测文章，用于测量写路径成本。

## 观察点

- 创建即发布会让归档 / 列表 / 站点统计的缓存失效
- 摘要留空时后端按正文前 50 字自动生成
- 写入走乐观锁，但创建不涉及版本冲突

## 说明

本段文字刻意保持中等长度（数百字节），让序列化与全文检索生成列
（SearchVector 由 zhparser 分词后写入 GIN 索引）都产生真实开销。
`;

export function setup() {
  return { token: login() };
}

export default function (data) {
  const n = `${__VU}-${__ITER}-${Date.now()}`;
  const payload = JSON.stringify({
    title: `[LOADTEST] k6 写入样本 ${n}`,
    content: CONTENT,
    summary: '',
    coverImage: '',
    categoryId: null,
    tagIds: null,
    collectionIds: null,
    publish: true,
  });

  const res = http.post(`${BASE_URL}/api/posts`, payload, {
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${data.token}` },
    tags: { endpoint: EP },
  });

  const created = expectOk(res, EP, 'posts-create');
  if (created) {
    // 写完立刻读一次（不计入本脚本的 SLO 指标，只作为"缓存失效代价"的旁证）
    http.get(`${BASE_URL}/api/posts?page=1&pageSize=12`, { tags: { endpoint: 'posts-list-after-write' } });
  }
}

export function handleSummary(data) {
  const out = summarize(EP, data, { slo: SLO.create, rate: RATE, duration: DURATION });
  const after = data.metrics['http_req_duration{endpoint:posts-list-after-write}'];
  if (after) {
    out.stdout += `  写后立刻读列表（冷缓存）：p50 ${after.values.med.toFixed(2)}ms ｜ p95 ${after.values['p(95)'].toFixed(2)}ms\n`;
  }
  if (__ENV.RATE === undefined) {
    out.stdout += `  ⚠️ 本次新增了约 ${Math.round(RATE * Number(String(DURATION).replace('s', '')))} 篇 [LOADTEST] 文章，清理见 tools/load/README.md\n`;
  }
  return out;
}
