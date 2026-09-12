# syntax=docker/dockerfile:1
#
# 前端 + 网关镜像：用 Nginx 同时提供前端静态文件与 /api 反向代理。
#
# 构建上下文必须是**仓库根目录**：
#   docker build -f deploy/nginx.Dockerfile -t blog-nginx:local .
#
# 为什么不把前端塞进后端镜像：
#   ① 前端构建要 Node 工具链，后端要 .NET SDK，混在一起镜像更大、缓存更容易失效
#   ② 职责清晰：静态文件交给 Nginx（更快、能长缓存），API 交给 Kestrel
#   ③ 前端重新构建不必重启后端

# ────────────────────────────────────────────────────────────────
# 阶段 1：构建前端（Vite）
# ────────────────────────────────────────────────────────────────
#
# 用 bookworm-slim（glibc）而不是 alpine（musl）：
#   本项目依赖里有带**原生二进制**的包（rolldown、lightningcss），
#   npm 按平台安装对应实现。package-lock.json 里 glibc 与 musl 变体都记录了，
#   但 glibc 是最稳妥的默认选择，与开发机（Windows）行为差异最小。
FROM node:24-bookworm-slim AS build
WORKDIR /src

# 与后端 Dockerfile 同理：先复制依赖清单再 npm ci，让依赖层可复用缓存。
# package*.json 这个通配同时匹配 package.json 与 package-lock.json。
COPY Blog.FrontEnd/package.json Blog.FrontEnd/package-lock.json ./

# `npm ci` 而不是 `npm install`：
#   - 严格按 lock 文件安装，构建可复现
#   - lock 与 package.json 不一致时**直接失败**，而不是悄悄改 lock
#   - 不会修改 lock 文件
# 注意不能加 --omit=dev：vue-tsc / vite 都在 devDependencies 里，构建需要它们。
RUN npm ci

COPY Blog.FrontEnd/ ./

# 等同 `vue-tsc -b && vite build`（见 package.json 的 build 脚本）
# 也就是说镜像构建**顺带做了类型检查** —— 类型错误会让镜像构建直接失败。
RUN npm run build

# ────────────────────────────────────────────────────────────────
# 阶段 2：Nginx
# ────────────────────────────────────────────────────────────────
FROM nginx:1.29-alpine AS runtime

# 官方 nginx 镜像默认会加载 /etc/nginx/conf.d/*.conf。
# 覆盖掉它自带的 default.conf，换成我们的站点配置。
RUN rm -f /etc/nginx/conf.d/default.conf
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf

# 只把 dist/ 搬过来：node_modules 与源码都不进最终镜像
COPY --from=build /src/dist /usr/share/nginx/html

EXPOSE 80

# ⚠️ 健康检查必须写 127.0.0.1，**不能写 localhost**。
#    Alpine 里 localhost 会优先解析成 IPv6 的 [::1]，
#    而 nginx 默认的 `listen 80;` 只绑定 IPv4 —— 结果是
#    "wget: can't connect to remote host: Connection refused"，
#    容器一直显示 unhealthy，但**从外部访问其实完全正常**。
#    （本项目实际踩到：表现为 docker compose ps 里 nginx 永远 unhealthy）
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD wget -qO- http://127.0.0.1/ >/dev/null 2>&1 || exit 1
