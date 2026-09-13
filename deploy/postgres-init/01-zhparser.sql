-- 初始化脚本：仅在【数据目录为空】时由 postgres 官方镜像的 entrypoint 执行。
--
-- 重要：对已存在的数据库，本脚本不会重跑。
-- 因此「扩展 + 检索配置」的权威来源是 EF 迁移（见 docs/06-数据库设计.md §8.3），
-- 本脚本只是为了让全新环境一次到位，避免开发者手工执行 SQL —— **不是必需的前置条件**。
--
-- ✅ 2026-09-13 起这一点才真正成立：此前迁移把 AddColumn<SearchVector>
--    （生成列，依赖 chinese 配置）排在了创建该配置之前，导致迁移链自己无法从零建库，
--    全新库能否跑起来完全依赖本脚本先执行。顺序已修正（缺口 G12，
--    见 docs/09-已知限制与技术债.md §5.11）。
--
-- 幂等：全部使用 IF NOT EXISTS / 条件判断，重复执行安全。

-- 1) 中文分词扩展
CREATE EXTENSION IF NOT EXISTS zhparser;

-- 2) 中文全文检索配置
--    PostgreSQL 没有 CREATE TEXT SEARCH CONFIGURATION IF NOT EXISTS，
--    因此用 DO 块判断后再建，保证幂等。
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_ts_config WHERE cfgname = 'chinese'
    ) THEN
        CREATE TEXT SEARCH CONFIGURATION chinese (PARSER = zhparser);
        -- n名词 v动词 a形容词 i成语 e叹词 l习用语 j简称 q量词
        -- 用 simple 字典：只做小写归一化，不做词干还原与停用词过滤。
        -- 中文不需要词干还原；且能让中英混合文本里的英文词原样保留。
        ALTER TEXT SEARCH CONFIGURATION chinese
            ADD MAPPING FOR n,v,a,i,e,l,j,q WITH simple;
    END IF;
END
$$;

-- 3) 自检输出（构建/启动时可在容器日志中看到）
DO $$
DECLARE
    sample text;
BEGIN
    SELECT to_tsvector('chinese', '使用 EF Core 做数据库优化与全文检索')::text INTO sample;
    RAISE NOTICE 'zhparser 自检分词结果: %', sample;
END
$$;
