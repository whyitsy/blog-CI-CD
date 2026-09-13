# 05 · API 接口清单

> **用途**：后端所有 HTTP 端点的权威清单（路径、方法、权限、参数、响应、错误）。
> **读者**：前端开发者、接口调试者、需要确认某个端点到底存不存在时。
> **最后核对**：2026-09-11 ｜ **代码依据**：仓库 HEAD（`Blog.WebApi/Controllers/`）
>
> 权限模型与错误码的完整说明见 [03-后端设计.md](./03-后端设计.md) §2、§5。

---

## 1. 通用约定

| 项 | 约定 |
|---|---|
| 基础路径 | 全部以 `/api` 开头 |
| 数据格式 | JSON（`Content-Type: application/json`）；**文件上传除外**（`multipart/form-data`） |
| 字段命名 | camelCase（前后端一致） |
| 认证 | `Authorization: Bearer <token>` |
| 时间格式 | ISO 8601 带时区（`DateTimeOffset`） |
| 主键 | GUID 字符串 |

### 1.1 权限标记说明

| 标记 | 含义 |
|---|---|
| 🌐 **公开** | 无需认证 |
| 🔑 **需登录** | 需任意有效 token（`[Authorize]` 无策略） |
| ✍️ **ContentWriter** | 需 `Admin` 或 `Author` 角色 |
| 👑 **AdminOnly** | 仅 `Admin` 角色 |

> ⚠️ **登录入口不承担权限边界**：`/api/auth/login` 不限定角色，
> 管理员与作者共用。权限由**具体接口上的策略**强制。

### 1.2 状态标记

本文档中所有端点均为 `[已实现]`。若某接口为"规划中"会显式标注。

---

## 2. 统一响应体

**所有**接口（含错误）返回同一结构：

```json
{
  "code": 0,
  "message": "ok",
  "data": { }
}
```

| 字段 | 说明 |
|---|---|
| `code` | `0` = 成功；非 0 = 业务错误码（见 §2.1） |
| `message` | 成功时为 `"ok"`；失败时是可直接展示给用户的中文提示 |
| `data` | 成功时的载荷；无返回数据的接口为 `null` |

HTTP 状态码**同时**被设置成语义正确的值（401/403/404/409/429），不只是 200。

### 2.1 错误码表

| code | HTTP | 含义 |
|---|---|---|
| `0` | 200 | 成功 |
| `4001` | 400 | 参数校验失败（含缺少/非法版本号） |
| `4002` | 400 | 业务规则不满足（如名称重复） |
| `4003` | 400 | 资源重复（如邮箱已被占用） |
| `4010` | 401 | 未认证：缺 token / 无效 / 过期 / 账号被停用 |
| `4030` | 403 | 无权限：角色不足，或越权访问他人资源 |
| `4040` | 404 | 资源不存在（**草稿与未发布专栏对无权限者也用此码**） |
| `4090` | 409 | 乐观锁并发冲突 |
| `4091` | 429 | 触发限流 |
| `4130` | 413 | 请求体过大 |
| `5000` | 500 | 系统异常 |

### 2.2 分页结构

列表类接口统一返回：

```json
{
  "items": [],
  "total": 0,
  "page": 1,
  "pageSize": 12,
  "totalPages": 0
}
```

> ⚠️ 字段是 **`total`**，不是 `totalCount`。前端类型 `PagedResult` 已与之对齐。
> 历史上曾因写成 `totalCount` 导致所有"共 N 篇"渲染为 `undefined`。

### 2.3 乐观锁约定

凡是**更新/删除**已存在资源的接口，都必须携带当前 `version`：

- 通过请求体传：字段名 `version`
- 通过查询串传：`?version=3`（删除、发布、停用等无请求体的操作）

版本不匹配返回 **409 / code 4090**。缺少或 `< 1` 的版本号返回 **4001**。

**写入语义统一规则**：

| 情形 | 语义 |
|---|---|
| 资源/配置 Key **不存在** | 视为新增，忽略 `version` |
| 资源/配置 Key **已存在** | 走乐观锁，`version` 必须 ≥ 1 |

---

## 3. 认证 `/api/auth`

`AuthController`

| # | 方法 | 路径 | 权限 | 说明 |
|---|---|---|---|---|
| 1 | POST | `/api/auth/login` | 🌐 公开 | 登录，返回 token 与用户信息 |
| 2 | GET | `/api/auth/me` | 🔑 需登录 | 当前登录用户 |
| 3 | POST | `/api/auth/logout` | 🔑 需登录 | 注销（使该账号**所有**旧 token 失效） |

**没有注册端点**（决策 T1）。作者账号由管理员在 `/api/users` 创建。

### 3.1 `POST /api/auth/login`

请求体：

```json
{ "email": "admin@example.com", "password": "Admin@12345" }
```

响应 `data`：

| 字段 | 类型 | 说明 |
|---|---|---|
| `token` | string | Access Token |
| `expiresAt` | string | 过期时间（ISO 8601） |
| `role` | string | `Admin` 或 `Author`（前端据此决定落地页） |
| `user` | object | 见 §3.1.1 |

