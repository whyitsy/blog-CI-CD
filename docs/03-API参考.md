# 03 · API 参考

> **用途**：后端 HTTP 端点的权威清单——路径、方法、权限、参数、响应字段、错误。
> **读者**：前端开发者、接口调试者、需要确认"某个端点到底存不存在、需要什么权限"时。
> **最后核对**：2026-09-14 ｜ **代码依据**：`Blog.Backend/Blog.WebApi/Controllers/*.cs`
>
> 权限模型全貌（角色、策略、归属校验为什么在服务层）见
> [02-架构与数据模型.md](./02-架构与数据模型.md) §10；异常映射与中间件顺序见同文 §4、§11。

---

## 1. 通用约定

| 项 | 约定 |
|---|---|
| 基础路径 | 全部以 `/api` 开头 |
| 数据格式 | JSON（`Content-Type: application/json`）；**文件上传除外**（`multipart/form-data`） |
| 字段命名 | camelCase（`JsonSerializerDefaults.Web`，前后端一致） |
| 认证 | `Authorization: Bearer <access token>` |
| 时间格式 | ISO 8601 带时区（`DateTimeOffset`） |
| 主键 | GUID 字符串；路由写的是 `{id:guid}`，传非 GUID 的 id **不匹配路由**（见 §1.1 末） |

没有注册端点（T1）：作者账号由管理员在 `/api/users` 创建；管理员账号由已有管理员创建。

### 1.1 统一响应体

**所有**接口（含各类错误）返回同一结构：

```json
{ "code": 0, "message": "ok", "data": { } }
```

| 字段 | 说明 |
|---|---|
| `code` | `0` = 成功；非 0 = 业务错误码（§1.2） |
| `message` | 成功时为 `"ok"`；失败时是可直接展示给用户的中文提示 |
| `data` | 成功时的载荷；无返回数据的接口为 `null` |

HTTP 状态码**同时**被设置成语义正确的值（400/401/403/404/409/413/429），不只是 200。
序列化 camelCase，定义见 `Blog.Application/Common/ApiResponse.cs`。

> ⚠️ **"统一响应体"这句话，从 P0（提交 `76cc0d7`）起才真正无条件成立。**
> 在此之前它**不成立**：`[ApiController]` 的自动模型校验发生在 Action **之前**，失败时短路
> 返回 400，默认输出 RFC 7807 **ProblemDetails**（`{type,title,status,errors,traceId}`）——
> 与 `{code,message,data}` 是**两套结构**。实测缺口（`GET /api/posts/search` 不带 `keyword`）：
> 响应体是 `{"type":"…rfc9110#section-15.5.1","title":"One or more validation errors occurred.",…}`。
>
> 对调用方的影响不是"不好看"，而是必须写两套解析逻辑：前端按 `{code,message}` 处理，
> 一旦请求在校验阶段被拦下就是 `{title,errors}`，用户看到空白或 `undefined`。
> 现在由 `ApiBehaviorOptions.InvalidModelStateResponseFactory`（`Program.cs:57-75`）接管，
> 模型校验失败也构造成 `400 + {code:4001, message:"参数「keyword」不合法或缺失"}`，
> 契约由 `Blog.Tests/Integration/UnifiedErrorResponseTests.cs` 的 3 个测试钉住；
> 机制说明见 [02](./02-架构与数据模型.md) §4.1。

**统一体的边界**：只要请求进入本项目的管道——成功、模型校验失败、认证失败（401）、
授权失败（403）、业务异常、未捕获异常——都是统一体。唯一例外是**没有命中任何路由**的请求
（路径拼错、`{id:guid}` 传了非 GUID），由框架直接返回**空体 404**；
前端 `src/api/http.ts` 对"响应体不是 JSON"单独兜底。

### 1.2 错误码表

定义在 `Blog.Application/Common/ErrorCodes.cs`（**11 个码**，含成功）：

| code | HTTP | 含义 | 典型场景 |
|---|---|---|---|
| `0` | 200 | 成功 | — |
| `4001` | 400 | 参数校验失败 | 字段缺失/超长、缺少或 `< 1` 的版本号、媒体地址非法 |
| `4002` | 400 | 业务规则不满足 | 分类名 / 标签名重复 |
| `4003` | 400 | 资源重复 | 邮箱已被占用、专栏 slug 已被占用 |
| `4010` | 401 | 未认证 | 缺 token / 无效 / 过期 / 账号被停用 |
| `4030` | 403 | 无权限 | 角色不足，或越权访问他人资源 |
| `4040` | 404 | 资源不存在 | **草稿与未发布专栏对无权限者也用此码**（不暴露存在性） |
| `4090` | 409 | 乐观锁并发冲突 | `version` 与库中不一致 |
| `4091` | 429 | 触发限流 | 附带 `Retry-After` 头 |
| `4130` | 413 | 请求体过大 | 上传文件超过 `FileStorage:MaxFileSize` |
| `5000` | 500 | 系统异常 | 未预期的异常，详情只写日志 |

`4002`/`4003` 没有专门的 HTTP 映射，落在默认分支的 **400**
（`ExceptionHandlingMiddleware.MapStatusCode`，`ExceptionHandlingMiddleware.cs:98`）。
401/403 即使由认证中间件产生（而非控制器抛异常）也走统一体，
由 `JwtBearerEvents.OnChallenge` / `OnForbidden` 保证。

### 1.3 分页结构

列表类接口统一返回 `PagedResult<T>`：

```json
{ "items": [], "total": 0, "page": 1, "pageSize": 12, "totalPages": 0 }
```

| 字段 | 说明 |
|---|---|
| `items` | 当前页数据 |
| `total` | 命中条件的总条数 |
| `page` | 当前页码；`< 1` 归一化为 `1` |
| `pageSize` | **实际生效**的每页条数（见下方 ⚠️） |
| `totalPages` | `ceil(total / pageSize)`；`pageSize ≤ 0` 时为 `0` |

