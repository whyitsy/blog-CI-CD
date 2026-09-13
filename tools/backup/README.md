# 备份与恢复

> **为什么这些脚本在仓库里**：`docs/09` §6.4 的教训 ——「开发机上的文件不是项目资产」。
> 备份是**上线后唯一能救命**的东西，放在仓库外等于没有。

---

## 1. 备份什么、不备份什么

| 资产 | 怎么备份 | 为什么 |
|---|---|---|
| **数据库** | `backup.sh`（`pg_dump` + gzip） | 文章、账号、配置全在这里 |
| **上传的图片** | `media` 卷打包（见下） | 数据库里只存路径，文件在卷里；**只备数据库会得到一堆 404 的图** |
| **`.env`** | ⚠️ **手工单独保管，绝不入库** | 里面有 `JWT_SIGNING_KEY` 与数据库密码。丢了它，站点起不来；泄露了它，任何人都能伪造 token |

### 备份上传文件（media 卷）

```bash
mkdir -p backups
docker run --rm \
  -v blog_media:/data:ro \
  -v "$PWD/backups:/out" \
  alpine tar czf "/out/media_$(date +%Y%m%d-%H%M%S).tar.gz" -C /data .
```

> 卷名由 compose 项目名决定（`docker-compose.yml` 里 `name: blog`，故为 `blog_media`）。
> 用 `docker volume ls | grep media` 核对实际名字。

---

## 2. 备份

```bash
# 在仓库根目录
bash tools/backup/backup.sh              # 输出到 ./backups/
bash tools/backup/backup.sh /mnt/e/bak   # 或指定目录
```

产出：`backups/blog_blog_stage2_<时间戳>.sql.gz`

脚本有两个刻意的设计：

- **`docker compose exec -T`**：`-T` 关掉伪终端，否则 gzip 会写进 tty 拿到空文件。
- **小于 1KB 判为失败**：`docker compose exec` 失败时管道里的 gzip 仍会成功退出，
  `set -e` 不会触发 —— 脚本会"成功地"产出一个空备份。**这是最危险的一种失败**，所以显式挡掉。

---

## 3. 恢复

```bash
# 默认还原到 blog_stage2_restore，不碰正式库
bash tools/backup/restore.sh backups/blog_blog_stage2_20260914-120000.sql.gz

# 指定目标库名
bash tools/backup/restore.sh backups/xxx.sql.gz my_check_db
```

脚本会打印四行核对结果：`Posts=` / `Users=` / `zhparser 扩展=` / `chinese 检索配置=`。

> ⚠️ **最后两项不能省，而且它比看上去更危险。**
>
> 本项目的全文检索依赖 PostgreSQL 的 `zhparser` 扩展与 `chinese` 检索配置（见 `docs/06` §8）。
> 实测（2026-09-14）确认：**它们确实在 `pg_dump` 的输出里** ——
> dump 第 26 行是 `CREATE EXTENSION IF NOT EXISTS zhparser WITH SCHEMA public;`，
> 第 40 行起是 `CREATE TEXT SEARCH CONFIGURATION public.chinese (…)`。
>
> 但把同一份 dump 灌进一个**普通的 `postgres:18.6` 镜像**，会直接失败：
>
> ```
> ERROR:  extension "zhparser" is not available
> HINT:  The extension must first be installed on the system where PostgreSQL is running.
> ```
>
> 也就是说：**数据在，检索能力不在。** 还原的目标实例必须**预先装好 zhparser 的扩展文件**，
> 而它不在官方镜像里、必须自行编译 —— 这正是生产环境**必须**使用
> `deploy/postgres-zhparser.Dockerfile` 构建的镜像的原因。
>
> 📌 这条结论来自一次真实的还原演练：演练之前本文档写的是"扩展不在 dump 里"，
> **是错的**。这就是为什么"没演练过的备份等于没有备份"——
> 一个听起来合理但错误的假设，只有真跑一遍才会露出来。

---

## 4. 恢复演练（每季度一次，必须真做）

> **没演练过的备份等于没有备份。**
> 备份脚本天天成功、真出事时才发现灌不进去 —— 这个剧本太常见了。

| # | 步骤 | 期望 |
|---|---|---|
| 1 | `bash tools/backup/backup.sh` | 产出 `.sql.gz` 且 > 1KB |
| 2 | `bash tools/backup/restore.sh <刚生成的文件>` | 四行核对都打印出数字 |
| 3 | 用还原库启动一次应用 | `ConnectionStrings__DefaultConnection` 指向 `_restore` 库，`/health` 返回 200 |
| 4 | 打开一篇文章、搜一次中文关键词 | 正文与图片正常、搜索有结果 |
| 5 | 清理：`DROP DATABASE "blog_stage2_restore";` | — |
| 6 | 把本次演练的日期与结果记到 `docs/06` 的对应条目 | 有记录才算做过 |

---

## 5. 上线后的定时备份

在服务器上（容器栈所在目录）加 crontab：

```cron
# 每天 03:00 备份数据库，保留 14 天
0 3 * * * cd /opt/blog && bash tools/backup/backup.sh /var/backups/blog >> /var/log/blog-backup.log 2>&1
15 3 * * 0 cd /opt/blog && docker run --rm -v blog_media:/data:ro -v /var/backups/blog:/out alpine tar czf "/out/media_$(date +\%Y\%m\%d).tar.gz" -C /data . >> /var/log/blog-backup.log 2>&1
```

⚠️ **备份文件不要和数据库放在同一台机器上** —— 机器挂了两个一起没。
至少要定期 `scp` 一份到别处（本机 / 对象存储）。

---

## 6. 相关文档

| 内容 | 位置 |
|---|---|
| 部署与回滚 | `docs/05-运维与部署手册.md`（部署章） |
| 数据库结构与 zhparser | `docs/06-数据库设计.md`、`docs/01` §6 |
| 这条要求的来源（技术债条目） | `plan/2026-09-14-大整理与上线方案.md` §2.4 E |