#### 3.1.1 `CurrentUserDto` 结构

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | guid | 账号 id |
| `email` | string | 登录邮箱 |
| `role` | string | `Admin` / `Author` |
| `isActive` | bool | 是否启用 |
| `authorId` | guid? | 关联的署名对象；可为 null |
| `authorName` | string? | 关联作者名 |
| `lastLoginAt` | string? | 最近登录时间 |

> **此结构绝不包含 `PasswordHash`**：后端 DTO 从不返回任何凭据字段。

**错误**：401（`4010`，邮箱或密码错误 / 账号被停用）。

### 3.2 `GET /api/auth/me`

响应 `data`：`CurrentUserDto`（同 §3.1.1）。

**错误**：401（`4010`）。前端启动时用它确认本地 token 是否仍然有效
（能正确处理"token 未过期但账号已被停用/改密"）。

### 3.3 `POST /api/auth/logout`

无请求体、无响应数据。

**行为**：把该账号的 `TokenVersion + 1`，因此**该账号所有设备上的旧 token 立即失效**。

---

## 4. 文章 `/api/posts`

`PostsController`

| # | 方法 | 路径 | 权限 | 说明 |
|---|---|---|---|---|
| 4 | GET | `/api/posts` | 🌐 公开* | 分页列表，支持多种过滤 |
| 5 | GET | `/api/posts/{id}` | 🌐 公开* | 详情（**浏览量 +1**） |
| 6 | GET | `/api/posts/{id}/readonly` | ✍️ ContentWriter | 详情（**不计数**），编辑器取数据用 |
| 7 | GET | `/api/posts/archives` | 🌐 公开 | 归档：按年月分组的数组（**非分页**） |
| 8 | GET | `/api/posts/search` | 🌐 公开 | 全文检索（等价于列表接口带 `keyword`） |
| 9 | POST | `/api/posts` | ✍️ ContentWriter | 创建 |
| 10 | PUT | `/api/posts/{id}` | ✍️ ContentWriter | 更新（乐观锁） |
| 11 | POST | `/api/posts/{id}/publish` | ✍️ ContentWriter | 发布 / 下架（乐观锁） |
| 12 | DELETE | `/api/posts/{id}` | ✍️ ContentWriter | 软删除（乐观锁） |

\* 带 `includeUnpublished=true` 或访问草稿时需登录，规则见 §4.1。

### 4.1 `GET /api/posts` — 分页列表

查询参数：

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `page` | int | `1` | 页码 |
| `pageSize` | int | `12` | 每页条数。**上限**：公开 50，管理端（`includeUnpublished`）100 |
| `categoryId` | guid? | — | 按分类过滤 |
| `tagId` | guid? | — | 按标签过滤 |
| `collectionId` | guid? | — | 按专栏过滤 |
| `authorId` | guid? | — | 按署名作者过滤 |
| `keyword` | string? | — | 关键词，走**中文全文检索** |
| `includeUnpublished` | bool | `false` | `true` 时含草稿（**需登录**） |
| `mine` | bool | `false` | `true` 时只看自己创建的（作者工作区） |

**权限细节**：

| 情形 | 行为 |
|---|---|
| 匿名 + `includeUnpublished=false` | 只返回已发布文章 |
| `includeUnpublished=true` + 非 Admin | 强制限定为**当前账号创建的**文章；未登录则 **403** |
| `mine=true` + 非自己 | 403 |
| Admin | 不受上述限制 |

**响应 `data`**：`PagedResult<PostCardDto>`（结构见 §2.2）

`PostCardDto`：

| 字段 | 类型 |
|---|---|
| `id` | guid |
| `title` | string |
| `summary` | string |
| `coverImage` | string |
| `categoryId` | guid? |
| `categoryName` | string? |
| `tags` | `[{id, name}]` |
| `publishedAt` | string? |
| `viewCount` | int |

### 4.2 `GET /api/posts/{id}` — 详情（计数）

**行为**：每次调用让 `ViewCount + 1`（数据库侧原子自增，**跳过乐观锁**），
并同步刷新详情缓存。

**草稿保护**：未发布文章仅 Admin 与**创建者账号**可读；
不可读时返回 **404**（不是 403，避免探测存在性）。

**响应 `data`**：`PostDetailDto`

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | guid | |
| `title` | string | |
| `content` | string | Markdown 原文 |
| `summary` | string | |
| `coverImage` | string | |
| `categoryId` | guid? | |
| `categoryName` | string? | |
| `tags` | `[{id, name}]` | |
| `collections` | `[{id, title, slug}]` | 所属专栏 |
| `authorId` | guid | 署名作者 |
| `authorName` | string? | |
| `authorAvatar` | string? | |
| `createdByUserId` | guid? | **归属校验用**（前端据此判断"能不能编辑"） |
| `publishedAt` | string? | `null` = 草稿 |
| `updatedAt` | string? | |
| `viewCount` | int | |
| `wordCount` | int | |
| `version` | int | **乐观锁版本号，写操作必须回传** |