> ⚠️ 字段是 **`total`**，不是 `totalCount`。前端类型 `PagedResult`（`src/types/index.ts`）
> 已与之对齐；写成 `totalCount` 会让所有"共 N 篇"渲染成 `undefined`。

> ⚠️ **`pageSize` 超限时回落到默认 12，不是截断到上限。** 只有落在 `1..maxSize` 内才被采用：
>
> | 场景 | `maxSize` |
> |---|---|
> | 公开列表（`includeUnpublished=false`） | 50 |
> | 管理端列表（`includeUnpublished=true`） | 100 |
>
> 所以 `?pageSize=1000` 返回的是 **12 条/页**，响应里的 `pageSize` 如实回显 `12`；
> `pageSize=0` 或负数同样回落为 12。实现：`PostService.Normalize`（`PostService.cs:355-366`）。

### 1.4 乐观锁与写入语义

凡是**更新 / 删除**已存在资源的接口，都必须携带当前 `version`：

| 传法 | 用于 |
|---|---|
| 请求体字段 `version` | 有请求体的更新（PUT） |
| 查询串 `?version=3` | 删除、发布、停用等无请求体的操作 |

- 版本不匹配 → **409 / `4090`**（文案"数据已被其他请求修改，请刷新后重试"）
- 缺少版本或 `< 1` → **400 / `4001`**
- 版本号原样取自查询/详情接口的响应字段，**不要自己 +1**；服务端以
  `UPDATE … SET "Version" = @expected + 1 WHERE "Id" = @id AND "Version" = @expected` 施加
  （`BaseRepository.ApplyOptimisticVersion`）

**写入语义统一规则**（站点配置这类"Key 可能还不存在"的接口尤其依赖它）：

| 情形 | 语义 |
|---|---|
| 资源 / 配置 Key **不存在** | 视为**新增**，忽略传入的 `version` |
| 资源 / 配置 Key **已存在** | 走**乐观锁**，`version` 必须 ≥ 1，否则 `4001` |

**浏览量是唯一的例外**：`GET /api/posts/{id}` 的 `ViewCount + 1` 由数据库侧原子自增完成、
**不参与乐观锁**——高频访问下若也走版本号，读一篇文章就会制造一片 409。

### 1.5 权限标记

| 标记 | 含义 | 代码 |
|---|---|---|
| 🌐 **公开** | 无需认证 | 未标注特性，或 `[AllowAnonymous]` |
| 🔑 **需登录** | 任意有效 token | `[Authorize]`（无策略） |
| ✍️ **ContentWriter** | `Admin` 或 `Author` 角色 | `[Authorize(Policy = "ContentWriter")]` |
| 👑 **AdminOnly** | 仅 `Admin` 角色 | `[Authorize(Policy = "AdminOnly")]` |

策略名是 `Program.cs` 里的 `AuthorizationPolicies` 常量（控制器与 Program 共用，避免魔法字符串）。
⚠️ **登录入口不承担权限边界**：`POST /api/auth/login` 不限定角色，管理员与作者共用；
边界由**具体接口上的策略**强制，逐条汇总见 §12。

### 1.6 47 个端点总表

按控制器连续编号：Auth 1–3、Posts 4–12、Authors 13–18、Categories 19–22、Tags 23–26、
Collections 27–33、Site 34–39、Users 40–45、Files 46–47。

