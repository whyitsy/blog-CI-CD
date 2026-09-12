# syntax=docker/dockerfile:1
#
# 后端镜像：ASP.NET Core (Blog.WebApi)
#
# 构建上下文必须是**仓库根目录**（因为要 COPY Blog.Backend/ 下的多个项目）：
#   docker build -f deploy/webapi.Dockerfile -t blog-webapi:local .
#
# 为什么要「多阶段」：
#   SDK 镜像约 1GB（含编译器、NuGet、MSBuild），运行只需要 ASP.NET Core 运行时（约 220MB）。
#   多阶段让最终镜像**不含编译器和源码**，体积小、攻击面也小。

# ────────────────────────────────────────────────────────────────
# 阶段 1：构建（builder）
# ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 【关键设计】先只复制 .csproj，再 restore，最后才复制源码。
#
# Docker 镜像是一层层构建的，某一层的输入没变就直接复用缓存。
# 源码几乎每次提交都变，但 .csproj 很少变。
# 如果把 `COPY . .` 放在 restore 之前，那么**每次改一行 C# 都要重新下载全部 NuGet 包**。
# 现在的顺序让「依赖还原」这一层在依赖清单没变时命中缓存。
COPY Blog.Backend/Blog.Domain/Blog.Domain.csproj                 Blog.Domain/
COPY Blog.Backend/Blog.Application/Blog.Application.csproj       Blog.Application/
COPY Blog.Backend/Blog.Infrastructure/Blog.Infrastructure.csproj Blog.Infrastructure/
COPY Blog.Backend/Blog.WebApi/Blog.WebApi.csproj                 Blog.WebApi/

# 只还原 WebApi 及其依赖链（Domain/Application/Infrastructure）。
# 不还原 Blog.Tests：镜像里不需要测试工程。
RUN dotnet restore Blog.WebApi/Blog.WebApi.csproj

# 依赖层建好之后，再复制全部源码
COPY Blog.Backend/ ./

# --no-restore：复用上面那一层的结果，避免重复还原
# -o /app/publish：发布产物集中输出，方便下一阶段整目录复制
RUN dotnet publish Blog.WebApi/Blog.WebApi.csproj \
      --configuration Release \
      --no-restore \
      --output /app/publish

# ────────────────────────────────────────────────────────────────
# 阶段 2：运行（runtime）
# ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# ASPNETCORE_ENVIRONMENT=Production 有两个作用：
#   1. 加载 appsettings.Production.json（而不是 Development）
#   2. Program.cs 里 `if (IsDevelopment()) app.MapOpenApi()` 不会执行 —— 生产不暴露 OpenAPI
# ASPNETCORE_HTTP_PORTS=8080：容器内监听端口。用非 443/80 端口是为了
#   让**无 root 用户也能绑定**（Linux 下 <1024 端口需要特权）。
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080

WORKDIR /app

# curl 只用于 HEALTHCHECK。
# 官方 aspnet 镜像**不含 curl**，而健康检查又必须真的发一个 HTTP 请求
# （只看端口通不通无法发现「进程活着但数据库连不上」）。
# 代价约 1MB，换来的是编排系统能正确判断容器是否可用。
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

# 只复制发布产物：没有源码、没有 SDK、没有 obj/bin 中间文件
COPY --from=build /app/publish ./

# 准备运行期目录并交给非 root 用户：
#   /app/media       上传文件（会被 compose 挂成数据卷持久化）
#   /app/logs        Serilog 按天滚动的日志（Program.cs 里是相对路径 "logs"）
#   /app/media-seed  只读种子资源，由 csproj 的 Content 项复制进发布产物
#
# .NET 8+ 官方镜像内置了非 root 用户 app（UID 1654）。
# **不要用 root 跑应用**：一旦被拿下，攻击者在容器内就是 root。
# 注意 chown 必须在 USER app **之前**执行。
RUN mkdir -p /app/media /app/logs \
 && chown -R app:app /app

USER app

EXPOSE 8080

# 打的是 /health（不只探端口）—— 它会真实校验数据库连通性、迁移是否应用、Redis。
# start-period 给足 40s：应用启动时会**自动执行数据库迁移**，首次启动较慢。
#
# 地址写 127.0.0.1 而不是 localhost：容器里 localhost 可能优先解析到 IPv6，
# 而 IPv4/IPv6 的绑定情况取决于运行时配置。写死 IPv4 可避免一整类
# "服务正常但探针失败"的假故障（deploy/nginx.Dockerfile 里踩过同样的坑）。
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD curl -fsS http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "Blog.WebApi.dll"]