### 4.3 `GET /api/posts/{id}/readonly` — 详情（不计数）

与 §4.2 返回结构完全相同，两点区别：

1. **不让浏览量 +1**（避免"后台每点一次编辑就 +1"污染统计）
2. **必须登录**（ContentWriter 策略）

管理端 / 编辑器取数据一律用这个端点。未发布文章仍受草稿权限保护。

### 4.4 `GET /api/posts/archives` — 归档

无参数。**返回数组而非分页结构**。

响应 `data`：`ArchiveGroupDto[]`

```json
[
  { "year": 2026, "month": 9, "items": [ { "id": "...", "title": "...", "publishedAt": "..." } ] }
]
```

### 4.5 `GET /api/posts/search` — 全文检索

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `keyword` | string | **必填** | 关键词 |
| `page` | int | `1` | |
| `pageSize` | int | `12` | |

**响应 `data`**：`PagedResult<PostCardDto>`

> **结果按相关度排序**（`ts_rank`，标题权重高于正文；同分按发布时间倒序）。
> 实现说明见 [03-后端设计.md](./03-后端设计.md) §7.4。
>
> **限流**：此路径命中 `search` 规则（容量 20，速率 2/秒），
> 是全站最严格的限流规则。

### 4.6 `POST /api/posts` — 创建

权限：✍️ ContentWriter

请求体 `CreatePostRequest`：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `title` | string | ✅ | ≤200 字符 |
| `content` | string | ✅ | Markdown |
| `summary` | string? | — | **留空则自动取正文前 50 字**；填写则以填写内容为准 |
| `coverImage` | string? | — | 只接受 `/api/files/...` 或空串，见 §4.6.1 |
| `categoryId` | guid? | — | 分类不存在返回 404 |
| `tagIds` | guid[]? | — | 存在非法 id 返回 4001 |
| `collectionIds` | guid[]? | — | 所属专栏（可多个，多对多） |
| `authorId` | guid? | — | **仅管理员可指定**；作者登录时忽略（强制为自己） |
| `publish` | bool | — | 默认 **`true`**（创建即发布） |

响应 `data`：`PostDetailDto`

#### 4.6.1 `coverImage` 的媒体白名单（2026-09-13 起）

`coverImage` 会被直接放进 `<img src>`，因此**只接受两种取值**：

| 取值 | 结果 |
|---|---|
| `null` / `""` / 空白 | ✅ 归一化为空串（不设置封面，卡片用内置渐变占位） |
| `/api/files/2026/09/xxx.webp` | ✅ 本站上传的地址 |
| `https://evil.com/x.png`、`//evil.com/x.png` | ❌ 400 / 4001（外链：访客 IP 泄露、混合内容告警） |
| `javascript:alert(1)`、`data:image/svg+xml;…` | ❌ 400 / 4001（危险 scheme） |
| `/api/files/../../etc/passwd`、`/api/files//evil.com/x` | ❌ 400 / 4001（穿越 / 归一化歧义） |

实现见 `Blog.Application/Common/MediaPath.cs`（白名单，非黑名单），
由 `PostService.CreateAsync` / `UpdateAsync` 调用；作者头像、站点 Logo、
首屏背景图共用同一套规则。

### 4.7 `PUT /api/posts/{id}` — 更新

权限：✍️ ContentWriter + **归属校验**（非 Admin 只能改自己创建的 → 否则 403）

请求体 `UpdatePostRequest`：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `title` | string | ✅ | |
| `content` | string | ✅ | |
| `summary` | string? | — | 规则同创建 |
| `coverImage` | string? | — | 只接受 `/api/files/...` 或空串，见 §4.6.1 |
| `categoryId` | guid? | — | |
| `tagIds` | guid[]? | — | |
| `collectionIds` | guid[]? | — | |
| `version` | int | ✅ | 乐观锁版本号 |

**注意**：更新接口**不含 `publish`**，发布/下架走 §4.8 的独立端点。

响应 `data`：`PostDetailDto`

**错误**：403（不是自己的文章）、404、409（版本冲突）

### 4.8 `POST /api/posts/{id}/publish` — 发布 / 下架

权限：✍️ ContentWriter + 归属校验

查询参数（**注意是查询串，不是请求体**）：

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `version` | int | **必填** | 乐观锁版本号 |
| `publish` | bool | `true` | `true` = 发布，`false` = 下架 |

**行为**：发布是**幂等**的——重复发布不会覆盖首次发布时间（否则归档排序会被打乱）。

响应 `data`：`PostDetailDto`

### 4.9 `DELETE /api/posts/{id}` — 软删除

权限：✍️ ContentWriter + 归属校验

查询参数：`version`（int，必填）

响应 `data`：`null`

**行为**：软删除（置 `IsDeleted`），数据保留。列表与详情均不再可见。

---

## 5. 作者 `/api/authors`