| # | 方法 | 路径 | 权限 | 说明 |
|---|---|---|---|---|
| 1 | POST | `/api/auth/login` | 🌐 公开 | 登录，返回 token 与用户信息 |
| 2 | GET | `/api/auth/me` | 🔑 需登录 | 当前登录用户 |
| 3 | POST | `/api/auth/logout` | 🔑 需登录 | 注销，使该账号所有旧 token 失效 |
| 4 | GET | `/api/posts` | 🌐 公开* | 分页列表，支持多种过滤 |
| 5 | GET | `/api/posts/{id}` | 🌐 公开* | 详情，**浏览量 +1** |
| 6 | GET | `/api/posts/{id}/readonly` | ✍️ ContentWriter | 详情，**不计数**（编辑器取数） |
| 7 | GET | `/api/posts/archives` | 🌐 公开 | 归档，按年月分组的数组（非分页） |
| 8 | GET | `/api/posts/search` | 🌐 公开 | 全文检索（列表接口带 `keyword`） |
| 9 | POST | `/api/posts` | ✍️ ContentWriter | 创建 |
| 10 | PUT | `/api/posts/{id}` | ✍️ ContentWriter | 更新（乐观锁 + 归属校验） |
| 11 | POST | `/api/posts/{id}/publish` | ✍️ ContentWriter | 发布 / 下架（乐观锁 + 归属校验） |
| 12 | DELETE | `/api/posts/{id}` | ✍️ ContentWriter | 软删除（乐观锁 + 归属校验） |
| 13 | GET | `/api/authors` | 🌐 公开 | 作者列表 |
| 14 | GET | `/api/authors/{id}` | 🌐 公开 | 单个作者 |
| 15 | GET | `/api/authors/me` | ✍️ ContentWriter | 当前账号的署名身份 |
| 16 | POST | `/api/authors` | 👑 AdminOnly | 创建作者 |
| 17 | PUT | `/api/authors/{id}` | ✍️ ContentWriter | 更新（作者只能改自己） |
| 18 | DELETE | `/api/authors/{id}` | 👑 AdminOnly | 软删除作者 |
| 19 | GET | `/api/categories` | 🌐 公开 | 分类列表（含文章数） |
| 20 | POST | `/api/categories` | 👑 AdminOnly | 创建分类 |
| 21 | PUT | `/api/categories/{id}` | 👑 AdminOnly | 更新（乐观锁） |
| 22 | DELETE | `/api/categories/{id}` | 👑 AdminOnly | 软删除（乐观锁） |
| 23 | GET | `/api/tags` | 🌐 公开 | 标签列表（含文章数） |
| 24 | POST | `/api/tags` | 👑 AdminOnly | 创建标签 |
| 25 | PUT | `/api/tags/{id}` | 👑 AdminOnly | 更新（乐观锁） |
| 26 | DELETE | `/api/tags/{id}` | 👑 AdminOnly | 软删除（乐观锁） |
| 27 | GET | `/api/collections` | 🌐 公开* | 专栏列表 |
| 28 | GET | `/api/collections/{slug}` | 🌐 公开* | 专栏详情（按 slug，含文章） |
| 29 | GET | `/api/collections/id/{id}` | 👑 AdminOnly | 按 id 取详情（管理端，含未发布） |
| 30 | POST | `/api/collections` | 👑 AdminOnly | 创建专栏 |
| 31 | PUT | `/api/collections/{id}` | 👑 AdminOnly | 更新（乐观锁） |
| 32 | DELETE | `/api/collections/{id}` | 👑 AdminOnly | 软删除（乐观锁） |
| 33 | PUT | `/api/collections/{id}/posts` | 👑 AdminOnly | 整体编排专栏内文章与顺序 |
| 34 | GET | `/api/site/config` | 🌐 公开 | 首屏配置聚合 |
| 35 | PUT | `/api/site/config` | 👑 AdminOnly | 逐项更新配置（乐观锁） |
| 36 | GET | `/api/site/social-links` | 🌐 公开 | 社交链接 |
| 37 | PUT | `/api/site/social-links` | 👑 AdminOnly | 批量新增 / 更新 |
| 38 | DELETE | `/api/site/social-links/{id}` | 👑 AdminOnly | 软删除（乐观锁） |
| 39 | GET | `/api/site/stats` | 🌐 公开 | Footer 统计 |
| 40 | GET | `/api/users` | 👑 AdminOnly | 账号列表 |
| 41 | GET | `/api/users/{id}` | 👑 AdminOnly | 单个账号 |
| 42 | POST | `/api/users` | 👑 AdminOnly | 创建账号 |
| 43 | PUT | `/api/users/{id}` | 👑 AdminOnly | 更新角色 / 关联作者 / 启用状态 |
| 44 | POST | `/api/users/{id}/reset-password` | 👑 AdminOnly | 重置密码 |
| 45 | POST | `/api/users/{id}/disable` | 👑 AdminOnly | 停用账号 |
| 46 | POST | `/api/files/upload` | ✍️ ContentWriter | 上传文件，返回可访问 URL |
| 47 | GET | `/api/files/{**path}` | 🌐 公开 | 读取文件 |

\* 带 `includeUnpublished=true` 或访问草稿时需登录，规则见 §4.1 与 §4.2。

---

## 2. 三份代表性响应示例

全站响应只有三种形状，其余端点只给字段表。**结构是契约，数值仅示意。**

**例 ① 分页列表**——`GET /api/posts?page=1&pageSize=2`：

```json
{
  "code": 0,
  "message": "ok",
  "data": {
    "items": [
      {
        "id": "3f1a8c2e-4b6d-4a91-9c07-2d5e6f7a8b90",
        "title": "Hello, World!",
        "summary": "第一篇示例文章的摘要。",
        "coverImage": "",
        "categoryId": "0c9b7a5e-1d2f-4c3b-8a49-6e5d4c3b2a10",
        "categoryName": "随笔",
        "tags": [{ "id": "9a8b7c6d-5e4f-4a3b-2c1d-0e9f8a7b6c5d", "name": "开始" }],
        "publishedAt": "2026-09-01T10:00:00+08:00",
        "viewCount": 42
      }
    ],
    "total": 1,
    "page": 1,
    "pageSize": 2,
    "totalPages": 1
  }
}
```

**例 ② 详情**——`GET /api/posts/{id}` 的 `data`（`PostDetailDto`，`version` 供后续写操作回传）：

```json
{
  "id": "3f1a8c2e-4b6d-4a91-9c07-2d5e6f7a8b90",
  "title": "Hello, World!",
  "content": "# 标题\n\nMarkdown 原文。",
  "summary": "第一篇示例文章的摘要。",
  "coverImage": "",
  "categoryId": "0c9b7a5e-1d2f-4c3b-8a49-6e5d4c3b2a10",
  "categoryName": "随笔",
  "tags": [{ "id": "9a8b7c6d-5e4f-4a3b-2c1d-0e9f8a7b6c5d", "name": "开始" }],
  "collections": [{ "id": "b7c6d5e4-f3a2-4b1c-9d8e-7f6a5b4c3d2e", "title": "建站记", "slug": "build-my-blog" }],
  "authorId": "1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d",
  "authorName": "kky",
  "authorAvatar": "/api/files/avatar-default.webp",
  "createdByUserId": "5d4c3b2a-1e0f-4a9b-8c7d-6e5f4a3b2c1d",
  "publishedAt": "2026-09-01T10:00:00+08:00",
  "updatedAt": "2026-09-10T21:30:00+08:00",
  "viewCount": 42,
  "wordCount": 1280,
  "version": 3
}
```

**例 ③ 写操作**——`PUT /api/site/config` 逐项更新，请求体带 `version`，
响应 `data` 是更新后的**完整** `SiteConfigDto`（`versions` 中该项已 +1）：

```json
{ "key": "SiteName", "value": "kky's blog", "version": 1 }
```

