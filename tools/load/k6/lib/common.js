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
//
// ⚠️ 下面 SLO / SLO_P99 / SLO.archivesBodyKiB 三个常量是
//   `docs/06-技术债与待办.md` §3.1「SLO（2026-09-14 定稿）」表的**代码副本**。
//   两处必须同步：文档改了这里要跟着改，这里改了文档也要跟着改 ——
//   否则"跑完自动判定"判的不是定稿的 SLO，阈值本身成了第三个真相。
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

/**
 * SLO 目标值 = docs/06 §3.1「SLO（2026-09-14 定稿）」表的 p95 列。
 * 每一项都可用 `-e SLO_P95_XXX=xxx` 覆盖 —— 那是**做对比实验**用的
 * （例如故意把目标收紧看余量），不是日常改目标的入口。
 */
export const SLO = {
  list: Number(__ENV.SLO_P95_LIST || 300),
  detail: Number(__ENV.SLO_P95_DETAIL || 300),
  search: Number(__ENV.SLO_P95_SEARCH || 800),
  archives: Number(__ENV.SLO_P95_ARCHIVES || 300), // 定稿新增：归档也在首屏路径上，草案漏了
  config: Number(__ENV.SLO_P95_CONFIG || 300),     // 定稿新增：同上
  create: Number(__ENV.SLO_P95_CREATE || 500),
  /**
   * 归档的**响应体** SLI ≤ 256 KiB（定稿第 ② 条）。
   * 为什么它不是延迟阈值：归档是六接口里唯一"响应体随数据量线性增长"的（无分页 + 全量内存分组），
   * 实测 103.4 KiB @ 796 篇已发布 ≈ 133 B/篇，按线性外推 1 万篇 ≈ 1.3 MiB ——
   * 而它的服务端 p95 只有 3.25 ms，**延迟 SLO 完全看不见这件事**。
   */
  archivesBodyKiB: Number(__ENV.SLO_BODY_ARCHIVES_KIB || 256),
};

/**
 * p99 目标（定稿第 ③ 条）：只写 p95 会漏掉"重启 / 扩容 / TTL 集中到期"这类**最危险的时刻**。
 * 定稿只给详情与检索各一条 ≤ 1 s；**其余接口没有 p99 目标**（不是漏了，是刻意不判）。
 *
 * 为什么按 endpoint 索引、而不是让六个脚本各自传参：补一条阈值不该改六个脚本，
 * 而且"哪个接口有 p99 目标"本来就是 SLO 表的内容，放在这里与文档一一对应。
 */
export const SLO_P99 = {
  'posts-detail': Number(__ENV.SLO_P99_DETAIL || 1000),
  'posts-search': Number(__ENV.SLO_P99_SEARCH || 1000),
};

/** 业务错误（code !== 0）与 HTTP 失败分开统计：前者是"假成功"，后者是传输/服务端故障 */
export const businessErrors = new Rate('business_errors');
export const businessFailures = new Counter('business_failures');

/**
 * 429 单列（定稿的**错误率口径**）。
 * 为什么单列：429 是**限流器在工作**，不是应用故障 —— 混进业务失败率会让
 * "限流忘了关"伪装成"应用出错"，而这两者的处置完全不同（一个改配置，一个查代码）。
 * 但对用户它就是失败，所以仍要**看得见**：下面给它挂一条空断言让它进摘要。
 */