`AuthorsController`

> **注意语义**：这里的"作者"是**内容层的署名对象**，不是登录账号。
> 账号管理在 `/api/users`（§10）。

| # | 方法 | 路径 | 权限 | 说明 |
|---|---|---|---|---|
| 13 | GET | `/api/authors` | 🌐 公开 | 作者列表 |
| 14 | GET | `/api/authors/{id}` | 🌐 公开 | 单个作者 |
| 15 | GET | `/api/authors/me` | ✍️ ContentWriter | 当前账号的署名身份 |
| 16 | POST | `/api/authors` | 👑 AdminOnly | 创建作者 |
| 17 | PUT | `/api/authors/{id}` | ✍️ ContentWriter | 更新（管理员可改任何人，作者只能改自己） |
| 18 | DELETE | `/api/authors/{id}` | 👑 AdminOnly | 软删除作者 |

`AuthorDto`：

| 字段 | 类型 |
|---|---|
| `id` | guid |
| `name` | string |
| `email` | string |
| `avatar` | string |
| `bio` | string |
| `createdAt` | string |
| `version` | int |

### 5.1 `GET /api/authors/me`

**权限**：✍️ ContentWriter

**行为**：返回当前登录账号关联的署名对象。

**错误**：**404** —— 当账号未关联任何作者时，消息为
"当前账号未关联作者，请联系管理员在「账号管理」中为你关联署名身份"。

> 这是"个人资料"页的取数端点。前端应把 404 当作"未关联"的正常状态处理，
> 而不是当作错误弹窗。

### 5.2 `PUT /api/authors/{id}` — 更新

**权限**：✍️ ContentWriter，但服务端进一步判断：

| 请求者 | 允许修改 |
|---|---|
| Admin | 任何作者 |
| Author | **只有自己关联的那位作者**（按 `ICurrentUser.AuthorId` 判定） |

越权时返回 **403**，消息"无权修改他人的作者资料"。

请求体 `UpdateAuthorRequest`：

```json
{ "name": "...", "email": "...", "bio": "...", "avatar": "...", "version": 1 }
```

> ⚠️ **`avatar` 只接受本站上传的地址**（`/api/files/...`）或空串。
> 传外部 URL、`//host/x.png`、`javascript:`、`data:`、含 `..` 的路径一律
> **400 / code 4001**。字段中文名为「头像」。
>
> 这条校验**必须在服务端**：前端已经把头像输入框改成「只能上传」，
> 但那只是 UI 约束，`curl` 直打接口依然会经过这条白名单。
> 完整规则见 §9.2「媒体白名单校验」。

> 注意：这是**署名信息**的更新，`email` 是展示用联系方式，
> **不是登录邮箱**（登录邮箱在 `/api/users` 改）。

### 5.3 `DELETE /api/authors/{id}` — 软删除

**权限**：👑 AdminOnly　**查询参数**：`version`（必填）

**行为**：软删除作者。其署名文章的 `AuthorId` 由 **SetNull** 置空，
**文章保留**（不会连带删文章）。

---

## 6. 分类 `/api/categories`

`CategoriesController`

| # | 方法 | 路径 | 权限 | 说明 |
|---|---|---|---|---|
| 19 | GET | `/api/categories` | 🌐 公开 | 列表（含文章数） |
| 20 | POST | `/api/categories` | 👑 AdminOnly | 创建 |
| 21 | PUT | `/api/categories/{id}` | 👑 AdminOnly | 更新（乐观锁） |
| 22 | DELETE | `/api/categories/{id}` | 👑 AdminOnly | 软删除（乐观锁） |

> **写接口仅 Admin**（按 T6，Author 不能创建分类/标签）。
> 作者角色在编辑器里看不到"快速新建分类"入口（前端用 `canManageTaxonomy` 控制），
> 后端也独立校验，**不依赖前端隐藏**。

`CategoryDto`：

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | guid | |
| `name` | string | ≤100 字符，唯一（软删除后可重建同名） |
| `postCount` | int | 文章数 |
| `version` | int | |

- **创建** 请求体：`{ "name": "..." }`
- **更新** 请求体：`{ "name": "...", "version": 1 }`
- **删除** 查询参数：`?version=1`

**错误**：4002（名称重复）

---

## 7. 标签 `/api/tags`

`TagsController`

| # | 方法 | 路径 | 权限 | 说明 |
|---|---|---|---|---|
| 23 | GET | `/api/tags` | 🌐 公开 | 列表（含文章数） |
| 24 | POST | `/api/tags` | 👑 AdminOnly | 创建 |
| 25 | PUT | `/api/tags/{id}` | 👑 AdminOnly | 更新（乐观锁） |
| 26 | DELETE | `/api/tags/{id}` | 👑 AdminOnly | 软删除（乐观锁） |

结构与分类完全对称（含同样的 AdminOnly 限制）。
`TagDto`：`{ id, name, postCount, version }`。
`name` 上限 **50** 字符（比分类的 100 短）。

---

