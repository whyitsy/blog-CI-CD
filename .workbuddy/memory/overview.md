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
- Git 提交：baseline → domain → int-version 乐观锁+服务层+缓存 → webapi+serilog+限流（共 4 次）

## 待办（阻塞项）
1. **Docker Desktop 未运行**：迁移 `20260906040849_InitCreate` 已生成但 blog_stage2 库未重建，接口冒烟测试未执行 → 启动 Docker 后执行 `dotnet ef database update`
2. 前端 Vue3 未开始（页面规划见 `docs/04-前端页面路由规划.md`）

## 环境备忘
- 沙箱 Bash 缺 Windows 环境变量导致 dotnet 构建报 `ArgumentNullException path1` → 用 `/d/tmpbuild/dn.sh` 包装脚本执行 dotnet
- 数据库：Docker 容器 `pgsql`（kky/123456），本阶段使用独立库 `blog_stage2`（旧库 blog 有历史数据未动）
