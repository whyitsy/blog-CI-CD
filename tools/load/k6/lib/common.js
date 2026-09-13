// ============================================================================
// k6 公共库：BASE_URL、SLO 阈值、统一响应体校验、结果摘要
//
// 为什么要有这个文件（learn/03 §8 的三个"必须写对的地方"）：
//   ① thresholds 里写 SLO —— 让"够不够快"变成跑完自动判定的断言，而不是文档里的一句话
//   ② 解析响应体里的 code —— 本项目统一响应体是 {code,message,data}，
//      **HTTP 200 也可能是业务失败**，只看状态码会把错误率系统性低估
//   ③ BASE_URL 可配置 —— 同一个脚本能指向本地 compose、测试环境、生产
//
// 还有一个本文件特有的取舍：**所有指标都按 endpoint 打标签**。
//   `http_req_duration` 是全局指标，如果把 login / setup 的请求也算进去，
//   写接口的 p95 会被 Pbkdf2 哈希（210,000 次迭代）污染。
//   所以每个脚本只对自己那条请求打 `endpoint` 标签，
//   thresholds 与摘要都只看 `http_req_duration{endpoint:xxx}` 这个子指标。
// ============================================================================

import http from 'k6/http';
import { check } from 'k6';
import { Rate, Counter } from 'k6/metrics';

// ---------------------------------------------------------------- 配置

/** 被压目标。默认打 nginx 暴露的 8080（= 端到端，含反代这一跳） */
export const BASE_URL = __ENV.BASE_URL || 'http://127.0.0.1:8080';

/** 摘要 JSON 落盘目录（相对执行 k6 时的 CWD，run-baseline.sh 在仓库根执行） */
export const OUT_DIR = __ENV.OUT_DIR || 'tools/load/results/raw';

/**
 * 摘要里必须带上 p50/p95/p99 —— 平均值会掩盖长尾（learn/03 §4.1：
 * 99 个 10ms + 1 个 9910ms，平均值只有 109ms，看起来"性能很好"）。
 */
export const TREND_STATS = ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'];

/** SLO 默认值 = docs/06 §3.1 的草案；可用 -e SLO_P95_MS=xxx 覆盖做对比 */
export const SLO = {
  list: Number(__ENV.SLO_P95_LIST || 300),
  detail: Number(__ENV.SLO_P95_DETAIL || 300),
  search: Number(__ENV.SLO_P95_SEARCH || 800),
  archives: Number(__ENV.SLO_P95_ARCHIVES || 300), // 草案没单列，按"其它读接口"同档
  config: Number(__ENV.SLO_P95_CONFIG || 300),
  create: Number(__ENV.SLO_P95_CREATE || 500),
};

/** 业务错误（code !== 0）与 HTTP 失败分开统计：前者是"假成功"，后者是传输/服务端故障 */
export const businessErrors = new Rate('business_errors');
export const businessFailures = new Counter('business_failures');

// ---------------------------------------------------------------- options 工厂

/**
 * 构造恒定载荷的 options。
 *
 * 为什么用 constant-arrival-rate 而不是 constant-vus：
 *   我们要的是"在 10 RPS 这个**确定的流量**下延迟是多少"。
 *   constant-vus 的 RPS 会随系统变慢而自动下降（VU 被阻塞），
 *   于是"系统越慢 → 压力越小"，测出来的延迟分布是失真的。
 *   到达速率恒定才能让"变慢"如实反映在延迟与 dropped_iterations 上。
 *
 * @param {object} o
 * @param {string} o.endpoint  指标标签（同时决定子指标名）
 * @param {number} o.p95       SLO：p95 上限（ms）
 * @param {number} [o.rate]    每秒请求数（恒定载荷）
 * @param {string} [o.duration] 持续时间
 * @param {number} [o.preVUs]  预分配 VU
 * @param {number} [o.maxVUs]  最大 VU（延迟恶化时 k6 需要更多 VU 才能维持到达速率）
 * @param {number} [o.p99]     可选：额外卡一条 p99 阈值
 */