```json
{
  "code": 0,
  "message": "ok",
  "data": {
    "siteName": "kky's blog",
    "logoName": "k",
    "siteLogo": null,
    "heroSubtitles": ["Hello, World!"],
    "heroBackgrounds": [],
    "foundingDate": "2026-01-01T00:00:00+08:00",
    "versions": { "SiteName": 2, "LogoName": 1, "HeroSubtitles": 1, "FoundingDate": 1 }
  }
}
```

---

## 3. 认证 `/api/auth`（#1–#3）

`AuthController.cs`

| 端点 | 请求 | 响应 `data` |
|---|---|---|
| `POST /login` | `{ email, password }` | `LoginResponse`（见下） |
| `GET /me` | — | `CurrentUserDto` |
| `POST /logout` | — | `null` |

`LoginResponse`：`token`（Access Token，只发这一个，无 Refresh Token，T7）、
`expiresAt`（ISO 8601，默认 30 分钟）、`role`（`Admin` / `Author`，前端据此决定落地页）、
`user`（`CurrentUserDto`）。

`CurrentUserDto`：

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | guid | 账号 id |
| `email` | string | 登录邮箱 |
| `role` | string | `Admin` / `Author` |
| `isActive` | bool | 是否启用 |
| `authorId` | guid? | 关联的署名对象；可为 `null` |
| `authorName` | string? | 关联作者名 |
| `lastLoginAt` | string? | 最近登录时间 |

> **两个 DTO 都绝不包含 `PasswordHash`**：后端从不返回任何凭据字段。

**错误与行为**：

- `login`：401 / `4010`——"邮箱或密码错误"或"账号已被停用"（两种情况**故意同码**，不区分）。
  ⚠️ 登录失败的 401 不应触发前端"跳登录页"；`src/api/http.ts` 的 `onUnauthorized`
  由调用方判断当前是否在登录页。
- `me`：401 / `4010`。前端启动时用它确认本地 token 是否仍然有效——这是唯一能识别
  "token 未过期但账号已被停用 / 改密"的方式（两者都会提升 `TokenVersion`）。
- `logout`：把该账号的 `TokenVersion + 1`，**该账号所有设备上的旧 token 立即失效**
  （JWT 本身无法主动吊销，这是补偿手段）。

---

## 4. 文章 `/api/posts`（#4–#12）

`PostsController.cs`

### 4.1 `GET /api/posts` — 分页列表

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `page` | int | `1` | 页码，`< 1` 归一化为 1 |
| `pageSize` | int | `12` | 每页条数；**超限回落 12**，上限公开 50 / 管理端 100（§1.3） |
| `categoryId` | guid? | — | 按分类过滤 |
| `tagId` | guid? | — | 按标签过滤 |
| `collectionId` | guid? | — | 按专栏过滤 |
| `authorId` | guid? | — | 按**署名作者**过滤 |
| `keyword` | string? | — | 关键词，走**中文全文检索** |
| `includeUnpublished` | bool | `false` | `true` 时含草稿（**需登录**） |
| `mine` | bool | `false` | `true` 时只看自己创建的（作者工作区） |

**权限细节**（服务层强制，不依赖端点鉴权）：

| 情形 | 行为 |
|---|---|
| 匿名 + `includeUnpublished=false` | 只返回已发布文章 |
| `includeUnpublished=true` + 非 Admin | 强制限定为**当前账号创建的**文章；未登录 → **403 / `4030`** |
| `mine=true` | 只返回当前账号创建的文章；**未登录时视为未指定**（不报错，返回公开列表） |
| Admin | 不受上述限制 |

**响应 `data`**：`PagedResult<PostCardDto>`（§1.3；完整示例见 §2 例①）。
`PostCardDto` = `id` / `title` / `summary` / `coverImage`（空串 = 未设置封面）/
`categoryId` / `categoryName` / `tags[{id,name}]` / `publishedAt` / `viewCount`。

### 4.2 `GET /api/posts/{id}` — 详情（计数）

**行为**：每次调用让 `ViewCount + 1`——数据库侧原子自增，**跳过乐观锁**，并同步刷新详情缓存。

**草稿保护**：未发布文章仅 Admin 与**创建者账号**（按 `CreatedByUserId` 判定，**不是 `AuthorId`**）
可读；不可读时返回 **404 / `4040`**（不是 403，避免探测存在性）。

**响应 `data`**：`PostDetailDto`（完整示例见 §2 例②）。字段要点：

- `content` 是 Markdown 原文；`tags` 为 `[{id,name}]`；`collections` 为 `[{id,title,slug}]`
- `publishedAt = null` 表示草稿；`wordCount` 是正文字数
- `createdByUserId` 是**归属校验用**的账号 id（前端据此判断"能不能编辑"）
- `version` 是乐观锁版本号，**写操作必须原样回传**

**错误**：404 / `4040`（不存在，或草稿且无权读）。

### 4.3 `GET /api/posts/{id}/readonly` — 详情（不计数）

返回结构与 §4.2 **完全相同**，两点区别：**不让浏览量 +1**（否则"后台每点一次编辑就 +1"
污染统计）、**必须登录**（ContentWriter）。管理端与编辑器取数据一律用这个端点；
未发布文章仍受草稿权限保护（404）。

### 4.4 `GET /api/posts/archives` — 归档

无参数，**返回数组而非分页结构**：`ArchiveGroupDto[]`，
每项 `{ year: int, month: int, items: [{ id, title, publishedAt }] }`。

### 4.5 `GET /api/posts/search` — 全文检索

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `keyword` | string | **必填** | 缺失 → 400 / `4001`（统一体，见 §1.1） |
| `page` / `pageSize` | int | `1` / `12` | 同 §4.1 |

**响应 `data`**：`PagedResult<PostCardDto>`。

- **按相关度排序**：`ts_rank`，标题权重高于正文，同分按发布时间倒序
  （检索实现见 [02](./02-架构与数据模型.md) §9）
