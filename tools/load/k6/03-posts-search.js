// ============================================================================
// 03 · GET /api/posts/search —— 中文全文检索
//
// 【这个脚本回答什么问题】
//   这是六个接口里**计算路径最长**的一个，也是 SLO 草案里唯一放宽到 800ms 的：
//     GIN 索引匹配（zhparser 分词）→ CTE 里算 ts_rank → 把命中的**全部 id 拉回应用**
//     → 再用 EF 做 Count + 关联投影 + 分页重排。
//   要回答：
//     ① 命中量大（宽泛词，如"缓存"命中 900+ 篇）时慢到什么程度？p99 是多少？
//     ② 命中量小（选择性词）时是否明显更快 —— 即"代价是否随命中数增长"。
//
// 【为什么关键词要轮流换】
//   只打一个词等于只测了一条固定的查询计划。脚本按迭代序号轮换
//   `宽泛词 → 中等词 → 生僻词`，于是延迟分布里同时包含"贵查询"和"便宜查询"，
//   p99 才代表真实用户搜到宽泛词时的体验。
//
// 【怎么跑】
//   k6 run tools/load/k6/03-posts-search.js
//   k6 run -e KEYWORDS=缓存,索引 tools/load/k6/03-posts-search.js
//
// ⚠️ search 是**全站最严格的限流规则**（容量 20、速率 2/秒，docs/03 §4.5）。
//    没关限流时这个脚本必然大面积 429 —— 那不是应用慢，是限流器在工作。
//    关闭方法见 tools/load/README.md。
// ============================================================================

import http from 'k6/http';
import { baseOptions, expectOk, reqParams, summarize, BASE_URL, SLO } from './lib/common.js';

const EP = 'posts-search';
const RATE = Number(__ENV.RATE || 10);
const DURATION = __ENV.DURATION || '30s';

/** 宽泛词（覆盖压测样本的正文主题）→ 中等词 → 生僻词，模拟真实搜索的混合分布 */
const KEYWORDS = (__ENV.KEYWORDS || '缓存,索引,查询计划,线程池,ts_rank').split(',');

export const options = baseOptions({ endpoint: EP, p95: SLO.search, rate: RATE, duration: DURATION });

export default function () {
  const kw = KEYWORDS[__ITER % KEYWORDS.length];
  const res = http.get(`${BASE_URL}/api/posts/search?keyword=${encodeURIComponent(kw)}&page=1&pageSize=12`, reqParams(EP));
  expectOk(res, EP, `search:${kw}`);
}

export function handleSummary(data) {
  return summarize(EP, data, { slo: SLO.search, rate: RATE, duration: DURATION });
}
