#!/usr/bin/env node
// ============================================================================
// 造数据脚本：往 compose 的 pgsql 容器（服务名 pgsql / 库 blog_stage2）灌文章
//
//   node tools/load/seed-posts.mjs            # 默认 1000 篇
//   node tools/load/seed-posts.mjs 10000      # 10,000 篇
//   node tools/load/seed-posts.mjs --clean 1000   # 先清掉历史 LOADTEST 数据再灌
//   node tools/load/seed-posts.mjs --only-clean   # 只清理
//   node tools/load/seed-posts.mjs --dry-run 100  # 只生成 SQL 看看规模，不执行
//
// 为什么这么写（每一条都是刻意的）：
//
// ① **不走 API，直接写库**。1000 篇走 POST /api/posts 至少是 1000 次 HTTP + 1000 次
//    Pbkdf2 之外的完整业务链路，而 write 规则限流是 **2 次/秒** —— 光限流就要 500 秒。
//    造数据是准备工作，不该被"为了保护线上而存在的限流器"拖住（learn/03 §9 热点一）。
//
// ② **确定性 ID + 确定性内容**。ID 由「固定基准时间 + 序号」生成的 UUIDv7 决定，
//    正文由固定种子的 PRNG 决定 —— 于是同一个序号每次生成的字节完全相同，
//    重跑用 `ON CONFLICT DO NOTHING` 就能幂等（不会灌出 2000 篇）。
//
// ③ **统一 `[LOADTEST]` 标题前缀**。这是清理的唯一依据，也是"这批数据是压测数据"的
//    肉眼可见标记。分类与标签同样带前缀。`--clean` 只删这两类，绝不碰真实文章。
//
// ④ **正文必须长且有中文**。列表页只投影卡片字段（不加载 Content），但详情页会加载
//    完整 Content，**搜索走 zhparser 中文分词 + ts_rank**。正文太短时中文全文检索的
//    真实开销会被严重低估（learn/03 §7.1 的"正文长度也要真实"）。
//
// ⑤ **不写 `SearchVector`**。它是 `GENERATED ALWAYS AS ... STORED` 生成列，
//    由数据库按 Title/Summary/Content 自动算（含 zhparser 分词），自己写会直接报错。
//    `Version` 写 1、`IsDeleted` 写 false：这两列 NOT NULL 且无数据库默认值。
//
// ⑥ 灌完自动 `ANALYZE`。规划器没有统计信息时会选错计划（可能全表扫描），
//    那样测出来的不是"有索引时的性能"。
// ============================================================================

import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

// ---------------------------------------------------------------- 配置

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HERE, '..', '..');

const TITLE_PREFIX = '[LOADTEST]';
const CATEGORY_PREFIX = '[LOADTEST] 分类';
const TAG_PREFIX = '[LOADTEST] 标签';

const PG_SERVICE = process.env.LOADTEST_PG_SERVICE ?? 'pgsql';
const PG_USER = process.env.LOADTEST_PG_USER ?? 'kky';
const PG_DB = process.env.LOADTEST_PG_DB ?? 'blog_stage2';

/** 草稿比例（20% 未发布；详情页草稿保护与列表过滤都要有真实数据才有意义） */
const DRAFT_RATIO = 0.2;
/** 每篇正文目标长度（字符数）。C# 的 WordCount 就是 content.Length，中文按 UTF-16 单元算 */
const CONTENT_TARGET_CHARS = 2600;
/** 造多少种分类 / 标签（带前缀，随机分配） */
const CATEGORY_COUNT = 8;
const TAG_COUNT = 24;
/** 已发布文章分布在最近多少个月内（让归档接口有多个年/月分组） */
const SPREAD_MONTHS = 30;

// ---------------------------------------------------------------- 参数解析

const argv = process.argv.slice(2);
const flags = new Set(argv.filter((a) => a.startsWith('--')));
const positional = argv.filter((a) => !a.startsWith('--'));

const count = Number.parseInt(positional[0] ?? '1000', 10);
const onlyClean = flags.has('--only-clean');
const doClean = onlyClean || flags.has('--clean');
const dryRun = flags.has('--dry-run');
/** `--no-analyze` 只在对比"统计信息缺失"的影响时用，正常别关 */
const skipAnalyze = flags.has('--no-analyze');