- **限流**：命中 `search` 规则（容量 20、速率 2/秒），是全站最严格的规则
  （规则表在 `appsettings.json` 的 `RateLimit` 节）；触发时 429 / `4091`

### 4.6 `POST /api/posts` — 创建

请求体 `CreatePostRequest`：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `title` | string | ✅ | ≤ 200 字符 |
| `content` | string | ✅ | Markdown |
| `summary` | string? | — | **留空则自动取正文前 50 字**；填写则以填写内容为准（≤ 200 字符） |
| `coverImage` | string? | — | 只接受 `/api/files/...` 或空串，见 §4.7 |
| `categoryId` | guid? | — | 分类不存在 → 404 / `4040` |
| `tagIds` | guid[]? | — | 存在非法 id → 400 / `4001` |
| `collectionIds` | guid[]? | — | 所属专栏（可多个，T2）；`null` = 不建立关联、`[]` = 清空 |
| `authorId` | guid? | — | **仅管理员可指定**；作者账号登录时忽略（强制为自己关联的署名） |
| `publish` | bool | — | 默认 **`true`**（创建即发布） |

响应 `data`：`PostDetailDto`（§4.2）。

### 4.7 `coverImage` 的媒体白名单

`coverImage` 会被直接放进 `<img src>`，因此**只接受两类取值**（其余一律 400 / `4001`）：

| 取值 | 结果 |
|---|---|
| `null` / `""` / 空白 | ✅ 归一化为空串（不设置封面） |
| `/api/files/2026/09/xxx.webp` | ✅ 本站上传的地址 |
| `https://evil.com/x.png`、`//evil.com/x.png` | ❌ 400 / `4001`（外链：访客 IP 泄露、混合内容告警） |
| `javascript:alert(1)`、`data:image/svg+xml;…` | ❌ 400 / `4001`（危险 scheme） |
| `/api/files/../../etc/passwd`、`/api/files//evil.com/x` | ❌ 400 / `4001`（穿越 / 归一化歧义） |

实现见 `Blog.Application/Common/MediaPath.cs`（**白名单，不是黑名单**），
由 `PostService.CreateAsync` / `UpdateAsync` 调用；作者头像、站点 Logo、首屏背景图共用同一套规则，
完整论证见 [02](./02-架构与数据模型.md) §10.6。

### 4.8 `PUT /api/posts/{id}` — 更新

**权限**：✍️ ContentWriter **+ 归属校验**——非 Admin 只能改自己创建的（否则 403 / `4030`）。
归属只认 `CreatedByUserId`（创建这篇文章的**账号**），不认 `AuthorId`。

请求体 `UpdatePostRequest`：字段与创建相同（§4.6 前 7 个字段，规则一致），
末尾多一个 **`version`（int，必填）**。**不含 `publish`**——发布/下架走 §4.9。
（更新时 `collectionIds` 传 `null` 表示**不改动**专栏关联，传 `[]` 才是清空。）

响应 `data`：`PostDetailDto`。**错误**：403（不是自己的）、404、409（版本冲突）。

### 4.9 `POST /api/posts/{id}/publish` — 发布 / 下架

**权限**：✍️ ContentWriter + 归属校验。参数走**查询串**（不是请求体）：

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `version` | int | **必填** | 乐观锁版本号，缺失或 `< 1` → 4001 |
| `publish` | bool | `true` | `true` = 发布，`false` = 下架 |

**行为**：发布**幂等**——重复发布不会覆盖首次发布时间（`Post.Publish()` 是 `PublishedAt ??= UtcNow`），
否则归档排序会被打乱；下架把 `PublishedAt` 置回 `null`。响应 `data`：`PostDetailDto`。

### 4.10 `DELETE /api/posts/{id}` — 软删除

**权限**：✍️ ContentWriter + 归属校验。查询参数：`version`（int，必填）。响应 `data`：`null`。

**行为**：软删除（置 `IsDeleted`），数据保留；全局查询过滤器 `HasQueryFilter(e => !e.IsDeleted)`
让列表与详情都不再可见。

---

## 5. 作者 `/api/authors`（#13–#18）

`AuthorsController.cs`

> **注意语义**：这里的"作者"是**内容层的署名对象**，不是登录账号——账号管理在 `/api/users`（§9）。
> 两者通过可空的 `User.AuthorId` 弱关联，允许存在没有账号的作者。

`AuthorDto`：

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | guid | |
| `name` | string | ≤ 100 字符 |
| `email` | string | ≤ 100 字符，**展示用联系方式，不是登录邮箱** |
| `avatar` | string | 本站上传地址或空串（白名单规则见 §4.7） |
| `bio` | string | ≤ 500 字符 |
| `createdAt` | string | |
| `version` | int | 乐观锁版本号 |

请求体：创建 `{ name, email, bio, avatar }`；更新 `{ name, email, bio, avatar, version }`；
删除 `?version=1`。列表与单个查询返回 `AuthorDto`（详情不存在 → 404 / `4040`）。

**`GET /api/authors/me`**：返回当前登录账号关联的署名对象。**错误**：**404 / `4040`**——
未关联任何作者时消息为"当前账号未关联作者，请联系管理员在「账号管理」中为你关联署名身份"。

> 这是"个人资料"页的取数端点。前端应把 404 当作"未关联"的**正常状态**，而不是错误弹窗。

**`PUT /api/authors/{id}`**：ContentWriter 策略之上，服务端再按 `ICurrentUser.AuthorId` 收窄——
**Admin 可改任何作者，Author 只能改自己关联的那位**；越权 → **403 / `4030`**
（"无权修改他人的作者资料"）。

> ⚠️ `avatar` 必须在服务端校验：前端把输入框改成「只能上传」只是 UI 约束，
> `curl` 直打接口依然会经过白名单（`MediaUrlValidationTests` 就是绕开前端直接打 HTTP 的）。