export function baseOptions(o) {
  const rawRate = o.rate !== undefined ? o.rate : Number(__ENV.RATE || 10);
  // ⚠️ k6 的 constant-arrival-rate 里 `rate` **必须是正整数**：
  //    写路径想跑 0.5 RPS（2 秒一篇）不能写 rate: 0.5（会在解析 options 时直接失败：
  //    `cannot unmarshal number 0.5 into Go struct field Options.scenarios.rate of type int64`），
  //    正确写法是 `rate: 1` + `timeUnit: '2s'`。
  const rate = rawRate >= 1 ? Math.round(rawRate) : 1;
  const timeUnit = rawRate >= 1 ? '1s' : `${Math.round(1 / rawRate)}s`;
  const duration = o.duration !== undefined ? o.duration : __ENV.DURATION || '30s';
  const preVUs = o.preVUs !== undefined ? o.preVUs : Number(__ENV.PRE_VUS || 10);
  const maxVUs = o.maxVUs !== undefined ? o.maxVUs : Number(__ENV.MAX_VUS || 100);

  const thresholds = {
    // 只看本脚本的主请求：HTTP 层失败率
    [`http_req_failed{endpoint:${o.endpoint}}`]: ['rate<0.01'],
    // 业务失败率（code !== 0）—— 统一响应体下这才是真实的错误率
    [`business_errors{endpoint:${o.endpoint}}`]: ['rate<0.01'],
    // SLO：p95
    [`http_req_duration{endpoint:${o.endpoint}}`]: [`p(95)<${o.p95}`],
    // 请求计数：确认"真的打出去了"。
    // 顺带一个副作用很关键 —— k6 只为**被 thresholds 引用**的标签子指标生成摘要条目，
    // 不写这条阈值，`http_reqs{endpoint:x}` 在 handleSummary 里就是空的（请求数显示 0）。
    [`http_reqs{endpoint:${o.endpoint}}`]: ['count>0'],
    // 通用检查（响应结构、code===0）必须全过
    checks: ['rate>0.99'],
  };
  if (o.p99) {
    thresholds[`http_req_duration{endpoint:${o.endpoint}}`].push(`p(99)<${o.p99}`);
  }

  return {
    // 摘要里必须带上 p(50)/p(95)/p(99)：平均值会掩盖长尾（learn/03 §4.1）
    summaryTrendStats: TREND_STATS,
    discardResponseBodies: false, // 要解析 code，不能丢响应体
    scenarios: {
      constant: {
        executor: 'constant-arrival-rate',
        rate,
        timeUnit,
        duration,
        preAllocatedVUs: preVUs,
        maxVUs,
        tags: { endpoint: o.endpoint },
      },
    },
    thresholds,
  };
}

// ---------------------------------------------------------------- 响应校验

/**
 * 校验统一响应体：HTTP 200 **且** code === 0。
 * 返回解析后的 data（解析失败返回 null，不会抛异常打断压测）。
 */
export function expectOk(res, endpoint, label) {
  let body = null;
  try {
    body = res.json();
  } catch (e) {
    body = null;
  }

  const okHttp = res.status === 200;
  const okCode = body !== null && body.code === 0;

  check(res, {
    [`${label}: HTTP 200`]: () => okHttp,
    [`${label}: code === 0`]: () => okCode,
  });

  // 业务错误单独计数：HTTP 429（限流）与 code !== 0 都算
  const failed = !okHttp || !okCode;
  businessErrors.add(failed, { endpoint });
  if (failed) {
    businessFailures.add(1, { endpoint });
    // 排障用：把首个失败样本打出来（限流 429 / 业务错误码一眼可见）
    if (__ENV.VERBOSE) {
      console.error(`[${label}] status=${res.status} body=${String(res.body).slice(0, 200)}`);
    }
  }
  return okCode ? body.data : null;
}

/** 给请求加统一的 tag，保证子指标名与 options 里声明的一致 */
export function reqParams(endpoint, extra) {
  return { tags: Object.assign({ endpoint }, extra || {}) };
}

// ---------------------------------------------------------------- 取 token（写接口用）

/**
 * 登录拿 Admin token。密码来自环境变量，默认用文档里的开发种子账号（docs/05 §5.2）。
 * 只在 setup() 里调用一次 —— 不进入被测量的请求延迟。
 */
