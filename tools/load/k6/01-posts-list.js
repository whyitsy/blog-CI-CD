// ============================================================================
// 01 · GET /api/posts —— 首页列表
//
// 【这个脚本回答什么问题】
//   首页卡片列表是全站访问量最大的读路径（前端首屏必调）。这里要回答：
//     ① 在 1,000 篇数据、恒定 10 RPS 下，列表接口的 p50 / p95 / p99 是多少？
//     ② 带分页（Skip/Take → SQL OFFSET）时，深页是否比第一页明显更慢？
//        （列表只投影卡片字段、**不加载 Content**，所以"快"是应该的；
//          "慢"说明 Count + 排序 + OFFSET 出了问题）
//
// 【怎么跑】
//   k6 run tools/load/k6/01-posts-list.js
//   # 额外测深分页（第 20 页，单独一组阈值，不污染第一页的 p95）：
//   k6 run -e DEEP_PAGES=20 tools/load/k6/01-posts-list.js
//
// 【读结果时注意】
//   `dropped_iterations > 0` 表示系统慢到 k6 都排不出请求了 —— 这本身就是结论，
//   不要只看延迟数字。业务错误看 `business_errors`（code !== 0），不是 `http_req_failed`。
// ============================================================================

import http from 'k6/http';
import { baseOptions, expectOk, reqParams, summarize, TREND_STATS, OUT_DIR, BASE_URL, SLO } from './lib/common.js';

const EP = 'posts-list';
const DEEP_PAGES = Number(__ENV.DEEP_PAGES || 0);
const RATE = Number(__ENV.RATE || 10);
const DURATION = __ENV.DURATION || '30s';

const base = baseOptions({ endpoint: EP, p95: SLO.list, rate: RATE, duration: DURATION });

// 深分页单独一个场景、单独一组阈值：混在一起算 p95 会把两个问题糊成一个数字
const scenarios = {
  first_page: Object.assign({}, base.scenarios.constant, { exec: 'firstPage' }),
};
const thresholds = Object.assign({}, base.thresholds);

if (DEEP_PAGES > 0) {
  scenarios.deep_page = Object.assign({}, base.scenarios.constant, {
    rate: Number(__ENV.DEEP_RATE || 2),
    tags: { endpoint: `${EP}-deep` },
    exec: 'deepPage',
  });
  thresholds[`http_req_failed{endpoint:${EP}-deep}`] = ['rate<0.01'];
  thresholds[`business_errors{endpoint:${EP}-deep}`] = ['rate<0.01'];
  thresholds[`http_req_duration{endpoint:${EP}-deep}`] = [`p(95)<${SLO.list}`];
  thresholds[`http_reqs{endpoint:${EP}-deep}`] = ['count>0'];
}

export const options = {
  summaryTrendStats: TREND_STATS,
  scenarios,
  thresholds,
};

/** 首页首屏：page=1、pageSize=12（前端默认值） */
export function firstPage() {
  const res = http.get(`${BASE_URL}/api/posts?page=1&pageSize=12`, reqParams(EP));
  expectOk(res, EP, 'posts-list');
}

/** 深分页：page=N，验证 OFFSET 的代价是否随页码增长 */
export function deepPage() {
  const ep = `${EP}-deep`;
  const res = http.get(`${BASE_URL}/api/posts?page=${DEEP_PAGES}&pageSize=12`, reqParams(ep));
  expectOk(res, ep, `posts-list-page${DEEP_PAGES}`);
}

export function handleSummary(data) {
  const out = summarize(EP, data, { slo: SLO.list, rate: RATE, duration: DURATION });
  if (DEEP_PAGES > 0) {
    const key = `${EP}-deep`;
    const deep = summarize(key, data, { slo: SLO.list, rate: Number(__ENV.DEEP_RATE || 2), duration: DURATION });
    out.stdout += deep.stdout;
    out[`${OUT_DIR}/${key}-summary.json`] = deep[`${OUT_DIR}/${key}-summary.json`];
  }
  return out;
}