**`DELETE /api/authors/{id}`**：👑 AdminOnly。软删除作者；其署名文章的 `AuthorId` 由外键
**SetNull** 置空，**文章保留**（不会连带删文章）。

---

## 6. 分类与标签 `/api/categories`、`/api/tags`（#19–#26）

`CategoriesController.cs` / `TagsController.cs`

两个控制器**完全对称**：读公开、**写仅 Admin**（按 T6，Author 不能创建分类/标签），
各 4 个端点（列表 / 创建 / 更新 / 删除），错误码相同，只有名称长度不同。

| | 分类 `/api/categories` | 标签 `/api/tags` |
|---|---|---|
| DTO | `{ id, name, postCount, version }` | 同左 |
| `name` 上限 | 100 字符 | **50** 字符 |
| 请求体 | 创建 `{ name }`；更新 `{ name, version }`；删除 `?version=1` | 同左 |
| 错误 | 4002 名称重复（软删除后可重建同名）；4001 名称为空 / 超长 | 同左 |

> 前端用 `canManageTaxonomy` 隐藏"快速新建分类"入口，但**后端独立校验，不依赖前端隐藏**。

---

## 7. 专栏 `/api/collections`（#27–#33）

`CollectionsController.cs`

`CollectionDto`：

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | guid | |
| `title` | string | ≤ 200 字符 |
| `slug` | string | ≤ 200 字符，唯一，用于 URL（只允许小写字母、数字与中划线） |
| `description` | string | ≤ 500 字符 |
| `coverImage` | string | 本站上传地址或空串 |
| `sortOrder` | int | 专栏之间的人工排序 |
| `isPublished` | bool | 未发布专栏对外表现为不存在 |
| `postCount` | int | **只统计已发布文章** |
| `version` | int | 乐观锁版本号 |

创建 `CreateCollectionRequest`：`{ title, slug, description, coverImage, sortOrder, isPublished }`；
更新 `UpdateCollectionRequest`：同上 + `version`；删除 `?version=1`。

### 7.1 列表与详情

- `GET /api/collections`：唯一参数 `includeUnpublished`（bool，默认 `false`）；
  传 `true` 需 **Admin**，非 Admin 得 **403 / `4030`**。
- `GET /api/collections/{slug}`：返回 `CollectionDetailDto` = `CollectionDto` + `posts`，
  元素为 `CollectionPostItemDto` = `{ id, title, summary, coverImage, publishedAt, viewCount, sortOrder }`。
  - **排序**：按 `PostCollection.SortOrder`（专栏内人为编排的顺序），**不是发布时间**
  - **未发布保护**：`isPublished = false` 时**非 Admin 得到 404 / `4040`**（不暴露存在性）
- `GET /api/collections/id/{id}`：👑 AdminOnly。与按 slug 的接口**刻意分开**——把"未发布的可见性
  判断"从公开路径里彻底移出去，管理端可无条件取到完整数据（含未发布专栏与未发布文章）。

### 7.2 `PUT /api/collections/{id}/posts` — 整体编排

请求体：`{ "postIds": ["guid1", "guid2"], "version": 1 }`。

**语义**：**整体覆盖**该专栏的文章集合——`postIds` 的**顺序即专栏内顺序**（数组下标写入
`SortOrder`），不在列表中的文章会被移出该专栏。存在非法文章 id → 400 / `4001`；
`version` 不匹配 → 409 / `4090`。响应 `data`：`CollectionDetailDto`。

**创建 / 更新的错误**：4001（标题或 slug 为空、slug 含非法字符、版本号非法）、
4003（slug 已被占用）、4040（不存在）。

---

## 8. 站点 `/api/site`（#34–#39）

`SiteController.cs`

> **读保持公开**：`GET /api/site/social-links` 是公开首屏要用的（前端 store 启动时调用它以
> 渲染社交图标），因此**不能**加鉴权。三个写接口则仅 Admin。

### 8.1 `GET /api/site/config`

| 字段 | 类型 | 说明 |
|---|---|---|
| `siteName` | string | 站点名 |
| `logoName` | string | Logo 圆点里的文字（缺省 `"k"`），与 `siteName` **分开配置** |
| `siteLogo` | string? | 自定义 Logo 地址；`null` = 未设置，前端回退到「渐变圆点 + `logoName`」 |
| `heroSubtitles` | string[] | 打字机文案列表 |
| `heroBackgrounds` | string[] | 首屏背景图（多张，前端每次随机展示一张）；空数组 = 用内置渐变 |
| `foundingDate` | string? | 建站日期 |
| `versions` | `Record<string, number>` | **各配置项当前的版本号**（`Key -> Version`） |

- `versions` 里**缺失的 Key** = 该项尚未创建，保存时版本号传 `0`（走 §1.4 的新增分支）；
  Key **保留原始大小写**（`"SiteName"` 而不是 `"siteName"`），前端按常量精确取值。
- ⚠️ `heroBackgrounds`（复数）对应的配置 **Key 仍叫 `HeroBackground`**（单数）：为避免多一个
  配置行的版本号迁移，只把 Value 从"一个裸 URL"升级成"JSON 数组字符串"；读取侧对历史裸地址
  保持兼容（`MediaPath.ParseList`）。

### 8.2 `PUT /api/site/config` — 逐项更新

请求体：`{ key, value, version }`（示例见 §2 例③）。**只更新一个 Key，不是整体替换**；
`versions` 里没有的 Key 直接新增（忽略 `version`），已存在的 Key 必须带 `version ≥ 1`。

| Key | 值格式 | 服务端校验 |
|---|---|---|
| `SiteName` | 字符串 | — |
| `LogoName` | 字符串（建议 1~2 字符） | — |
| `SiteLogo` | `/api/files/...` 相对地址，或空串 | **媒体白名单**（同 §4.7） |
| `HeroSubtitles` | **JSON 数组字符串**，如 `["a","b"]` | — |
| `FoundingDate` | `yyyy-MM-dd` | — |
| `HeroBackground` | **JSON 数组字符串**，如 `["/api/files/a.webp"]` | **媒体白名单**逐项校验 + 最多 20 张 |

