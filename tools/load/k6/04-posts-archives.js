// ============================================================================
// 04 · GET /api/posts/archives —— 归档
//
// 【这个脚本回答什么问题】
//   这是**唯一一个"响应体随数据量线性增长"**的接口：无参数、不分页，
//   服务端把全部已发布文章拉到内存后按年月分组，一次性返回全部标题
//   （learn/03 §9 热点三 / docs/06 §3.1 的观察点）。
//   要回答：
//     ① 1,000 篇（约 800 篇已发布）时，归档的 p50 / p95 / p99 与**响应体大小**是多少？
//     ② 它的 p95 是否随文章数线性增长 —— 这条假设在 10,000 篇那一轮再判一次。
//
// 【为什么要单独记录响应体大小】
//   延迟只反映"服务端算得快不快"，反映不了"要把多少字节推给用户"。
//   归档接口真正的风险是**传输量**：即使服务端 5ms，1MB 的 JSON 在移动网络下也要几秒。
//   所以这里用自定义 Trend 记录每次响应的 KiB，和延迟一起看。
//
// 【怎么跑】
//   k6 run tools/load/k6/04-posts-archives.js
// ============================================================================

import http from 'k6/http';
import { Trend } from 'k6/metrics';
import { baseOptions, expectOk, reqParams, summarize, BASE_URL, SLO } from './lib/common.js';

const EP = 'posts-archives';
const RATE = Number(__ENV.RATE || 10);
const DURATION = __ENV.DURATION || '30s';

/** 响应体大小（KiB）。归档接口的风险在传输量，不在服务端计算 */
const bodyKib = new Trend('response_body_kib', true);

export const options = baseOptions({ endpoint: EP, p95: SLO.archives, rate: RATE, duration: DURATION });

export default function () {
  const res = http.get(`${BASE_URL}/api/posts/archives`, reqParams(EP));
  const data = expectOk(res, EP, 'posts-archives');
  bodyKib.add(res.body ? res.body.length / 1024 : 0, { endpoint: EP });
  // 归档返回的是数组：顺带校验分组结构没被改坏（这类结构变更最容易在重构里静默发生）
  if (data) {
    if (!Array.isArray(data)) throw new Error('archives 返回的不是数组');
  }
}

export function handleSummary(data) {
  const out = summarize(EP, data, { slo: SLO.archives, rate: RATE, duration: DURATION });
  const kb = data.metrics.response_body_kib;
  if (kb) {
    out.stdout += `  响应体大小 平均 ${kb.values.avg.toFixed(1)} KiB ｜ 最大 ${kb.values.max.toFixed(1)} KiB\n`;
  }
  return out;
}
