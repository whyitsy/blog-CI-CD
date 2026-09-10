# 待确认问题清单

> 版本：v3.0 ｜ 编写日期：2026-09-10
> 配套文档：[business.md](./business.md) ｜ [backend.md](./backend.md) ｜ [frontend.md](./frontend.md) ｜ [tech.md](./tech.md)
>
> 本文只列**无法从代码单方面确定**的问题。
> `[已决定]` **T1–T10、Q1–Q14、E1–E16 全部已确认**，决议记录见 [tech.md](./tech.md) §八 与 §十，本文不再重复。

---

## 1. 当前唯一阻塞项

| # | 问题 | 状态 | 详见 |
|---|---|---|---|
| **T11** | **如何把 `zhparser` 固化进部署环境？** | `[待确认]` | [tech.md](./tech.md) §5.3.1 / §9.1 |

### 为什么这是阻塞项

按 T9 决定，站内搜索采用 **`zhparser` 中文全文检索**。但 `zhparser` **不在官方 PostgreSQL 镜像中**，
我已在本机 `pgsql` 容器内手工编译安装了 SCWS + zhparser 并验证可用：

```sql
SELECT to_tsvector('chinese', '使用 EF Core 做数据库优化与全文检索');
-- 'core':3 'ef':2 '优化':6 '使用':1 '做':4 '全文检索':7 '数据库':5
```

**问题在于：这些改动只存在于当前运行的容器里。**

一旦发生以下任一情况，`zhparser` 会**全部丢失**，全文检索立刻失效：

- 容器被 `docker rm` / 重建
- 换一台开发机
- 部署到测试/生产环境
- CI 起一个干净的 PostgreSQL 跑集成测试

失效时的报错是：

```
ERROR:  text search configuration "chinese" does not exist
```

即**生产环境上搜索功能是个定时炸弹**。

### 需要你决定

| 选项 | 说明 | 评价 |
|---|---|---|
| **A. 编写自定义 `Dockerfile`**（基于 `postgres:18` 编译 SCWS + zhparser） | 把编译步骤固化为镜像；`CREATE EXTENSION` 与 `CREATE TEXT SEARCH CONFIGURATION` 纳入 EF 迁移 | ✅ **建议**：唯一可复现、可交接的做法 |
| B. 只用文档记录手工步骤 | 把 [tech.md](./tech.md) §9.1 的步骤写成运维手册 | ❌ 换人/换机时必然出错，且 CI 无法自动化 |
| C. 换成自带中文分词扩展的现成镜像 | 例如社区维护的 PG 镜像 | ⚠️ 引入对第三方镜像的供应链信任 |
| D. 放弃中文 FTS，退回 `pg_trgm` | 取消 T9，改回模糊匹配 | ⚠️ 与你「需要支持中文全文检索」的决定冲突，需重新确认 |

**建议选 A**。完整 Dockerfile 草稿与 6 个踩坑点已写在 [tech.md](./tech.md) §9.1，可直接落地。

**如果你希望我现在就做**，我可以：① 写 Dockerfile；② 构建带 zhparser 的镜像；
③ 用它替换当前容器；④ 跑 EF 迁移，验证 `CREATE EXTENSION` / `CREATE TEXT SEARCH CONFIGURATION`
在全新库上能自动完成；⑤ 端到端验证检索可用。

---

## 2. 非阻塞的 TODO（依赖性能测试）

以下为技术文档中标注 `TODO` 的位置，均为**原需求未给出量化标准**、不编造数字的项。
建议在 Q8 的三档缓存性能测试时一并确定。

| 位置 | 项 | 说明 |
|---|---|---|
| business §8.1 | 各页面响应时间目标（P95/P99） | 原需求无 SLA。建议完成 Redis / Memory / Null 三档压测后，以 Memory 或 Redis 档的 P95 作为基线 |
| business §8.4 | 颜色对比度是否达 WCAG AA | 未做工具化校验 |
| backend §3.8 | 生产迁移执行流程 | 建议拆为独立发布步骤（不在应用启动时自动迁移），流程待定 |
| frontend §6.8 | 键盘可达性覆盖度 | 未系统实现与验收 |
| backend §4.3 | `Deployment:InstanceCount` 的实际取值 | 单实例为 1；多实例部署时由运维声明 |

---

## 3. 已确认决策的索引（不再重复正文）

| 分组 | 编号 | 详见 |
|---|---|---|
| 部署与环境 | Q1 | [tech.md](./tech.md) §一 |
| 认证与授权 | Q2、Q3、Q7、T1、T3、T7 | [tech.md](./tech.md) §二 |
| 数据模型 | Q4、Q5、T2、T4 | [tech.md](./tech.md) §三 |
| 缓存 | Q8、T5、T8 | [tech.md](./tech.md) §四 |
| 搜索 | T9 | [tech.md](./tech.md) §五 |
| SEO | Q11、T10 | [tech.md](./tech.md) §六 |
| 提交规范 | Q14 | [tech.md](./tech.md) §七 |
| 其他 | Q6、Q9、Q10、Q12、Q13、T6、E1–E16 | [tech.md](./tech.md) §八、§十 |

### 各文档职责（避免重复查阅）

| 文档 | 职责 |
|---|---|
| [tech.md](./tech.md) | 技术决策与知识点（为什么这么定、坑在哪） |
| [business.md](./business.md) | 业务视角：角色、流程、领域模型、功能清单、P0/P1/P2 拓展 |
| [backend.md](./backend.md) | 后端实现：表、索引、缓存、接口、错误码、技术债 |
| [frontend.md](./frontend.md) | 前端实现：路由、组件、API 层、技术债 |
| 本文 | 仅记录**尚未决定**的事项 |

---

## 4. 关于历史设计稿

本仓库曾有一批 **2026-09-06 的历史设计稿**（需求分析、API 清单、后端任务拆分、前端路由规划、
架构设计文档，以及两张架构示意图 SVG）。其内容已被上述文档完整取代，
且经逐条核实存在 **16 处与实现不一致**（即 E1–E16）。

已按指示删除，使 `docs/` 只保留单一事实来源。原始需求文档 `补充具体说明.md`（仓库根目录）保留。

> **如需回滚查阅**：被删除的文件都在 git 历史中。用
> `git log --diff-filter=D --name-only` 找到删除它们的提交，再用 `git show <commit>^:<path>`
> 查看内容，或 `git checkout <commit>^ -- <path>` 取回。