媒体校验失败一律 **400 / `4001`**，提示形如
`站点 Logo：只允许使用本站上传的图片（地址须以 /api/files/ 开头），不支持填写外部链接`。
`HeroBackground` 的数组**任意一项不合法就整批拒绝**；JSON 格式损坏也报 4001
（**不会**静默存成空数组——那会让配置无声无息地丢失）。
响应 `data`：更新后的完整 `SiteConfigDto`。

### 8.3 `GET /api/site/social-links`

参数 `includeHidden`（bool，默认 `false`）：管理端配置页传 `true` 以同时返回隐藏项。
`SocialLinkDto` = `{ id, name, icon, url, sortOrder, isVisible, version }`。

> **`includeHidden` 不校验权限**——匿名也能传 `true` 拿到隐藏项。这是**刻意的**：该端点是
> 公开首屏要用的，而隐藏项只是前端不展示、并非敏感数据。**写接口才是安全边界**（见 §12）。

### 8.4 `PUT /api/site/social-links` — 批量保存

请求体是**数组** `UpsertSocialLinkRequest[]`：

```json
[{ "id": null, "name": "GitHub", "icon": "github", "url": "https://github.com/kky", "sortOrder": 0, "isVisible": true, "version": null }]
```

`id = null` 表示新增（版本号为 1）；更新时**必须**携带当前 `version`（缺失 → 4001）。
**行为**：按 `sortOrder` 逐条新增/更新后重排。响应 `data`：保存后的完整 `SocialLinkDto[]`
（**含隐藏项**，便于管理端回填）。**错误**：4001（名称或地址为空、字段超长）、4040（id 不存在）。

### 8.5 `DELETE /api/site/social-links/{id}`

查询参数 `version`（必填）。软删除，响应 `data` 为 `null`。

### 8.6 `GET /api/site/stats`

`SiteStatsDto` = `siteDays`（由 `FoundingDate` 计算）、`totalPosts`、`totalWords`（long）、
`totalViews`、`tagCount`、`categoryCount`。

---

## 9. 账号 `/api/users`（#40–#45）

`UsersController.cs` — **整个控制器仅管理员可访问**（类级 `[Authorize(Policy = "AdminOnly")]`，
六个端点全部 👑 AdminOnly，不再逐个标注）。

> 这是**作者账号的唯一创建入口**（T1：作者不开放自助注册）。响应 DTO 不含任何凭据字段。

`UserDto` = `id` / `email` / `role` / `isActive` / `authorId` / `authorName` /
`lastLoginAt` / `createdAt` / `version`。

| 端点 | 请求体 | 说明 |
|---|---|---|
| `POST /users` | `{ email, password, role, authorId }` | `role` 只能是 `Admin` / `Author`；`authorId` 即"给某位作者开通登录"，不存在 → 4001 |
| `PUT /users/{id}` | `{ role, authorId, isActive, version }` | 改角色 / 关联作者 / 启用状态；**不修改凭据**（密码走重置端点） |
| `POST /users/{id}/reset-password` | `{ newPassword, version }` | 提升 `TokenVersion`，该账号旧 token 立即失效 |
| `POST /users/{id}/disable` | `?version=`（必填） | `IsActive = false` + 提升 `TokenVersion`；**不物理删除**，保留审计线索 |

**错误**：400 / `4003`（邮箱已被占用）、400 / `4001`（邮箱格式或密码长度不合法）、
404 / `4040`（账号不存在）、409 / `4090`（版本冲突）。

---

## 10. 文件 `/api/files`（#46–#47）

`FilesController.cs`

> **上传需要 ContentWriter**：上传是写操作，匿名开放会让任何人都能往服务器塞文件。
> **读取保持公开**——文章封面与作者头像必须能被匿名访客加载。

### 10.1 `POST /api/files/upload`

- Content-Type：`multipart/form-data`，字段名 **`file`**
- 框架层上限：`[RequestSizeLimit(50MB)]`；业务层上限：`FileStorage:MaxFileSize`（默认 10MB）
- 响应 `data`：`url`（可直接用于 `coverImage` / `avatar` 的相对地址）、`storedBytes`、
  `originalBytes`、`converted`（是否转成了 WebP）

> ⚠️ **上传的错误用 HTTP 状态码表达（400 / 413），与其它接口完全一致**——
> 不要用"HTTP 200 + body 里的 `code`"判断上传是否成功：

| 场景 | HTTP | `code` | `message` |
|---|---|---|---|
| 文件为空 | **400** | `4001` | 文件不能为空 |
| 超过业务上限（默认 10MB） | **413** | `4130` | 文件大小超过限制（10MB） |
| 扩展名不在白名单 | **400** | `4001` | 不支持的文件类型：`.svg` |
| 匿名 / 角色不足 | **401 / 403** | `4010` / `4030` | — |

**允许的扩展名**（`LocalFileStorageService.AllowedExtensions`）：

| 类别 | 扩展名 |
|---|---|
| 图片 | `.png` `.jpg` `.jpeg` `.gif` `.webp` `.ico` |
| 其他 | `.mp4` `.webm` `.pdf` `.zip` |

> ⚠️ **`.svg` 被刻意排除**：SVG 可以内嵌 `<script>`，而本站文件接口是**同源内联**下发的——
> 直接打开 `/api/files/xxx.svg` 会让脚本在站点源上执行，构成**存储型 XSS**（缺口 G11）。
> 完整论证见 [02](./02-架构与数据模型.md) §13.3。

