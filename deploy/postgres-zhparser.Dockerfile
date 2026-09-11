# 自定义 PostgreSQL 镜像：在官方 postgres 基础上加装中文分词扩展 zhparser
#
# 为什么需要这个镜像
#   项目按 T9 决策使用 PostgreSQL 全文检索（FTS）做站内中文检索。
#   PostgreSQL 内置分词器只按空格/标点切分，对中文会把整句当成一个词元，
#   因此必须安装中文分词器 zhparser（依赖 SCWS 分词库）。
#   zhparser 不在官方镜像里，必须自行编译。
#
# 为什么不能只写文档
#   手工在容器里编译，容器一重建就全丢，全文检索会直接报
#     ERROR: text search configuration "chinese" does not exist
#   因此把编译步骤固化进镜像，做到可复现、可交接、CI 可用。
#
# 构建
#   docker build -f deploy/postgres-zhparser.Dockerfile -t blog-postgres-zhparser:18 .
#
# 详见 docs/01-项目初始化与配置.md §6

FROM postgres:18.6

# 构建依赖：
#   build-essential / postgresql-server-dev-18 —— 编译 PG 扩展
#   autoconf automake libtool pkg-config       —— SCWS 的 autotools 构建链
#   git ca-certificates                        —— 拉源码
# 注意：Debian 13 (trixie) 源里没有 libscws-dev，因此 SCWS 也必须从源码编译。
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends \
        build-essential \
        postgresql-server-dev-18 \
        autoconf \
        automake \
        libtool \
        pkg-config \
        git \
        ca-certificates \
    ; \
    rm -rf /var/lib/apt/lists/*

# ---------------------------------------------------------------- SCWS
# zhparser 依赖 SCWS 做词法切分，需先装 SCWS 到 /usr/local。
RUN set -eux; \
    git clone --depth 1 https://github.com/hightman/scws.git /tmp/scws; \
    cd /tmp/scws; \
    # 坑 1：SCWS 仓库没有 autogen.sh，实际脚本名是 acprep
    # 坑 2：acprep 会因 Makefile.am 中「Tab 缩进的 # 注释」报
    #       "'#' comment at start of rule is unportable" 而失败，
    #       必须先删掉那一行；automake --warnings=no-portability 无法抑制该错误
    sed -i '/^[[:space:]]*#unison/d' Makefile.am; \
    ln -sf README.md README; \
    ./acprep; \
    ./configure --prefix=/usr/local; \
    make -j"$(nproc)"; \
    make install; \
    ldconfig; \
    # 清掉源码与构建产物，避免镜像无谓变大
    rm -rf /tmp/scws

# ---------------------------------------------------------------- zhparser
RUN set -eux; \
    git clone --depth 1 https://github.com/amutu/zhparser.git /tmp/zhparser; \
    cd /tmp/zhparser; \
    SCWS_HOME=/usr/local make; \
    SCWS_HOME=/usr/local make install; \
    rm -rf /tmp/zhparser

# ---------------------------------------------------------------- 初始化脚本
# 容器首次初始化数据目录时，postgres 官方镜像会执行 /docker-entrypoint-initdb.d 下的脚本。
# 注意：该机制**只在数据目录为空时**生效；已有的库不会重跑，
#       因此扩展与检索配置最终应以 EF 迁移为准（见 docs/06-数据库设计.md §8.3）。
COPY deploy/postgres-init/ /docker-entrypoint-initdb.d/

# 构建期只做静态自检（不启动数据库，避免在 build 阶段引入不可靠的状态）。
# 运行时的端到端验证见 deploy/README.md 的「验证」小节。
RUN set -eux; \
    test -f "$(pg_config --pkglibdir)/zhparser.so"; \
    test -f "$(pg_config --sharedir)/extension/zhparser.control"; \
    test -f /usr/local/lib/libscws.so; \
    echo "OK: zhparser.so / zhparser.control / libscws.so 均已就位"
