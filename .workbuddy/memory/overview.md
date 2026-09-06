# 博客后端开发总览（Stage2）

## 本轮完成内容

### 1. 乐观锁方案重构（核心变更）
按用户要求将 `[Timestamp] byte[] RowVersion` 改为 **int Version + SQL 条件手动控制**：
- `BaseEntity.Version`（int，默认 1），跨数据库（PostgreSQL/MySQL/SQLite 通用）
- 逻辑收敛在仓储层 `BaseRepository.ApplyOptimisticVersion(entity, expectedVersion)`：
  生成 `UPDATE ... SET Version=@expected+1 WHERE Id=@id AND Version=@expected`，
  版本不匹配 0 行受影响 → `DbUpdateConcurrencyException` → 全局中间件转 **409**
- 所有 DTO/Service/查询仓储从 base64 RowVersion 改为 int Version

### 2. Application 服务层
- Post / Category / Tag / Site / Author 五个服务 + 统一 `ApiResponse<T>` / `PagedResult<T>` / `BusinessException`
- 文章列表支持 categoryId / tagId / keyword（模糊搜索）组合过滤；详情自动累计浏览量（ExecuteUpdate 绕过乐观锁）
- 归档按年月分组；站点配置/社交链接/Footer 统计齐全

### 3. 缓存（Cache:Enabled 开关 + Provider: Memory/Redis）
- 穿透：空值哨兵短 TTL（2min）；击穿：key 级互斥重建（内存信号量 / Redis LockTake，抢锁失败直接回源不阻塞）；雪崩：TTL ±20% 随机抖动
- Redis 故障自动降级直查，不抛异常

### 4. WebApi 层
- 6 个控制器：Posts（列表/详情/归档/搜索/CRUD/发布）、Categories、Tags、Site、Files、Authors
- 全局异常中间件：BusinessException→400/404、并发冲突→409、其他→500
- 文件独立接口 `/api/files/**`（防目录穿越、扩展名白名单、10MB 上限）

### 5. Serilog 日志
- 控制台 + `logs/blog-.log` 按天滚动保留 30 天；请求日志含耗时；EF 慢查询拦截器（>500ms 记警告）

### 6. 令牌桶限流（Redis Lua + 内存降级）
- Lua 脚本原子化补/取令牌，多实例共享；Redis 故障按配置降级内存桶或放行
- 规则配置化（appsettings RateLimit 节）：路径前缀 + 方法 + 粒度（Ip/Global/Endpoint）+ 容量/速率
- 触发返回 429 + `Retry-After` 头 + 统一响应体（code=4091）

## 构建与提交
- `dotnet build`：**0 警告 0 错误**
- Git 共 6 次提交，最新：`35dc41d chore: ignore runtime uploaded media files`

## 冒烟测试（2026-09-06 13:45 全部通过）
数据库迁移已应用，20+ 项接口验证全过：CRUD、乐观锁 409（正确版本200 version+1 / 过期版本4090）、
模糊搜索、归档分组、分页、分类标签文章数、站点统计、文件上传/下载、目录穿越 404、限流 429、400/404 状态码。
测试中修复 5 个 bug：限流 DI 未注册、EF 投影后排序、PostTag 重复插入（改增量同步）、404 状态映射、HostAbortedException 误报。

## 待办
1. 前端 Vue3 未开始（页面规划见 `docs/04-前端页面路由规划.md`）
2. （可选）生产环境配置：Redis Provider 切换、限流规则调优

## 环境备忘
- 沙箱 Bash 缺 Windows 环境变量导致 dotnet 构建报 `ArgumentNullException path1` → 用 `/d/tmpbuild/dn.sh` 包装脚本执行 dotnet
- 数据库：Docker 容器 `pgsql`（kky/123456），本阶段使用独立库 `blog_stage2`（旧库 blog 有历史数据未动）
- 接口验证只能用沙箱 Git Bash curl（`--noproxy localhost,127.0.0.1`）；PowerShell 的 localhost 请求会被 mock 层拦截返回假数据
- 重启应用前用 `Get-Process -Name "Blog.WebApi"` 杀进程（apphost 进程名不是 dotnet）