> 💡 **上游还有一道 nginx 限制**：`client_max_body_size 12m`。经 nginx 访问时，**超过 12MB
> 的请求到不了应用**，由 nginx 直接返回自己的 413 页面（**不是**统一响应体）。12MB 刻意比业务
> 上限 10MB 大一点，让业务上限先起作用、由应用给出可读提示。部署侧见
> [05-运维与部署手册.md](./05-运维与部署手册.md)。

### 10.2 `GET /api/files/{**path}` — 读取

- 命中时下发 `Cache-Control: public,max-age=86400`
- 未命中返回 **404 / `4040`** + 统一响应体，**且不下发缓存头**

> ⚠️ **缓存头只能在确认命中后设置**：若给 404 也盖上 `max-age=86400`，浏览器会把这个 404
> 缓存一整天——文件补回来后用户仍长时间看到空白，必须手动强刷。

**路径穿越防护**：`LocalFileStorageService` 校验解析后的绝对路径仍在存储根目录内
（与 `MediaPath` 白名单构成纵深防御，见 [02](./02-架构与数据模型.md) §10.6）。

---

## 11. 端点统计与非控制器端点

端点数：`Auth` 3、`Posts` 9、`Authors` 6、`Categories` 4、`Tags` 4、`Collections` 7、
`Site` 6、`Users` 6、`Files` 2 —— **合计 47**，与 §1.6 的总表逐条对应。

两个**不在 `/api` 下、也不在控制器里**的端点：

| 端点 | 说明 |
|---|---|
| `GET /` | 存活探针（`Program.cs:212`），返回 `{ name: "Blog API", status: "running" }`——**只证明进程在，不检查依赖**，也**不是**统一响应体 |
| `GET /health` | 健康检查（`HealthCheckSetup.cs:55`），**对外只返回 `Healthy` / `Unhealthy` 纯文本**（200 / 503），详情只写日志 |

`/health` 的检查项有四项（PostgreSQL 连通性、`zhparser` 与 `chinese` 检索配置是否真的可用、
迁移是否全部应用、Redis 连通性），任一项失败整体 503；设计说明见
[02-架构与数据模型.md](./02-架构与数据模型.md) §15。

---

## 12. 权限矩阵

本文的权限标记逐条取自 `Controllers/*.cs` 的 `[Authorize(...)]`。汇总：

| 控制器 | 读接口 | 写接口 | 服务层附加约束 |
|---|---|---|---|
| `AuthController` | `me` 需登录 | 登录 `AllowAnonymous`；`logout` 需登录 | — |
| `PostsController` | 公开（`readonly` 需 ContentWriter） | `ContentWriter` | **归属校验**：非 Admin 只能改 / 删 / 发布自己创建的 |
| `AuthorsController` | 公开（`me` 需 ContentWriter） | POST / DELETE `AdminOnly`；PUT `ContentWriter` | PUT：作者只能改自己关联的 `AuthorId` |
| `CategoriesController` | 公开 | `AdminOnly` | — |
| `TagsController` | 公开 | `AdminOnly` | — |
| `CollectionsController` | 公开（`id/{id}` 需 `AdminOnly`） | `AdminOnly` | 列表 `includeUnpublished=true` 需 Admin；未发布详情对非 Admin 返回 404 |
| `SiteController` | 公开 | `AdminOnly` | — |
| `UsersController` | `AdminOnly`（类级） | `AdminOnly`（类级） | — |
| `FilesController` | 公开 | `ContentWriter`（仅上传） | — |

**读接口一律公开**——公开站点必须能匿名浏览；唯二例外是 `/api/posts/{id}/readonly`
（编辑器用，`ContentWriter`）与 `/api/collections/id/{id}`（管理端用，`AdminOnly`）。

> ⚠️ **改控制器时守住一条原则**：前端隐藏按钮**不是安全边界**，
> 新加的写接口必须自己带 `[Authorize]`。这一点有过教训，排查过程见
> [archive/问题排查记录.md](../archive/问题排查记录.md)。

---

## 13. 前端调用入口对照

| 前端文件 | 对应后端控制器 |
|---|---|
| `src/api/auth.ts` | Auth（`/api/auth`） |
| `src/api/posts.ts` | Posts |
| `src/api/authors.ts` | Authors |
| `src/api/categories.ts` | Categories |
| `src/api/tags.ts` | Tags |
| `src/api/collections.ts` | Collections |
| `src/api/site.ts` | Site |
| `src/api/users.ts` | Users |
| `src/api/files.ts` | Files |
| `src/api/http.ts` | 统一封装：`{code,message,data}` 解析、错误码常量、401 回调、非 JSON 响应兜底 |

`src/api/http.ts` 的约定：请求前缀取自 `import.meta.env.VITE_API_BASE`（缺省空串，开发期由
Vite 代理转发）；`code !== 0` 抛 `ApiError(code, message)`，`message` 直接来自后端可直接展示；
上传用 `FormData` 且**不能**预设 `Content-Type`（boundary 必须由浏览器生成）。

---

## 14. 与代码的对应关系

| 内容 | 文件 |
|---|---|
| 全部端点定义 | `Blog.Backend/Blog.WebApi/Controllers/*.cs` |
| 请求 / 响应 DTO | `Blog.Backend/Blog.Application/Services/*/*Dtos.cs`（Author 为 `AuthorDto.cs`） |
| 错误码 | `Blog.Backend/Blog.Application/Common/ErrorCodes.cs` |
| 统一响应体与模型校验接管、授权策略 | `Blog.Backend/Blog.Application/Common/ApiResponse.cs`、`Blog.WebApi/Program.cs` |
| 前端类型与封装对齐 | `Blog.FrontEnd/src/types/index.ts`、`Blog.FrontEnd/src/api/http.ts` |

全项目代码索引总表见 [00-文档规范.md](./00-文档规范.md)。