if (!Number.isFinite(count) || count < 0) {
  console.error('用法：node tools/load/seed-posts.mjs [篇数=1000] [--clean] [--only-clean] [--dry-run]');
  process.exit(2);
}
if (!onlyClean && count === 0) {
  console.error('篇数必须 > 0（只想清理请用 --only-clean）');
  process.exit(2);
}

// ---------------------------------------------------------------- 确定性随机

/** mulberry32：小而快的确定性 PRNG。种子固定 ⇒ 每次生成的数据完全一致 */
function mulberry32(seed) {
  let a = seed >>> 0;
  return function next() {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const rand = mulberry32(0x10ad7e57); // 固定种子：跨机器、跨次运行结果一致
const randInt = (min, max) => min + Math.floor(rand() * (max - min + 1));
const pick = (arr) => arr[Math.floor(rand() * arr.length)];
const pickMany = (arr, n) => {
  const pool = [...arr];
  const out = [];
  for (let i = 0; i < n && pool.length > 0; i++) {
    out.push(pool.splice(Math.floor(rand() * pool.length), 1)[0]);
  }
  return out;
};

// ---------------------------------------------------------------- UUIDv7

/** 与 Blog.Domain 的 BaseEntity 保持一致：UUIDv7（时间前缀 + 随机位），在 PG 里索引局部性好 */
function uuidv7(ms, rnd) {
  const hex = ms.toString(16).padStart(12, '0').slice(-12);
  const r = () => Math.floor(rnd() * 16).toString(16);
  const r2 = () => Math.floor(rnd() * 4).toString(16);
  const randA = r() + r() + r();
  const variant = (8 + Math.floor(rnd() * 4)).toString(16);
  const randB = Array.from({ length: 12 }, r).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-7${randA}-${variant}${r2()}${r2()}${r2()}-${randB}`;
}

/** 基准时间：压测数据的时间原点。固定值 ⇒ ID 与发布时间都确定 */
const BASE_MS = Date.UTC(2026, 0, 1, 0, 0, 0);

// ---------------------------------------------------------------- 中文正文生成

const TOPICS = [
  { name: 'EF Core 查询与索引', terms: ['AsNoTracking', '投影查询', '查询计划', '复合索引', '分页', 'N+1', 'DbContext', '变更追踪'] },
  { name: 'PostgreSQL 全文检索', terms: ['tsvector', 'ts_rank', 'GIN 索引', '分词配置', '停用词', '相关度排序', 'CTE', '生成列'] },
  { name: '.NET 垃圾回收', terms: ['LOH', 'Gen2 回收', '工作集', '托管堆', '弱代假设', '压缩', '碎片', '分配速率'] },
  { name: 'Redis 缓存设计', terms: ['缓存穿透', '空值哨兵', 'TTL 抖动', '雪崩', '击穿', '序列化', '命中率', '降级'] },
  { name: 'ASP.NET Core 中间件', terms: ['限流', '异常处理', '转发头', '响应缓存', '管道顺序', '依赖注入', '生命周期', '终结点路由'] },
  { name: '并发与线程池', terms: ['线程饥饿', '异步阻塞', '信号量', '连接池', '行级锁', '死锁', '乐观锁', '重试'] },
  { name: '容器与部署', terms: ['健康检查', '优雅停机', '数据卷', '反代', '镜像分层', '环境变量', '滚动发布', '回滚'] },
  { name: '可观测性', terms: ['结构化日志', '慢查询', '计数器', '火焰图', '链路追踪', '错误率', '饱和度', '告警'] },
  { name: '前端性能', terms: ['首屏', '骨架屏', '懒加载', '打包体积', '关键渲染路径', '缓存策略', '水合', '虚拟列表'] },
  { name: '测试与质量', terms: ['集成测试', '契约测试', '夹具', '覆盖率', '回归', '假红', '端到端', '断言'] },
];

/** 正文模板：{t}/{t2}/{t3} 由主题词替换，{n} 是段落序号 */
const SENTENCES = [
  '在「{topic}」这个话题上，{t}经常被当成唯一的怀疑对象，但真正拖慢响应的往往是{t2}与{t3}的相互作用。',
  '第一次遇到这个问题时，团队花了整整一个下午去查{t}，最后发现瓶颈其实在{t2}上——这类误判在缺少测量手段时特别常见。',
  '把{t}和{t2}放在一起看会清晰很多：前者决定单次请求的固定开销，后者决定开销随数据量增长的速度。',
  '经验上，只有当{t}的代价可以被观测到（日志、计数器或者压测数字）时，优化它才是合理的，否则只是凭直觉改代码。',
  '需要强调的是，{t}的收益存在上限：一旦{t2}成为主导项，继续调优{t}就只能得到很小的改善。',
  '从数据量角度看，{t2}在小表上几乎不产生可观测的差异，只有当行数进入规划器认真对待索引的量级时，{t3}的影响才会显现。',
  '一个反直觉的结论是：先加{t}并不总是对的，有时候减少一次{t2}比给{t3}加十个索引都有效。',
  '如果只能记住一句话，那就是——先测量{t}，再决定要不要优化{t2}，否则你无法证明改动真的有效。',
  '在排查过程中，我们习惯把{t}拆成三段：请求进入、业务处理、数据访问；三段各自计时，才能判断{t3}到底在哪一段。',
  '需要注意的是，{t}在开发机上看起来完全正常，是因为开发库的数据量太小，规划器直接选择了全表扫描。',
];

const CODE_SNIPPETS = [
  `\`\`\`csharp
var query = _context.Posts
    .AsNoTracking()
    .Where(p => p.PublishedAt != null)
    .OrderByDescending(p => p.PublishedAt)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .Select(ProjectCard());
\`\`\``,
  `\`\`\`sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT "Id", "Title" FROM "Posts"
WHERE "SearchVector" @@ plainto_tsquery('chinese'::regconfig, '缓存')
ORDER BY ts_rank("SearchVector", plainto_tsquery('chinese'::regconfig, '缓存')) DESC
LIMIT 12;
\`\`\``,
  `\`\`\`bash
docker compose exec pgsql psql -U kky -d blog_stage2 -c 'SELECT count(*) FROM "Posts";'
\`\`\``,
  `\`\`\`javascript
export const options = {
  stages: [{ duration: '30s', target: 10 }],
  thresholds: { http_req_duration: ['p(95)<300'] },
};
\`\`\``,
];

const LIST_ITEMS = [
  '看 p50 而不是平均值：平均值会把 1% 的长尾平均掉',
  '看错误率时同时解析业务 code，HTTP 200 也可能是失败',
  '看饱和度：CPU、连接池、线程池排队长度',
  '记录环境规格，否则数字无法复现',
  '冷缓存与热缓存必须分开测',
];

/**
 * 生成一篇带长正文的 Markdown 文章。
 * 用 PRNG 保证同一序号每次生成的内容完全一致（幂等的前提）。
 */
function buildContent(index) {
  const topic = TOPICS[index % TOPICS.length];
  const parts = [];
  parts.push(`# ${topic.name}：第 ${index} 次复盘\n`);
  parts.push(
    `> 这是压测样本 #${index}，正文由脚本生成，仅用于制造接近真实量级的数据。` +
      `主题：${topic.name}；关键词覆盖：${topic.terms.join('、')}。\n`,
  );

  let chars = parts.join('').length;
  let section = 1;
  while (chars < CONTENT_TARGET_CHARS) {
    parts.push(`\n## ${section}. ${pick(topic.terms)} 的观察点\n`);
    const paragraphs = randInt(3, 5);
    for (let p = 0; p < paragraphs; p++) {
      const sentenceCount = randInt(3, 5);
      const sentences = [];
      for (let s = 0; s < sentenceCount; s++) {
        const t1 = pick(topic.terms);
        let t2 = pick(topic.terms);
        let t3 = pick(topic.terms);
        if (t2 === t1) t2 = topic.terms[(topic.terms.indexOf(t1) + 1) % topic.terms.length];
        if (t3 === t1 || t3 === t2) t3 = topic.terms[(topic.terms.indexOf(t2) + 2) % topic.terms.length];
        sentences.push(
          pick(SENTENCES)
            .replaceAll('{topic}', topic.name)
            .replaceAll('{t3}', t3)
            .replaceAll('{t2}', t2)
            .replaceAll('{t}', t1),
        );
      }
      parts.push(`${sentences.join('')}\n`);
      if (p === 1) parts.push(`\n${pick(CODE_SNIPPETS)}\n`);
    }
    parts.push('\n排查清单：\n');
    for (const item of pickMany(LIST_ITEMS, 3)) parts.push(`- ${item}\n`);
    chars = parts.join('').length;
    section++;
    if (section > 40) break; // 保险丝：任何情况下都不生成无限长正文
  }
  return parts.join('');
}

// ---------------------------------------------------------------- SQL 生成

const LT = '$lt$'; // dollar-quoting 标签：正文里绝不含这四个字符（下面有断言）

/** 把任意文本包成 PostgreSQL dollar-quoted 字符串，避免手工转义引号与换行 */
function q(text) {
  const s = String(text);
  if (s.includes(LT)) throw new Error(`生成文本里出现了 dollar-quote 标签 ${LT}，会破坏 SQL 拼接`);
  return `${LT}${s}${LT}`;
}

function buildCleanSql() {
  // 只删带前缀的压测数据。PostTag / PostCollections 的外键都是 ON DELETE CASCADE，
  // 删文章与标签会自动清掉关联行，不需要手工删中间表。
  return [
    `\\echo '--- 清理历史 LOADTEST 数据 ---'`,
    `DELETE FROM "Posts" WHERE starts_with("Title", ${q(TITLE_PREFIX)});`,
    `DELETE FROM "Tags" WHERE starts_with("Name", ${q(TAG_PREFIX)});`,
    `DELETE FROM "Categories" WHERE starts_with("Name", ${q(CATEGORY_PREFIX)});`,
  ].join('\n');
}

/**
 * 生成完整的灌数 SQL。
 * @param {number} n 篇数
 * @param {{adminUserId: string|null, authorId: string|null, categoryIds: string[], tagIds: string[]}} ctx
 */
function buildSeedSql(n, ctx) {
  const stmts = [];
  const ts = (ms) => `'${new Date(ms).toISOString()}'`;

  stmts.push(`\\echo '--- 预置 LOADTEST 分类与标签 ---'`);
  const catIds = [];
  for (let i = 1; i <= CATEGORY_COUNT; i++) {
    const id = uuidv7(BASE_MS + i * 1000, rand);
    catIds.push(id);
    stmts.push(
      `INSERT INTO "Categories" ("Id","Name","CreatedAt","IsDeleted","Version") VALUES ` +
        `('${id}', ${q(`${CATEGORY_PREFIX}-${String(i).padStart(2, '0')}`)}, now(), false, 1) ` +
        `ON CONFLICT ("Id") DO NOTHING;`,
    );
  }
  const tagIds = [];
  for (let i = 1; i <= TAG_COUNT; i++) {
    const id = uuidv7(BASE_MS + (CATEGORY_COUNT + i) * 1000, rand);
    tagIds.push(id);
    stmts.push(
      `INSERT INTO "Tags" ("Id","Name","CreatedAt","IsDeleted","Version") VALUES ` +
        `('${id}', ${q(`${TAG_PREFIX}-${String(i).padStart(2, '0')}`)}, now(), false, 1) ` +
        `ON CONFLICT ("Id") DO NOTHING;`,
    );
  }

  // 真实分类/标签也参与随机分配：只有压测分类时，按分类过滤的查询计划会与线上不同
  const allCats = [...catIds, ...ctx.categoryIds];
  const allTags = [...tagIds, ...ctx.tagIds];

  stmts.push(`\\echo '--- 灌入 ${n} 篇文章 ---'`);

  const BATCH = 100;
  let batch = [];
  const flush = () => {
    if (batch.length === 0) return;
    stmts.push(`INSERT INTO "Posts" ("Id","UpdatedAt","Title","Content","CoverImage","AuthorId","PublishedAt","ViewCount","Summary","WordCount","CategoryId","CreatedAt","IsDeleted","DeletedAt","Version","CreatedByUserId") VALUES\n${batch.join(',\n')}\nON CONFLICT ("Id") DO NOTHING;`);
    batch = [];
  };

  const tagRows = [];
  for (let i = 1; i <= n; i++) {
    const id = uuidv7(BASE_MS + i * 1000, rand);
    const content = buildContent(i);
    const isDraft = rand() < DRAFT_RATIO;
    // 发布时间：分布在最近 SPREAD_MONTHS 个月内，让归档接口有多个分组
    const publishedAt = isDraft ? null : Date.now() - randInt(0, SPREAD_MONTHS * 30) * 86400_000 - i * 60_000;
    const createdAt = isDraft ? Date.now() - randInt(0, SPREAD_MONTHS * 30) * 86400_000 : publishedAt;
    const title = `${TITLE_PREFIX} #${String(i).padStart(5, '0')} · ${TOPICS[i % TOPICS.length].name}`;
    // 摘要：与标题、与其它文章都不同（含序号）。前 50 字是自动摘要规则的长度（T4）
    const summary = `【压测样本 #${i}】${content.replace(/[#>`\n]/g, ' ').trim().slice(0, 120)}`;
    const categoryId = rand() < 0.85 ? pick(allCats) : null;
    const viewCount = randInt(0, 5000);

    batch.push(
      `('${id}', ${ts(createdAt)}, ${q(title)}, ${q(content)}, '', ` +
        `${ctx.authorId ? `'${ctx.authorId}'` : 'NULL'}, ${publishedAt ? ts(publishedAt) : 'NULL'}, ${viewCount}, ${q(summary)}, ` +
        `${content.length}, ${categoryId ? `'${categoryId}'` : 'NULL'}, ${ts(createdAt)}, false, NULL, 1, ` +
        `${ctx.adminUserId ? `'${ctx.adminUserId}'` : 'NULL'})`,
    );
    if (batch.length >= BATCH) flush();

    // 每篇 1~4 个标签
    const tags = pickMany(allTags, randInt(1, 4));
    for (const t of tags) tagRows.push(`('${id}','${t}')`);
  }
  flush();

  if (tagRows.length > 0) {
    const CHUNK = 500;
    for (let i = 0; i < tagRows.length; i += CHUNK) {
      stmts.push(
        `INSERT INTO "PostTag" ("PostsId","TagsId") VALUES\n${tagRows.slice(i, i + CHUNK).join(',\n')}\nON CONFLICT ("PostsId","TagsId") DO NOTHING;`,
      );
    }
  }

  if (!skipAnalyze) {
    stmts.push(
      `\\echo '--- ANALYZE（让规划器看到新数据的统计信息）---'`,
      `ANALYZE "Posts";`,
      `ANALYZE "Tags";`,
      `ANALYZE "Categories";`,
    );
  }

  stmts.push(
    `\\echo '--- 校验 ---'`,
    `SELECT count(*) AS loadtest_posts FROM "Posts" WHERE starts_with("Title", ${q(TITLE_PREFIX)});`,
    `SELECT count(*) AS all_posts FROM "Posts";`,
    `SELECT count(*) AS published FROM "Posts" WHERE starts_with("Title", ${q(TITLE_PREFIX)}) AND "PublishedAt" IS NOT NULL;`,
    `SELECT count(*) AS drafts FROM "Posts" WHERE starts_with("Title", ${q(TITLE_PREFIX)}) AND "PublishedAt" IS NULL;`,
    `SELECT count(*) AS with_search_vector FROM "Posts" WHERE starts_with("Title", ${q(TITLE_PREFIX)}) AND "SearchVector" IS NOT NULL;`,
    `SELECT pg_size_pretty(pg_total_relation_size('"Posts"')) AS posts_total_size;`,
    `SELECT round(avg(length("Content"))) AS avg_content_chars, min(length("Content")) AS min_chars, max(length("Content")) AS max_chars FROM "Posts" WHERE starts_with("Title", ${q(TITLE_PREFIX)});`,
    `\\echo '--- 中文全文检索抽查（关键词：缓存）---'`,
    `\\timing on`,
    `SELECT count(*) AS fts_hits FROM "Posts" WHERE "SearchVector" @@ plainto_tsquery('chinese'::regconfig, '缓存');`,
    `\\timing off`,
  );

  return stmts.join('\n');
}

// ---------------------------------------------------------------- 执行

/** 通过 `docker compose exec -T pgsql psql` 把 SQL 从 stdin 灌进去 */
function runPsql(sql, label) {
  return new Promise((resolve, reject) => {
    const args = ['compose', 'exec', '-T', PG_SERVICE, 'psql', '-U', PG_USER, '-d', PG_DB, '-v', 'ON_ERROR_STOP=1'];
    const child = spawn('docker', args, { cwd: REPO_ROOT, stdio: ['pipe', 'inherit', 'inherit'] });

    const started = process.hrtime.bigint();
    child.on('error', reject);
    child.on('close', (code) => {
      const seconds = Number(process.hrtime.bigint() - started) / 1e9;
      if (code !== 0) return reject(new Error(`${label} 失败，psql 退出码 ${code}`));
      resolve(seconds);
    });

    // 分块写，尊重背压（一次性 write 10MB 会把内存顶上去）
    const CHUNK = 1 << 20;
    let offset = 0;
    const writeMore = () => {
      while (offset < sql.length) {
        const piece = sql.slice(offset, offset + CHUNK);
        offset += CHUNK;
        if (!child.stdin.write(piece)) {
          child.stdin.once('drain', writeMore);
          return;
        }
      }
      child.stdin.end();
    };
    writeMore();
  });
}

/** 用一条 SELECT 把灌数需要的外键目标（管理员账号、署名作者、已有分类/标签）读出来 */
function queryRows(sql) {
  return new Promise((resolve, reject) => {
    const args = ['compose', 'exec', '-T', PG_SERVICE, 'psql', '-U', PG_USER, '-d', PG_DB, '-t', '-A', '-F', '|', '-c', sql];
    const child = spawn('docker', args, { cwd: REPO_ROOT });
    let out = '';
    let err = '';
    child.stdout.on('data', (d) => (out += d));
    child.stderr.on('data', (d) => (err += d));
    child.on('error', reject);
    child.on('close', (code) => {
      if (code !== 0) return reject(new Error(`查询失败：${err.trim()}`));
      resolve(out.trim().split('\n').filter(Boolean).map((line) => line.split('|')));
    });
  });
}

// ---------------------------------------------------------------- 主流程

async function main() {
  console.log(`# 造数据脚本：${count} 篇（默认 1000）｜库 ${PG_DB} @ compose 服务 ${PG_SERVICE}`);
  console.log(`# 标记前缀：标题 ${TITLE_PREFIX} / 分类 ${CATEGORY_PREFIX} / 标签 ${TAG_PREFIX}`);

  const cleanSql = buildCleanSql();

  if (onlyClean) {
    console.log('\n== 只清理模式 ==');
    const secs = await runPsql(cleanSql, '清理');
    console.log(`\n✅ 清理完成，耗时 ${secs.toFixed(2)}s`);
    return;
  }

  // 外键目标：必须先读出来，否则 AuthorId / CreatedByUserId 写错会触发 FK 报错
  const rows = await queryRows(
    `SELECT
       coalesce((SELECT "Id"::text FROM "Users" WHERE "Role" = 'Admin' ORDER BY "Id" LIMIT 1), ''),
       coalesce((SELECT "Id"::text FROM "Authors" ORDER BY "Id" LIMIT 1), ''),
       coalesce((SELECT string_agg("Id"::text, ',') FROM "Categories" WHERE "IsDeleted" = false AND NOT starts_with("Name", ${q(CATEGORY_PREFIX)})), ''),
       coalesce((SELECT string_agg("Id"::text, ',') FROM "Tags" WHERE "IsDeleted" = false AND NOT starts_with("Name", ${q(TAG_PREFIX)})), '');`,
  );
  const [adminUserId, authorId, catCsv, tagCsv] = rows[0];
  const ctx = {
    adminUserId: adminUserId || null,
    authorId: authorId || null,
    categoryIds: catCsv ? catCsv.split(',') : [],
    tagIds: tagCsv ? tagCsv.split(',') : [],
  };
  console.log(
    `# 外键目标：管理员账号 ${ctx.adminUserId ?? '(无)'}｜署名作者 ${ctx.authorId ?? '(无)'}｜` +
      `复用已有分类 ${ctx.categoryIds.length} 个 / 标签 ${ctx.tagIds.length} 个`,
  );

  const seedSql = buildSeedSql(count, ctx);

  if (dryRun) {
    console.log(`\n== dry-run：SQL 共 ${(seedSql.length / 1024 / 1024).toFixed(2)} MiB，未执行 ==`);
    return;
  }

  let total = 0;
  if (doClean) {
    console.log('\n== ① 清理历史 LOADTEST 数据 ==');
    total += await runPsql(cleanSql, '清理');
  }
  console.log(`\n== ${doClean ? '②' : '①'} 灌入 ${count} 篇 ==`);
  const seedSeconds = await runPsql(seedSql, '灌数');
  total += seedSeconds;

  console.log(`\n✅ 完成`);
  console.log(`   灌数耗时：${seedSeconds.toFixed(2)}s（${(count / seedSeconds).toFixed(1)} 篇/秒）`);
  if (doClean) console.log(`   含清理总耗时：${total.toFixed(2)}s`);
  console.log(`   清理命令：node tools/load/seed-posts.mjs --only-clean`);
}

main().catch((err) => {
  console.error(`\n❌ ${err.message}`);
  process.exit(1);
});
