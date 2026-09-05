# 02-API 接口清单

> 文档版本：v1.0 | 基础路由前缀：`/api`

## 0. 通用约定

- 统一响应体：`{ "code": 0, "data": {}, "message": "ok" }`（code 语义见 01 文档 §6）
- 分页请求参数：`page`（从 1 起）、`pageSize`（默认 12，上限 50）
- 分页响应体：`{ items: [], page, pageSize, total, totalPages }`
- 时间格式：ISO 8601 UTC（`2026-09-06T12:00:00Z`）
- 写操作请求体需带 `rowVersion`（Base64），用于乐观锁校验

---

## 1. 文章 Posts

### 公开接口

| # | Method | 路由 | 说明 | 缓存 |
|---|--------|------|------|------|
| 1.1 | GET | `/api/posts?page=1&pageSize=12&categoryId=&tagId=&keyword=&sort=new` | 分页文章列表（首页卡片数据）。卡片项含：id、title、summary(前50字)、coverImage、分类、标签数组、publishedAt、viewCount | 列表页级缓存 |
| 1.2 | GET | `/api/posts/{id}` | 文章详情（含全文、作者、分类、标签、viewCount）；**每次调用浏览量原子 +1** | 详情缓存（写入后失效） |
| 1.3 | GET | `/api/posts/archives` | 归档时间轴数据：按年月倒序分组 `[{ year, month, items: [{id,title,publishedAt}] }]` | 缓存，文章变更失效 |
| 1.4 | GET | `/api/posts/search?keyword=xx&page=1&pageSize=10` | 全文模糊搜索（标题/内容/标签名/分类名 ILIKE），供搜索弹窗调用 | 短 TTL 缓存 |

### 管理接口（写操作，走乐观锁）

| # | Method | 路由 | 说明 |
|---|--------|------|------|
| 1.5 | POST | `/api/posts` | 创建文章（标题、内容、categoryId、标签 id 集、封面 URL、是否立即发布） |
| 1.6 | PUT | `/api/posts/{id}` | 更新文章（带 rowVersion，冲突返回 409/4090） |
| 1.7 | DELETE | `/api/posts/{id}` | 软删除（带 rowVersion） |
| 1.8 | POST | `/api/posts/{id}/publish` | 发布（带 rowVersion） |
| 1.9 | POST | `/api/posts/{id}/unpublish` | 下架（带 rowVersion） |

## 2. 分类 Categories

| # | Method | 路由 | 说明 | 缓存 |
|---|--------|------|------|------|
| 2.1 | GET | `/api/categories` | 全部分类 + 文章数：`[{id,name,postCount}]` | 缓存 |
| 2.2 | POST | `/api/categories` | 创建分类（管理） | 失效 |
| 2.3 | PUT | `/api/categories/{id}` | 更新（管理，带 rowVersion） | 失效 |
| 2.4 | DELETE | `/api/categories/{id}` | 软删除（管理，带 rowVersion；其下文章 CategoryId 置 null） | 失效 |

## 3. 标签 Tags

| # | Method | 路由 | 说明 | 缓存 |
|---|--------|------|------|------|
| 3.1 | GET | `/api/tags` | 全部标签 + 文章数：`[{id,name,postCount}]` | 缓存 |
| 3.2 | POST | `/api/tags` | 创建标签（管理） | 失效 |
| 3.3 | PUT | `/api/tags/{id}` | 更新（管理，带 rowVersion） | 失效 |
| 3.4 | DELETE | `/api/tags/{id}` | 软删除（管理，带 rowVersion；多对多关联随软删失效） | 失效 |

## 4. 站点 Site

| # | Method | 路由 | 说明 | 缓存 |
|---|--------|------|------|------|
| 4.1 | GET | `/api/site/config` | 站点配置：站点名、Logo、副文本打字机内容数组、建站日期（供 Footer 建站天数计算） | 长缓存 |
| 4.2 | PUT | `/api/site/config` | 更新配置（管理，带 rowVersion） | 失效 |
| 4.3 | GET | `/api/site/social-links` | 社交图标列表（按 SortOrder 排序，仅 IsVisible） | 长缓存 |
| 4.4 | PUT | `/api/site/social-links` | 批量更新社交链接（管理，逐条带 rowVersion） | 失效 |
| 4.5 | GET | `/api/site/stats` | 站点统计：`{ siteDays, totalPosts, totalWords, totalViews, tagCount, categoryCount }` | 缓存 |

## 5. 文件 Files（独立于 /api 数据接口的媒体路由）

> 路由前缀独立为 `/media`，与业务 API 分离，便于后续替换为 OSS/CDN

| # | Method | 路由 | 说明 |
|---|--------|------|------|
| 5.1 | GET | `/media/{**path}` | 读取 `wwwroot/uploads/` 下文件（防目录穿越校验），带 `Cache-Control: max-age=604800` |
| 5.2 | POST | `/media/upload` | 上传文件（multipart，白名单扩展名 + 10MB 上限 + 随机文件名），返回 `{ url }`（管理） |

## 6. 错误码总表

| code | HTTP | 含义 |
|------|------|------|
| 0 | 200 | 成功 |
| 4001 | 400 | 参数校验失败 |
| 4010 | 401 | 未认证（预留） |
| 4030 | 403 | 无权限（预留） |
| 4040 | 404 | 资源不存在 |
| 4090 | 409 | 乐观锁并发冲突 |
| 4091 | 429 | 触发限流（响应头 `Retry-After`） |
| 5000 | 500 | 系统异常 |

## 7. 服务层（Service）规划

| Service | 职责 |
|---------|------|
| `IPostService` / `PostService` | 文章分页查询、详情（含浏览量原子自增）、归档、搜索、创建/更新/删除/发布 |
| `ICategoryService` / `CategoryService` | 分类列表（含计数）、CRUD |
| `ITagService` / `TagService` | 标签列表（含计数）、CRUD |
| `ISiteService` / `SiteService` | 站点配置读写、社交链接、站点统计聚合 |
| `IFileStorageService` / `LocalFileStorageService` | 文件保存/读取（Infrastructure 实现本地磁盘版，可替换 OSS） |
| `ICacheService` / `RedisCacheService` / `NoopCacheService` / `MemoryCacheService` | 缓存抽象与实现（三防逻辑在 Redis 实现内） |
| `IRateLimiter` / `RedisTokenBucketLimiter` / `MemoryTokenBucketLimiter` | 令牌桶限流抽象与实现 |

## 8. 仓储扩展点（相对现有 IBaseRepository 新增）

- `IPostRepository`：`GetPagedAsync`（分页+过滤+排序+投影 DTO）、`SearchAsync`（ILIKE）、`GetArchivesAsync`（分组投影）、`IncrementViewCountAsync`（ExecuteUpdate 原子自增）
- `ICategoryRepository` / `ITagRepository`：`GetAllWithPostCountAsync`（GroupJoin 计数投影）
- 通用：`ISoftDelete` 由现有 `BaseEntity` 承担；软删除统一走 `entity.Delete()` + UoW 提交
