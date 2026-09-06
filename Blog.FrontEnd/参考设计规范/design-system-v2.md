# Hexo Aurora 博客设计系统 v2 — 极光霓虹风格

> 适用于 Benny's Blog（Hexo Aurora 主题）前端 UI。暗色为默认主题，亮色为自动/手动切换主题。

---

## 1. 设计原则

- **极光霓虹 Aurora Neon**：保留炫酷的渐变、径向光晕与发光效果，避免过度收敛。
- **暗色优先**：默认模式为深色背景，亮色模式通过 `data-theme="light"` / `prefers-color-scheme: light` 切换。
- **玻璃拟态 Glassmorphism**：导航、卡片使用半透明底色 + 背景模糊 + 微边框。
- **双栏内容**：文章详情页采用「正文 720px + 侧栏 300px」布局，归档页单列列表。
- **可开发落地**：所有颜色、间距、圆角均已沉淀为 Design Token，输出 CSS / SCSS。

---

## 2. 色彩系统

### 2.1 背景色

| Token | Dark | Light | 用途 |
|---|---|---|---|
| `bg-canvas` | `#13141A` | `#F1F3F9` | 页面最底层背景 |
| `bg-surface` | `#1B1D24` | `#FFFFFF` | 卡片、面板背景 |
| `bg-raised` | `#23262F` | `#FFFFFF` | 抬起的按钮/标签 |
| `bg-sunken` | `#0E0F13` | `#E9EDF5` | 下沉区域、代码块 |
| `bg-invert` | `#171A20` | `#F2F5F9` | 反色背景 |

### 2.2 文字色

| Token | Dark | Light | 用途 |
|---|---|---|---|
| `text-strong` | `#F2F5F9` | `#171A20` | 标题、强强调 |
| `text-default` | `#CBDAE5` | `#2E333D` | 正文、主内容 |
| `text-muted` | `#A3A9B8` | `#535B6D` | 次要信息 |
| `text-subtle` | `#8A90A0` | `#6A7284` | 辅助、占位符 |
| `text-disabled` | `#565B68` | `#A2AABE` | 禁用态文字 |
| `text-invert` | `#13141A` | `#FFFFFF` | 渐变按钮上的文字 |

### 2.3 品牌与强调色

#### Dark 模式
- **brand-500**: `#22C5DC`（青色）
- **sub-500**: `#F4569D`（品红）
- **accent-500**: `#7C5CFF`（紫色）

#### Light 模式
- **brand-500**: `#E93796`（品红）
- **sub-500**: `#547CE7`（蓝色）
- **accent-500**: `#5433FF`（深紫）

### 2.4 极光渐变

- **gradient-start**: `#24C6DC`（青）
- **gradient-mid**: `#5433FF`（紫）
- **gradient-end**: `#FF0099`（品红）

用于：Hero 大标题、Logo 光晕、活跃状态、分隔线、徽章、按钮。

### 2.5 边框与状态色

| Token | Dark | Light |
|---|---|---|
| `border-subtle` | `#2A2C36` | `#E6EAF3` |
| `border-default` | `#383B47` | `#D3D9E8` |
| `border-strong` | `#4A4E5C` | `#B9C1D4` |
| `status-success` | `#3DD68C` / `#0F9D58` | 成功 |
| `status-warning` | `#F5B544` / `#D9800A` | 警告 |
| `status-danger` | `#F87171` / `#DC2626` | 危险 |
| `status-info` | `#60A5FA` / `#4166DB` | 信息 |

---

## 3. 字体系统

- **西文主字体**: `Rubik`（400/500/600/700）
- **中文主字体**: `Noto Sans SC`（400/500/700）
- **代码字体**: `Noto Sans Mono`（400/600）
- **兜底**: `system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif`

### 字号层级

| 名称 | 字号 | 行高 | 字重 | 用途 |
|---|---|---|---|---|
| Display | 56px | 1.15 | 700 | 首页 Hero 标题 |
| H1 | 46px | 1.2 | 700 | 文章详情标题 |
| H2 | 28px | 1.3 | 700 | 章节标题 |
| H3 | 22px | 1.35 | 600 | 小标题 |
| Body | 16px | 1.75 | 400 | 正文 |
| Body-sm | 14px | 1.6 | 400 | 辅助说明 |
| Caption | 12px | 1.5 | 500 | 标签、时间 |

---

## 4. 间距系统

以 4px 为基准单位：