## 8. 专栏 `/api/collections`

`CollectionsController`

| # | 方法 | 路径 | 权限 | 说明 |
|---|---|---|---|---|
| 27 | GET | `/api/collections` | 🌐 公开* | 列表 |
| 28 | GET | `/api/collections/{slug}` | 🌐 公开* | 详情（按 slug，含该专栏文章） |
| 29 | GET | `/api/collections/id/{id}` | 👑 AdminOnly | 按 id 取详情（管理端，含未发布） |
| 30 | POST | `/api/collections` | 👑 AdminOnly | 创建 |
| 31 | PUT | `/api/collections/{id}` | 👑 AdminOnly | 更新（乐观锁） |
| 32 | DELETE | `/api/collections/{id}` | 👑 AdminOnly | 软删除（乐观锁） |
| 33 | PUT | `/api/collections/{id}/posts` | 👑 AdminOnly | **整体编排**专栏内文章与顺序 |

### 8.1 `GET /api/collections`

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `includeUnpublished` | bool | `false` | `true` 需 **Admin**，非 Admin 传 true 得 **403** |

`CollectionDto`：

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | guid | |
| `title` | string | ≤200 |
| `slug` | string | ≤200，唯一，用于 URL |
| `description` | string | ≤500 |
| `coverImage` | string | |
| `sortOrder` | int | 专栏之间排序 |
| `isPublished` | bool | |
| `postCount` | int | **只统计已发布文章** |
| `version` | int | |

### 8.2 `GET /api/collections/{slug}` — 详情

`CollectionDetailDto` = `CollectionDto` + `posts: CollectionPostItemDto[]`

`CollectionPostItemDto`：`{ id, title, summary, coverImage, publishedAt, viewCount, sortOrder }`

**排序**：按 `PostCollection.SortOrder`（专栏内人为编排的顺序），**不是发布时间**。

**未发布保护**：`isPublished = false` 时，**非 Admin 得到 404**（不暴露存在性）。

### 8.3 `GET /api/collections/id/{id}` — 管理端详情

权限：👑 AdminOnly

与 §8.2 分开的原因：把"未发布的可见性判断"从公开路径里彻底移出去，
管理端可以无条件取到完整数据（含未发布）。

### 8.4 `PUT /api/collections/{id}/posts` — 整体编排

权限：👑 AdminOnly

请求体 `SetCollectionPostsRequest`：

```json
{ "postIds": ["guid1", "guid2"], "version": 1 }
```

**语义**：**整体覆盖**该专栏的文章集合——`postIds` 的**顺序即专栏内顺序**
（数组下标写入 `SortOrder`）。不在列表中的文章会被移出该专栏。

响应 `data`：`CollectionDetailDto`

**创建/更新请求体**：

- 创建 `CreateCollectionRequest`：`{ title, slug, description, coverImage, sortOrder, isPublished }`
- 更新 `UpdateCollectionRequest`：同上 + `version`
- 删除：查询参数 `?version=1`

---

## 9. 站点 `/api/site`

`SiteController`

| # | 方法 | 路径 | 权限 | 说明 |
|---|---|---|---|---|
| 34 | GET | `/api/site/config` | 🌐 公开 | 首屏配置聚合 |
| 35 | PUT | `/api/site/config` | 👑 AdminOnly | 逐项更新配置（乐观锁） |
| 36 | GET | `/api/site/social-links` | 🌐 公开 | 社交链接 |
| 37 | PUT | `/api/site/social-links` | 👑 AdminOnly | 批量新增/更新 |
| 38 | DELETE | `/api/site/social-links/{id}` | 👑 AdminOnly | 软删除（乐观锁） |
| 39 | GET | `/api/site/stats` | 🌐 公开 | Footer 统计 |

> **读保持公开**：`GET /api/site/social-links` 是公开首屏要用的（本站 store 在启动时
> 调用它以渲染可见的社交图标），因此**不能**加鉴权。
> 三个写接口则仅 Admin。

### 9.1 `GET /api/site/config`

`SiteConfigDto`：

| 字段 | 类型 | 说明 |
|---|---|---|
| `siteName` | string | 站点名 |
| `logoName` | string | Logo 圆点里的文字（缺省 `"k"`），与 `siteName` **分开配置** |
| `siteLogo` | string? | 自定义 Logo 图片地址；`null` 表示未设置，前端回退到「渐变圆点 + `logoName`」 |
| `heroSubtitles` | string[] | 打字机文案列表 |
| `heroBackgrounds` | string[] | 首屏背景图列表（多张，前端每次随机展示一张）；空数组表示用内置渐变 |
| `foundingDate` | string? | 建站日期 |
| `versions` | `Record<string, number>` | **各配置项当前的版本号**（`Key -> Version`） |

> `versions` 里**缺失的 Key** 表示该配置项尚未创建，保存时版本号传 `0`。
> `versions` 的 Key **保留原始大小写**（`"SiteName"` 而不是 `"siteName"`），前端按常量精确取值。

