// ============================================================================
// 00 · B0 冒烟：10 RPS × 30 秒（docs/06 §3.1 的分阶段执行里的第一步）
//
// 【这个脚本回答什么问题】
//   "现在这套环境能不能开始压？"——冒烟不是性能结论，它是**前置门禁**：
//     ① /health 是否 200（learn/03 坑 7：容器启动 ≠ 可连接，压测前必须等服务就绪）
//     ② 首页列表在 10 RPS 这个**很小的载荷**下是否 100% 成功、延迟是否稳定
//     ③ 有没有 429（限流没关）/ 5xx（服务端故障）这类"假故障"
//   冒烟不过，后面 B1 基线的数字全部作废 —— 那是"环境问题"不是"性能问题"。
//
// 【为什么同时打 /health 和首页】
//   /health 会真实校验 PG + zhparser + 迁移 + Redis（docs/05 §8.4），
//   它单独跑是一条"依赖健康"的证据；和列表一起跑还能看出探针是否被业务流量拖慢。
//
// 【怎么跑】
//   k6 run tools/load/k6/00-smoke.js
//   # 冒烟失败时打开 VERBOSE 看首个失败样本的响应体：
//   k6 run -e VERBOSE=1 tools/load/k6/00-smoke.js
// ============================================================================

import http from 'k6/http';
import { check } from 'k6';
import { baseOptions, expectOk, reqParams, summarize, BASE_URL, SLO } from './lib/common.js';

const EP = 'smoke';
const RATE = Number(__ENV.RATE || 10); // 与 docs/06 的 "bombardier 10 RPS × 30 秒" 对齐
const DURATION = __ENV.DURATION || '30s';

export const options = baseOptions({ endpoint: EP, p95: SLO.list, rate: RATE, duration: DURATION });

/** 冒烟前置：/health 必须 200 且返回 Healthy（依赖全部就绪才继续） */
export function setup() {
  const res = http.get(`${BASE_URL}/health`, { tags: { endpoint: 'health-setup' } });
  check(res, {
    'health: HTTP 200': (r) => r.status === 200,
    'health: Healthy': (r) => String(r.body).indexOf('Healthy') >= 0,
  });
  if (res.status !== 200) {
    throw new Error(`/health 未就绪（HTTP ${res.status}）：先等容器 healthy 再压测`);
  }
  return {};
}

export default function () {
  const res = http.get(`${BASE_URL}/api/posts?page=1&pageSize=12`, reqParams(EP));
  expectOk(res, EP, 'smoke-posts-list');
}

export function handleSummary(data) {
  const out = summarize(EP, data, { slo: SLO.list, rate: RATE, duration: DURATION });
  out.stdout += `  冒烟结论：${data.metrics.checks.values.rate === 1 ? '✅ 环境可用，可以进入 B1 基线' : '❌ 有检查未通过，先修环境再谈性能'}\n`;
  return out;
}