export function login() {
  const email = __ENV.ADMIN_EMAIL || 'admin@example.com';
  const password = __ENV.ADMIN_PASSWORD || 'Admin@12345';
  const res = http.post(`${BASE_URL}/api/auth/login`, JSON.stringify({ email, password }), {
    headers: { 'Content-Type': 'application/json' },
    tags: { endpoint: 'login-setup' },
  });
  if (res.status !== 200) {
    throw new Error(`登录失败：HTTP ${res.status} ${String(res.body).slice(0, 200)}`);
  }
  const body = res.json();
  if (!body || body.code !== 0 || !body.data || !body.data.token) {
    throw new Error(`登录响应缺少 token：${String(res.body).slice(0, 200)}`);
  }
  return body.data.token;
}

// ---------------------------------------------------------------- 结果摘要

function pick(metrics, key) {
  const m = metrics[key];
  return m && m.values ? m.values : null;
}

/**
 * 生成"人能读"的摘要：一行一个关键数字，p50/p95/p99 齐全。
 * 同时把同一份数据写成 JSON，交给 run-baseline.sh 汇总成报告里的表格。
 */
export function summarize(endpoint, data, extra) {
  const dur = pick(data.metrics, `http_req_duration{endpoint:${endpoint}}`) || pick(data.metrics, 'http_req_duration') || {};
  const failed = pick(data.metrics, `http_req_failed{endpoint:${endpoint}}`) || {};
  const biz = pick(data.metrics, `business_errors{endpoint:${endpoint}}`) || {};
  const reqs = pick(data.metrics, `http_reqs{endpoint:${endpoint}}`) || {};
  const dropped = pick(data.metrics, 'dropped_iterations') || {};
  const checks = pick(data.metrics, 'checks') || {};

  const num = (v, digits = 2) => (v === undefined || v === null ? null : Number(v.toFixed(digits)));

  const record = {
    endpoint,
    script: __ENV.SCRIPT_NAME || endpoint,
    base_url: BASE_URL,
    requests: reqs.count || 0,
    rps: num(reqs.rate),
    dropped_iterations: dropped.count || 0,
    http_failed_rate: num((failed.rate || 0) * 100, 3),
    business_error_rate: num((biz.rate || 0) * 100, 3),
    checks_rate: num((checks.rate || 0) * 100, 2),
    p50_ms: num(dur.med),
    p90_ms: num(dur['p(90)']),
    p95_ms: num(dur['p(95)']),
    p99_ms: num(dur['p(99)']),
    avg_ms: num(dur.avg),
    min_ms: num(dur.min),
    max_ms: num(dur.max),
    slo_p95_ms: extra && extra.slo ? extra.slo : null,
    slo_pass: extra && extra.slo ? dur['p(95)'] < extra.slo : null,
    rate: extra && extra.rate ? extra.rate : null,
    duration: extra && extra.duration ? extra.duration : null,
    passed: true,
  };
  if (extra && extra.slo) record.passed = dur['p(95)'] < extra.slo && (biz.rate || 0) < 0.01 && (failed.rate || 0) < 0.01;

  const lines = [
    '',
    `── ${endpoint} ${'─'.repeat(Math.max(0, 58 - endpoint.length))}`,
    `  请求数 ${record.requests} ｜ 实际 RPS ${record.rps} ｜ 丢弃迭代 ${record.dropped_iterations}`,
    `  HTTP 失败率 ${record.http_failed_rate}% ｜ 业务失败率(code!==0) ${record.business_error_rate}% ｜ checks 通过率 ${record.checks_rate}%`,
    `  延迟 p50 ${record.p50_ms}ms ｜ p90 ${record.p90_ms}ms ｜ p95 ${record.p95_ms}ms ｜ p99 ${record.p99_ms}ms ｜ max ${record.max_ms}ms`,
  ];
  if (record.slo_p95_ms) {
    lines.push(`  SLO p95 < ${record.slo_p95_ms}ms ⇒ ${record.passed ? '✅ 通过' : '❌ 未达标'}`);
  }

  const out = {};
  out.stdout = lines.join('\n') + '\n';
  out[`${OUT_DIR}/${endpoint}-summary.json`] = JSON.stringify(record, null, 2);
  return out;
}