> ⚠️ **`heroBackground`（单数，string）已在 2026-09-13 改为 `heroBackgrounds`（复数，string[]）。**
> **Key 名仍叫 `HeroBackground`**（避免多一个配置行的版本号迁移），只是它的 Value 从
> 「一个裸 URL」升级成「JSON 数组」。读取侧对历史裸地址保持兼容，见 §9.2。

### 9.2 `PUT /api/site/config` — 逐项更新

请求体 `UpdateSiteConfigRequest`：

```json
{ "key": "SiteName", "value": "新站点名", "version": 1 }
```

**语义**：只更新**一个** Key，不是整体替换。

| Key | 值格式 | 服务端校验 |
|---|---|---|
| `SiteName` | 字符串 | — |
| `LogoName` | 字符串（建议 1~2 字符） | — |
| `SiteLogo` | `/api/files/...` 相对地址，或空串 | **媒体白名单**，见下 |
| `HeroSubtitles` | **JSON 数组字符串**，如 `["a","b"]` | — |
| `FoundingDate` | `yyyy-MM-dd` | — |
| `HeroBackground` | **JSON 数组字符串**，如 `["/api/files/a.webp"]` | **媒体白名单**，逐项校验 + 最多 20 张 |

**媒体白名单校验**（`SiteLogo` / `HeroBackground`，与作者头像、文章封面同一条规则）：