export const http429 = new Rate('http_429');

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
 * @param {number} [o.p99]     可选：额外卡一条 p99 阈值（默认按 SLO_P99[endpoint] 自动取）
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
    // 只看本脚本的主请求：HTTP 层失败率。
    // ⚠️ k6 的 `http_req_failed` 把**所有非 2xx/3xx**都算失败（4xx 也在内），比 docs/06 §3.1
    //    那条"HTTP 5xx 比例 < 0.1%"更严 —— 这是**刻意**的：429 必须让整轮判定失败，
    //    否则"限流没关"那一轮的数字会被当成有效基线；429 的**可见性**由下面那条单列指标负责。
    [`http_req_failed{endpoint:${o.endpoint}}`]: ['rate<0.01'],
    // 业务失败率（code !== 0）—— 统一响应体下这才是真实的错误率
    [`business_errors{endpoint:${o.endpoint}}`]: ['rate<0.01'],
    // 429 单列：`rate>=0` 是**空断言**（永远成立），唯一作用是让 http_429 子指标出现在摘要里
    // —— k6 只为**被 thresholds 引用**的标签子指标生成摘要条目（同下面 http_reqs 那条的说明）。
    // 真正判定它"失败"的是上面的 http_req_failed；这里只负责让它**可见**（定稿要求单列）。
    [`http_429{endpoint:${o.endpoint}}`]: ['rate>=0'],
    // SLO：p95
    [`http_req_duration{endpoint:${o.endpoint}}`]: [`p(95)<${o.p95}`],
    // 请求计数：确认"真的打出去了"。
    // 顺带一个副作用很关键 —— k6 只为**被 thresholds 引用**的标签子指标生成摘要条目，
    // 不写这条阈值，`http_reqs{endpoint:x}` 在 handleSummary 里就是空的（请求数显示 0）。
    [`http_reqs{endpoint:${o.endpoint}}`]: ['count>0'],
    // 通用检查（响应结构、code===0）必须全过
    checks: ['rate>0.99'],
  };
  // SLO：p99（定稿第 ③ 条）。脚本没显式传就按 endpoint 查表，详情与检索各一条 ≤ 1 s。
  const p99 = o.p99 !== undefined ? o.p99 : SLO_P99[o.endpoint];
  if (p99) {
    thresholds[`http_req_duration{endpoint:${o.endpoint}}`].push(`p(99)<=${p99}`);
  }
  // 归档的**响应体** SLI（定稿第 ② 条）。为什么写死 endpoint 判断：
  //   ① 指标 `response_body_kib` 由 04-posts-archives.js 用自定义 Trend 上报
  //      （`res.body.length / 1024`，即 UTF-8 字节数 ÷ 1024，与 DevTools 的传输字节口径一致）；
  //   ② 其它脚本没有这个指标，给它们挂阈值会让 k6 直接报错（阈值引用了不存在的指标）；
  //   ③ 同一个指标名不能在两个文件里各 `new Trend` 一次，所以这里只挂阈值、不重复定义。
  if (o.endpoint === 'posts-archives') {
    thresholds[`response_body_kib{endpoint:${o.endpoint}}`] = [`max<=${SLO.archivesBodyKiB}`];
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
 *
 * ⚠️ 三个失败指标**口径互不重叠**（docs/06 §3.1 的错误率口径）：
 *   `http_req_failed`（k6 内置）→ HTTP 层失败：网络错误 + 4xx/5xx，**429 也在里面**
 *   `http_429`（自定义）        → 限流器在工作，**单列**（不算应用故障，但要让报告看得见）
 *   `business_errors`（自定义） → 拿到了统一响应体、但 `code !== 0`（"HTTP 200 也可能是失败"）
 * 把 429/5xx 混进 `business_errors` 会让"限流忘了关"伪装成"应用出错" —— 两者的处置完全不同。
 */
export function expectOk(res, endpoint, label) {
  let body = null;
  try {
    body = res.json();
  } catch (e) {
    body = null; // 不是 JSON（网关错误页 / 截断的响应）：算 HTTP 失败，但不算"业务失败"
  }

  const okHttp = res.status === 200;
  const hasBody = body !== null && typeof body.code === 'number';
  const okCode = hasBody && body.code === 0;

  check(res, {
    [`${label}: HTTP 200`]: () => okHttp,
    [`${label}: code === 0`]: () => okCode,
  });

  // ⚠️ 429 必须**对每一个请求**都记一次（命中记 1、否则记 0），rate 才是"429 占全部请求的比例"。
  //    只在 429 时 add(1) 会把分母也变成"429 的请求数"，于是永远显示 100%（实测踩到过）。
  http429.add(res.status === 429, { endpoint });
  // 业务失败 = 拿到了统一响应体、但 code !== 0（"HTTP 200 也可能是失败"）。
  // ⚠️ 必须显式排除 429：限流器的响应体是 `{"code":4091,"message":"请求过于频繁…"}`，
  //    也是一个非 0 的 code —— 不排除就会让"限流忘了关"同时污染业务失败率，
  //    而 docs/06 §3.1 的口径要求 429 **单列**（它不是应用故障，但对用户就是失败）。
  //    分母 = 全部请求：没解析出响应体的算"非业务失败"，它已经记在 http_req_failed 里了。
  businessErrors.add(hasBody && body.code !== 0 && res.status !== 429, { endpoint });

  const failed = !okHttp || !okCode;
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
  const m429 = pick(data.metrics, `http_429{endpoint:${endpoint}}`) || {};
  // 响应体指标只有归档脚本（04-posts-archives.js）上报，其它接口这里是 null
  const body = pick(data.metrics, `response_body_kib{endpoint:${endpoint}}`) || {};
  const reqs = pick(data.metrics, `http_reqs{endpoint:${endpoint}}`) || {};
  const dropped = pick(data.metrics, 'dropped_iterations') || {};
  const checks = pick(data.metrics, 'checks') || {};

  const num = (v, digits = 2) => (v === undefined || v === null ? null : Number(v.toFixed(digits)));

  const slo = extra && extra.slo ? extra.slo : null;
  const p95Pass = slo ? dur['p(95)'] < slo : null;
  const p99Slo = slo ? SLO_P99[endpoint] : null;
  const p99Pass = p99Slo ? dur['p(99)'] <= p99Slo : null;
  const errPass = (biz.rate || 0) < 0.01 && (failed.rate || 0) < 0.01;

  const record = {
    endpoint,
    script: __ENV.SCRIPT_NAME || endpoint,
    base_url: BASE_URL,
    requests: reqs.count || 0,
    rps: num(reqs.rate),
    dropped_iterations: dropped.count || 0,
    http_failed_rate: num((failed.rate || 0) * 100, 3),
    business_error_rate: num((biz.rate || 0) * 100, 3),
    http_429_rate: num((m429.rate || 0) * 100, 3),
    checks_rate: num((checks.rate || 0) * 100, 2),
    p50_ms: num(dur.med),
    p90_ms: num(dur['p(90)']),
    p95_ms: num(dur['p(95)']),
    p99_ms: num(dur['p(99)']),
    avg_ms: num(dur.avg),
    min_ms: num(dur.min),
    max_ms: num(dur.max),
    slo_p95_ms: slo,
    slo_p95_pass: p95Pass,
    slo_p99_ms: p99Slo,
    slo_p99_pass: p99Pass,
    slo_pass: p95Pass,
    rate: extra && extra.rate ? extra.rate : null,
    duration: extra && extra.duration ? extra.duration : null,
    passed: true,
  };
  if (body.max !== undefined) {
    // 归档的响应体 SLI（定稿第 ② 条）：一律按最坏的一次判（响应体在固定数据量下基本是常数）
    record.body_avg_kib = num(body.avg, 1);
    record.body_max_kib = num(body.max, 1);
    record.slo_body_kib = SLO.archivesBodyKiB;
    record.slo_body_pass = body.max <= SLO.archivesBodyKiB;
  }
  if (slo) {
    record.passed = p95Pass && errPass && (record.slo_body_pass === undefined || record.slo_body_pass);
  }

  const lines = [
    '',
    `── ${endpoint} ${'─'.repeat(Math.max(0, 58 - endpoint.length))}`,
    `  请求数 ${record.requests} ｜ 实际 RPS ${record.rps} ｜ 丢弃迭代 ${record.dropped_iterations}`,
    `  HTTP 失败率 ${record.http_failed_rate}% ｜ 业务失败率(code!==0) ${record.business_error_rate}% ｜ 429 率 ${record.http_429_rate}% ｜ checks 通过率 ${record.checks_rate}%`,
    `  延迟 p50 ${record.p50_ms}ms ｜ p90 ${record.p90_ms}ms ｜ p95 ${record.p95_ms}ms ｜ p99 ${record.p99_ms}ms ｜ max ${record.max_ms}ms`,
  ];
  if (slo) {
    lines.push(`  SLO p95 < ${slo}ms ⇒ ${p95Pass ? '✅ 通过' : '❌ 未达标'}`);
  }
  if (p99Slo) {
    lines.push(`  SLO p99 ≤ ${p99Slo}ms ⇒ ${p99Pass ? '✅ 通过' : '❌ 未达标'}`);
  }
  if (record.slo_body_pass !== undefined) {
    lines.push(`  SLO 响应体 ≤ ${record.slo_body_kib} KiB ⇒ ${record.slo_body_pass ? '✅ 通过' : '❌ 未达标'}`);
  }

  const out = {};
  out.stdout = lines.join('\n') + '\n';
  out[`${OUT_DIR}/${endpoint}-summary.json`] = JSON.stringify(record, null, 2);
  return out;
}
