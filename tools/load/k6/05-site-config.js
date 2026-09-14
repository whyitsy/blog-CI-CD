// ============================================================================
// 05 · GET /api/site/config —— 站点配置（**冷 / 热缓存差异最大的一环**）
//
// 【这个脚本回答什么问题】
//   站点配置对应 5 个聚合查询（其中两个是全表 SUM，见 learn/03 §9 热点四），
//   结果被 Redis 缓存。所以它有两个数量级不同的答案：
//       热缓存：一次 GET，几乎不碰数据库
//       冷缓存：5 个聚合查询串行执行（发布文章会让统计缓存失效，重启/扩容/TTL 到期同理）
//   要回答：
//     ① 热缓存下的 p50 / p95 / p99（默认，脚本直接打）
//     ② 冷缓存下的首字节时间（用 `-e COLD=1` 或 tools/load/cold-cache-probe.mjs）
//
// 【为什么 COLD=1 要单独处理】
//   恒定载荷下"只有第一个请求是冷的"，其余全是热缓存 —— 直接跑 30 秒，
//   p95 会被热缓存淹没，得出严重乐观的结论（learn/03 §7.2 的坑 4）。
//   所以 COLD=1 时本脚本**每次请求前都清 Redis**（通过 k6 每轮一个小间隔的
//   arrival-rate 场景 + run-baseline.sh 里的独立探针配合；k6 本身不能执行外部命令，
//   因此真正的冷缓存测量由 cold-cache-probe.mjs 完成，这里只标注期望值）。
//
// 【怎么跑】
//   k6 run tools/load/k6/05-site-config.js                     # 热缓存基线
//   node tools/load/cold-cache-probe.mjs                       # 冷缓存基线（清 Redis 后单次请求）
// ============================================================================

import http from 'k6/http';
import { baseOptions, expectOk, reqParams, summarize, BASE_URL, SLO } from './lib/common.js';

const EP = 'site-config';
const RATE = Number(__ENV.RATE || 10);
const DURATION = __ENV.DURATION || '30s';

export const options = baseOptions({ endpoint: EP, p95: SLO.config, rate: RATE, duration: DURATION });

export default function () {
  const res = http.get(`${BASE_URL}/api/site/config`, reqParams(EP));
  const data = expectOk(res, EP, 'site-config');
  // 顺带校验契约：这个接口是首屏阻塞项，字段缺失会让前端静默渲染空白
  if (data && !data.versions) throw new Error('site/config 缺少 versions 字段');
}

export function handleSummary(data) {
  return summarize(EP, data, { slo: SLO.config, rate: RATE, duration: DURATION });
}
