// ============================================================================
// 02 · GET /api/posts/{id} —— 文章详情（**会触发浏览量写入**）
//
// 【这个脚本回答什么问题】
//   详情页是全站成本最高的读路径：它加载完整 Content（本项目造的样本每篇 3KB 级），
//   并且**每次调用都会让 ViewCount + 1**（数据库侧原子自增，跳过乐观锁）。
//   要回答：
//     ① 详情接口在 10 RPS 下的 p50 / p95 / p99 是多少？
//     ② 浏览量自增在"分散到多篇文章"时是否成为瓶颈？
//        （learn/03 §9 热点二 / docs/06 §3.2 实验 C：同一行的 UPDATE 会加行级排他锁）
//
// 【同一篇 vs 多篇：这是本脚本的关键开关】
//   默认在 setup() 里取 100 个不同的 id 轮流打 —— 模拟真实的"长尾访问分布"。
//   `-e SAME_POST=1` 则只打同一篇，用来复现"热门文章"的行锁串行化。
//   两组对比就能回答实验 C：**p99 是否在单篇时显著恶化**。
//
// 【怎么跑】
//   k6 run tools/load/k6/02-post-detail.js
//   k6 run -e SAME_POST=1 tools/load/k6/02-post-detail.js      # 热门文章对照组
//
// ⚠️ 这个脚本会真的改数据（ViewCount）。压完想恢复计数，从库里自行 UPDATE 即可；
//    它**不会**新增或删除文章，所以不需要 --clean。
// ============================================================================

import http from 'k6/http';
import { baseOptions, expectOk, reqParams, summarize, BASE_URL, SLO } from './lib/common.js';

const EP = 'posts-detail';
const RATE = Number(__ENV.RATE || 10);
const DURATION = __ENV.DURATION || '30s';
const SAME_POST = __ENV.SAME_POST === '1';
const POOL_SIZE = Number(__ENV.POOL_SIZE || 100);

export const options = baseOptions({ endpoint: EP, p95: SLO.detail, rate: RATE, duration: DURATION });

/** 取一批真实存在的文章 id（列表按发布时间倒序，前 N 篇就是"最常被访问"的那批） */
export function setup() {
  const res = http.get(`${BASE_URL}/api/posts?page=1&pageSize=50`, { tags: { endpoint: 'setup-ids' } });
  const body = res.json();
  if (!body || body.code !== 0 || !body.data || body.data.items.length === 0) {
    throw new Error('取文章 id 失败：请先跑 tools/load/seed-posts.mjs 造数据');
  }
  const ids = body.data.items.map((x) => x.id);
  // pageSize 上限公开是 50，用 keyword 为空的多页把它凑到 POOL_SIZE
  let page = 2;
  while (ids.length < POOL_SIZE && page <= 5) {
    const more = http.get(`${BASE_URL}/api/posts?page=${page}&pageSize=50`, { tags: { endpoint: 'setup-ids' } });
    const b = more.json();
    if (!b || b.code !== 0 || !b.data || b.data.items.length === 0) break;
    for (const it of b.data.items) ids.push(it.id);
    page++;
  }
  return { ids: SAME_POST ? [ids[0]] : ids.slice(0, POOL_SIZE) };
}

export default function (data) {
  // 分散打：按迭代序号轮流选 id；单篇打：永远第一个
  const id = data.ids[(__ITER + __VU) % data.ids.length];
  const res = http.get(`${BASE_URL}/api/posts/${id}`, reqParams(EP));
  expectOk(res, EP, 'posts-detail');
}

export function handleSummary(data) {
  return summarize(EP, data, { slo: SLO.detail, rate: RATE, duration: DURATION });
}