`space-1=4`, `space-2=8`, `space-3=12`, `space-4=16`, `space-5=20`, `space-6=24`, `space-8=32`, `space-10=40`, `space-12=48`, `space-16=64`, `space-24=96`。

### 常用布局尺寸

- 正文最大宽度：`size-prose = 720px`
- 内容容器：`size-container = 1120px`
- 宽屏容器：`size-container-wide = 1280px`
- 侧栏：`size-sidebar = 300px`

---

## 5. 圆角系统

| Token | 值 | 用途 |
|---|---|---|
| `radius-xs` | 4px | 小标签、输入框 |
| `radius-sm` | 6px | 按钮、徽章 |
| `radius-md` | 10px | 小卡片 |
| `radius-lg` | 16px | 文章卡片 |
| `radius-xl` | 24px | 大面板、评论卡片 |
| `radius-2xl` | 32px | Hero 搜索框、全圆角胶囊 |

---

## 6. 基础组件规范

### 6.1 按钮

- **Primary（渐变）**: `linear-gradient(90deg, gradient-start, gradient-mid, gradient-end)`，文字 `text-invert`，圆角 `radius-sm`，hover 轻微上移 + 发光阴影。
- **Secondary（描边）**: 透明/半透明底 + `border-default`，文字 `text-default`。
- **Ghost（幽灵）**: 透明底，hover 时背景 `bg-raised`。

### 6.2 卡片

- 背景：`bg-surface`
- 边框：1px `border-subtle`
- 圆角：`radius-lg` / `radius-xl`
- 阴影：根据层级使用 `shadow-sm` / `shadow-md` / 发光阴影

### 6.3 输入框 / 评论框

- 背景：`bg-surface`
- 边框：1px `border-default`
- 占位符：`text-subtle`
- Focus：`border-brand-500` + 外发光 `0 0 0 3px rgba(brand-500, 0.15)`

### 6.4 标签 / 徽章

- 默认：半透明背景 + 品牌色描边
- 活跃：渐变背景 + 深色文字
- 小型标签用于归档列表

### 6.5 导航

- 玻璃拟态顶栏：半透明背景（Dark `bg-surface` 82% 透明度，Light `#FFFFFF` 82% 透明度）+ `backdrop-blur(12px)`
- Logo：渐变圆形 + 紫色发光阴影
- 操作按钮：胶囊描边样式

---

## 7. 光影与特效

### 7.1 发光阴影

```css
--glow-cyan: 0 0 20px rgba(36, 198, 220, 0.45);
--glow-purple: 0 0 20px rgba(124, 92, 255, 0.45);
--glow-pink: 0 0 20px rgba(255, 0, 153, 0.45);
```

### 7.2 径向光斑

Hero/封面背景叠加 2-3 层 `GRADIENT_RADIAL`：
- 青色光斑 alpha 0.16（Light）/ 0.36（Dark）
- 品红光斑 alpha 0.14（Light）/ 0.30（Dark）

### 7.3 玻璃拟态

```css
.glass {
  background: rgba(var(--bg-surface-rgb), 0.82);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border-bottom: 1px solid var(--border-subtle);
}
```

---

## 8. 无障碍（WCAG AA）

- 正文对比度：Dark `#CBDAE5` on `#13141A` ≈ 12.8:1；Light `#2E333D` on `#FFFFFF` ≈ 11.2:1，均远超 4.5:1。
- 渐变文字仅用于装饰性大标题，不承载关键操作说明。
- 所有交互元素尺寸 ≥ 44×44px；按钮最小高度 36px。
- Focus 状态使用 2px 品牌色 outline + 2px offset。
- 尊重 `prefers-reduced-motion`，发光/位移动画可降级为静态。

---

## 9. 已交付页面

| 画板 | 名称 | 尺寸 |
|---|---|---|
| `3:55` | 01 首页 · 暗色 | 1440×2020 |
| `3:56` | 02 首页 · 亮色 | 1440×2020 |
| `3:57` | 03 文章详情 · 暗色 | 1440×2700 |
| `3:58` | 04 文章详情 · 亮色 | 1440×2880 |
| `3:59` | 05 归档与标签 · 暗色 | 1440×1230 |
| `3:60` | 06 归档与标签 · 亮色 | 1440×1230 |

---

## 10. 配套文件

- `tokens-v2.css`：CSS 自定义属性（Dark 默认，Light 通过 `data-theme="light"` 覆盖）
- `_tokens-v2.scss`：SCSS 变量与 Map