- 合法取值只有两种：**空**（表示未设置）、或以 `/api/files/` 开头的本站路径
- 拒绝：`https://…`、`//…`、`javascript:`、`data:`、`..`、`//`、`%`、`?`、`#`、`\`、目录结尾等
- 失败一律返回 **HTTP 400 / code 4001**，提示形如
  `站点 Logo：只允许使用本站上传的图片（地址须以 /api/files/ 开头），不支持填写外部链接`
- `HeroBackground` 的数组**任意一项不合法就整批拒绝**；JSON 格式损坏也报 4001（**不会**静默存成空数组）

写入语义遵循 §2.3 的统一规则：Key 不存在 = 新增（忽略 version），
已存在 = 乐观锁（`version` 必须 ≥ 1，否则 4001）。

响应 `data`：更新后的完整 `SiteConfigDto`

### 9.3 `GET /api/site/social-links`

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `includeHidden` | bool | `false` | 管理端配置页传 `true` 以同时返回隐藏项 |

`SocialLinkDto`：`{ id, name, icon, url, sortOrder, isVisible, version }`

> **`includeHidden` 不校验权限**——匿名也能传 `true` 拿到隐藏项。
> 这是**刻意的**：该端点是公开首屏要用的（公开读，见 §9 顶部说明），
> 而隐藏项只是前端不展示、并非敏感数据。**写接口才是安全边界**，均已鉴权（见 §9.4）。

### 9.4 写接口鉴权状态（已全部落实）

**所有写接口（POST / PUT / DELETE）都已带上正确的鉴权策略**，
匿名调用一律返回 **401 + code 4010**。

| 控制器 | 写接口策略 | 说明 |
|---|---|---|
| `PostsController` | `ContentWriter` | 增删改 + 发布；另在 Service 层做**归属校验** |
| `AuthorsController` | POST/DELETE `AdminOnly`；PUT `ContentWriter` | PUT 另按 `AuthorId` 限制"作者只能改自己" |
| `CategoriesController` | `AdminOnly` | 创建/更新/删除分类 |
| `TagsController` | `AdminOnly` | 创建/更新/删除标签 |
| `CollectionsController` | `AdminOnly` | 增删改 + 文章编排 |
| `UsersController` | `AdminOnly` | 类级标注，整个控制器仅管理员 |
| `SiteController` | `AdminOnly` | 配置更新、社交链接保存/删除 |
| `FilesController` | `ContentWriter`（仅上传） | **读取保持公开**（文章封面/头像需匿名可见） |
| `AuthController` | 登录 `AllowAnonymous`；`me`/`logout` 需登录 | 登录入口不承担权限边界 |

**读接口（GET）保持公开**——公开站点必须能匿名浏览，
唯一例外是 `/api/posts/{id}/readonly`（编辑器用）与 `/api/collections/id/{id}`（管理端用），
它们分别要求 `ContentWriter` 与 `AdminOnly`。

> ⚠️ **改这个控制器时请守住一条原则**：前端隐藏按钮**不是安全边界**，
> 新加的写接口必须自己带 `[Authorize]`。
> 这一点有过教训——上述策略曾经遗漏过，历史记录见 [10-决策记录.md](./10-决策记录.md) §5。

### 9.5 `PUT /api/site/social-links` — 批量保存

请求体：`UpsertSocialLinkRequest[]`

```json
[
  { "id": null, "name": "GitHub", "icon": "github", "url": "https://...", "sortOrder": 0, "isVisible": true, "version": null }
]
```

| 字段 | 说明 |
|---|---|
| `id` | `null` = 新增 |
| `version` | 新增时传 `null`；更新时必须携带当前版本 |

响应 `data`：保存后的完整 `SocialLinkDto[]`

### 9.6 `DELETE /api/site/social-links/{id}`

查询参数：`version`（必填）。软删除。

### 9.7 `GET /api/site/stats`

`SiteStatsDto`：

| 字段 | 类型 | 说明 |
|---|---|---|
| `siteDays` | int | 建站天数（由 `FoundingDate` 计算） |
| `totalPosts` | int | 文章总数 |
| `totalWords` | long | 总字数 |
| `totalViews` | int | 总浏览量 |
| `tagCount` | int | 标签数 |
| `categoryCount` | int | 分类数 |

---

## 10. 账号 `/api/users`

`UsersController` — **整个控制器仅管理员可访问**（类级 `[Authorize(Policy = "AdminOnly")]`）

> 这是**作者账号的唯一创建入口**（决策 T1：作者不开放自助注册）。
> 响应 DTO 不含任何凭据字段。

| # | 方法 | 路径 | 权限 | 说明 |
|---|---|---|---|---|
| 40 | GET | `/api/users` | 👑 AdminOnly | 账号列表 |
| 41 | GET | `/api/users/{id}` | 👑 AdminOnly | 单个账号 |
| 42 | POST | `/api/users` | 👑 AdminOnly | 创建账号 |
| 43 | PUT | `/api/users/{id}` | 👑 AdminOnly | 更新角色/关联作者/启用状态 |
| 44 | POST | `/api/users/{id}/reset-password` | 👑 AdminOnly | 重置密码 |
| 45 | POST | `/api/users/{id}/disable` | 👑 AdminOnly | 停用账号 |

`UserDto`：

| 字段 | 类型 |
|---|---|
| `id` | guid |
| `email` | string |
| `role` | string |
| `isActive` | bool |
| `authorId` | guid? |
| `authorName` | string? |
| `lastLoginAt` | string? |
| `createdAt` | string |
| `version` | int |

### 10.1 `POST /api/users` — 创建账号

请求体 `CreateUserRequest`：

```json
{ "email": "author@example.com", "password": "...", "role": "Author", "authorId": "guid" }
```

| 字段 | 说明 |
|---|---|
| `role` | `Admin` 或 `Author` |
| `authorId` | `role=Author` 时应指定关联的署名作者，即"给某位作者开通登录" |

**错误**：4003（邮箱已被占用）

### 10.2 `PUT /api/users/{id}` — 更新账号

请求体 `UpdateUserRequest`：`{ role, authorId, isActive, version }`

**不修改凭据**（密码走 §10.3）。

### 10.3 `POST /api/users/{id}/reset-password` — 重置密码

请求体 `ResetPasswordRequest`：`{ newPassword, version }`

**行为**：改密码并把 `TokenVersion + 1`，因此**该账号所有旧 token 立即失效**
（已登录的设备会被踢下线）。

### 10.4 `POST /api/users/{id}/disable` — 停用账号

查询参数：`version`（必填）

**行为**：设 `IsActive = false`，同时 `TokenVersion + 1`（立即踢下线）。
**不物理删除**，保留审计线索。

---

## 11. 文件 `/api/files`

`FilesController`

| # | 方法 | 路径 | 权限 | 说明 |
|---|---|---|---|---|
| 46 | POST | `/api/files/upload` | ✍️ ContentWriter | 上传文件，返回可访问 URL |
| 47 | GET | `/api/files/{**path}` | 🌐 公开 | 读取文件 |

> **上传需要 `ContentWriter`**（Admin 或 Author）：上传是写操作，
> 匿名开放会让任何人都能往服务器塞文件。**读取保持公开**——
> 文章封面与作者头像必须能被匿名访客加载。

### 11.1 `POST /api/files/upload`

- Content-Type：`multipart/form-data`，字段名 **`file`**
- 框架层上限：`[RequestSizeLimit(50MB)]`
- 业务层上限：`FileStorage:MaxFileSize`（默认 10MB）

**错误响应**（2026-09-13 起，全部用 **HTTP 状态码**表达，不再返回 200 + 错误码）：

| 场景 | HTTP | `code` | `message` |
|---|---|---|---|
| 文件为空 | **400** | `4001` | 文件不能为空 |
| 超过业务上限（默认 10MB） | **413** | `4130` | 文件大小超过限制（10MB） |
| 扩展名不在白名单 | **400** | `4001` | 不支持的文件类型：`.svg` |
| 匿名 / 角色不足 | **401 / 403** | `4010` / `4030` | — |

> ⚠️ **改造前这里返回的是 HTTP 200**：`FilesController.Upload` 直接
> `return ApiResponse.Fail(...)`，只有 body 里的 `code` 是错误码 ——
> 于是「参数不合法」这一个语义在上传接口上是 200、在其它接口上是 400。
> 现在统一抛 `BusinessException`，由全局中间件映射，与其它接口完全一致。
> 详见 [14-开发问题记录](./14-开发问题记录.md) 第 6 条。

> 💡 **上游还有一道 nginx 限制**：`client_max_body_size 12m`。
> 所以经 nginx 访问时，**超过 12MB 的请求到不了应用**，会由 nginx 直接返回
> 自己的 413 页面（**不是**统一响应体）。12MB 这个值刻意比业务上限 10MB 大一点，
> 让业务上限先起作用、由应用给出可读提示。见 [09](./09-已知限制与技术债.md) §5.8 第 4 条。

**允许的扩展名**（`LocalFileStorageService.AllowedExtensions`）：

| 类别 | 扩展名 |
|---|---|
| 图片 | `.png` `.jpg` `.jpeg` `.gif` `.webp` `.ico` |
| 其他 | `.mp4` `.webm` `.pdf` `.zip` |

> ⚠️ **`.svg` 被刻意排除**（2026-09-13，缺口 G11）。SVG 可以内嵌 `<script>`，
> 而本站文件接口是**同源内联**下发的 —— 直接打开 `/api/files/xxx.svg`
> 会让脚本在站点源上执行，构成**存储型 XSS**。
> 上传 SVG 返回 **400 + 4001 +「不支持的文件类型：.svg」**。
> 完整论证见 [03-后端设计.md](./03-后端设计.md) §5.8、[09](./09-已知限制与技术债.md) §5.10。
>
> 前端对应的 `accept` 也只列了 png/jpeg/webp/gif（`src/utils/media.ts` 的 `ACCEPT_IMAGE`），
> 但**那只是选择器过滤**，真正的拦截在服务端。

响应 `data`：

```json
{ "url": "/api/files/2026/09/xxx.webp", "storedBytes": 12345, "originalBytes": 45678, "converted": true }
```

| 字段 | 说明 |
|---|---|
| `url` | 可直接用于 `coverImage` / `avatar` 的相对 URL |
| `storedBytes` | 实际存储字节数 |
| `originalBytes` | 原始字节数 |
| `converted` | 是否发生了图片格式转换（转 WebP） |

**错误**：4001（文件为空 / 超限 / 不支持的格式）

### 11.2 `GET /api/files/{**path}` — 读取

- 命中时下发 `Cache-Control: public,max-age=86400`
- 未命中返回 **404** + 统一响应体，**且不下发缓存头**

> ⚠️ 历史 Bug：此前用 `[ResponseCache]` 标注 action，导致"文件不存在"的 404 也被
> 缓存一整天，文件补回来后用户仍看到空白。现在改为**确认命中后**才设置响应头。

**路径穿越防护**：`LocalFileStorageService` 校验解析后的绝对路径仍在存储根目录内。

---

## 12. 端点统计

| 控制器 | 端点数 |
|---|---|
| AuthController | 3 |
| PostsController | 9 |
| AuthorsController | 6 |
| CategoriesController | 4 |
| TagsController | 4 |
| CollectionsController | 7 |
| SiteController | 6 |
| UsersController | 6 |
| FilesController | 2 |
| **合计** | **47** |

另有：

| 端点 | 说明 |
|---|---|
| `GET /` | 存活探针，返回 `{ name: "Blog API", status: "running" }`（只证明进程在，**不检查依赖**） |
| `GET /health` | 健康检查，**对外只返回 `Healthy` / `Unhealthy` 纯文本**（200 / 503），详情只写日志 |

`/health` 的检查项：PostgreSQL 连通性、`zhparser` 扩展与 `chinese` 检索配置是否真的可用
（会实际跑一次 `to_tsvector('chinese', ...)`）、迁移是否全部应用、Redis 连通性
（仅当 `Cache:Provider = Redis` 时视为必需）。
实现见 `Blog.WebApi/HealthChecks/HealthCheckSetup.cs`，设计说明见
[03-后端设计.md](./03-后端设计.md) §11。

---

## 13. 前端调用入口对照

| 前端文件 | 对应后端控制器 |
|---|---|
| `src/api/auth.ts` | Auth |
| `src/api/posts.ts` | Posts |
| `src/api/authors.ts` | Authors |
| `src/api/categories.ts` | Categories |
| `src/api/tags.ts` | Tags |
| `src/api/collections.ts` | Collections |
| `src/api/site.ts` | Site |
| `src/api/users.ts` | Users |
| `src/api/files.ts` | Files |
| `src/api/http.ts` | 统一封装（含错误码常量） |

---

## 14. 与代码的对应关系

| 内容 | 文件 |
|---|---|
| 全部端点定义 | `Blog.Backend/Blog.WebApi/Controllers/*.cs` |
| 请求/响应 DTO | `Blog.Backend/Blog.Application/Services/*/*Dtos.cs` |
| 错误码 | `Blog.Backend/Blog.Application/Common/ErrorCodes.cs` |
| 授权策略 | `Blog.Backend/Blog.WebApi/Program.cs` |
| 前端类型对齐 | `Blog.FrontEnd/src/types/index.ts` |
| 前端错误码常量 | `Blog.FrontEnd/src/api/http.ts` |
