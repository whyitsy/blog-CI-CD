<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { deleteSocialLink, getSiteConfig, getSocialLinks, saveSocialLinks, updateSiteConfig } from '@/api/site'
import { uploadFile } from '@/api/files'
import FormSkeleton from '@/components/skeleton/FormSkeleton.vue'
import SocialIcon from '@/components/common/SocialIcon.vue'
import { useSiteStore } from '@/stores/site'
import { SiteConfigKey } from '@/types'
import type { SocialLinkDto } from '@/types'

const site = useSiteStore()

interface SocialRow {
  id: string | null
  name: string
  icon: string
  url: string
  sortOrder: number
  isVisible: boolean
  version: number
}

const loading = ref(true)
const errorMsg = ref('')
const basicMsg = ref('')
const linkMsg = ref('')

// ---------- 基本配置 ----------
const versions = ref<Record<string, number>>({})
const savingBasic = ref(false)
const uploadingBg = ref(false)
const uploadingLogo = ref(false)

const basic = ref({
  siteName: '',
  /** Logo 圆点里的文字，与站点名分开配置（站点名放不下，圆点只放 1~2 字符） */
  logoName: '',
  /** 自定义 Logo 图片；空字符串 = 未设置，前端回退到「渐变圆点 + logoName」 */
  siteLogo: '',
  subtitlesText: '',
  /** 首屏背景图（多张，每次随机展示一张）；为空则用内置渐变背景 */
  heroBackgrounds: [] as string[],
  foundingDate: '',
})

async function loadConfig() {
  const cfg = await getSiteConfig()
  versions.value = cfg.versions ?? {}
  basic.value = {
    siteName: cfg.siteName ?? '',
    logoName: cfg.logoName ?? '',
    siteLogo: cfg.siteLogo ?? '',
    subtitlesText: (cfg.heroSubtitles ?? []).join('\n'),
    heroBackgrounds: [...(cfg.heroBackgrounds ?? [])],
    foundingDate: cfg.foundingDate ? cfg.foundingDate.slice(0, 10) : '',
  }
}

const versionOf = (key: string) => versions.value[key] ?? 0

const subtitles = computed(() =>
  basic.value.subtitlesText
    .split('\n')
    .map((s) => s.trim())
    .filter(Boolean),
)

/** 上传站点 Logo。同头像/封面：只允许上传，不提供 URL 输入框（服务端 MediaPath 兜底） */
async function onPickLogo(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return

  if (!file.type.startsWith('image/')) {
    errorMsg.value = '请选择图片文件'
    input.value = ''
    return
  }

  uploadingLogo.value = true
  errorMsg.value = ''
  try {
    basic.value.siteLogo = await uploadFile(file)
    basicMsg.value = 'Logo 上传成功，记得点击保存'
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '上传失败'
  } finally {
    uploadingLogo.value = false
    input.value = ''
  }
}

/** 上传首屏背景图：每次追加一张，最终由前端随机挑一张展示 */
async function onPickBackground(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return

  if (!file.type.startsWith('image/')) {
    errorMsg.value = '请选择图片文件'
    input.value = ''
    return
  }

  uploadingBg.value = true
  errorMsg.value = ''
  try {
    basic.value.heroBackgrounds = [...basic.value.heroBackgrounds, await uploadFile(file)]
    basicMsg.value = '背景图上传成功，记得点击保存'
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '上传失败'
  } finally {
    uploadingBg.value = false
    input.value = ''
  }
}

function removeBackground(index: number) {
  basic.value.heroBackgrounds = basic.value.heroBackgrounds.filter((_, i) => i !== index)
}

async function saveBasic() {
  if (!basic.value.siteName.trim()) {
    errorMsg.value = '站点名称不能为空'
    return
  }

  savingBasic.value = true
  errorMsg.value = ''
  basicMsg.value = ''
  try {
    // 逐项提交、仅取最后一次响应（每次 PUT 都会返回全量配置与最新 versions），
    // 避免每项都重新 GET 一次。
    // 版本号取 GET 下发的 versions；缺失（=0）表示该 Key 尚未创建，后端按新增处理。
    await updateSiteConfig(SiteConfigKey.SiteName, basic.value.siteName.trim(), versionOf(SiteConfigKey.SiteName))
    await updateSiteConfig(SiteConfigKey.HeroSubtitles, JSON.stringify(subtitles.value), versionOf(SiteConfigKey.HeroSubtitles))

    // Logo 文字留空时回退到 "k"（与改造前写死的字符一致），避免导航栏出现空白圆点
    await updateSiteConfig(
      SiteConfigKey.LogoName,
      basic.value.logoName.trim() || 'k',
      versionOf(SiteConfigKey.LogoName),
    )
    await updateSiteConfig(SiteConfigKey.SiteLogo, basic.value.siteLogo, versionOf(SiteConfigKey.SiteLogo))

    // 建站日期存 yyyy-MM-dd，后端按 DateTimeOffset 解析（用于 Footer 建站天数）
    await updateSiteConfig(
      SiteConfigKey.FoundingDate,
      basic.value.foundingDate ? `${basic.value.foundingDate}T00:00:00+08:00` : '',
      versionOf(SiteConfigKey.FoundingDate),
    )
    // 背景图存 JSON 数组；后端会逐项做 /api/files 白名单校验，有一项不合法就整批拒绝
    // 最后一项的响应即包含全部配置项的最新版本号
    const latest = await updateSiteConfig(
      SiteConfigKey.HeroBackground,
      JSON.stringify(basic.value.heroBackgrounds),
      versionOf(SiteConfigKey.HeroBackground),
    )
    versions.value = latest.versions ?? {}
    basicMsg.value = '已保存'
    await site.refreshAll()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    savingBasic.value = false
  }
}

// ---------- 社交链接 ----------
const rows = ref<SocialRow[]>([])
const originalRows = ref<SocialRow[]>([])
const savingLinks = ref(false)

function toRow(l: SocialLinkDto): SocialRow {
  return {
    id: l.id,
    name: l.name,
    icon: l.icon,
    url: l.url,
    sortOrder: l.sortOrder,
    isVisible: l.isVisible,
    version: l.version,
  }
}

async function loadLinks() {
  const links = await getSocialLinks(true)
  rows.value = links.map(toRow)
  originalRows.value = links.map(toRow)
}

function addRow() {
  const nextOrder = rows.value.reduce((max, r) => Math.max(max, r.sortOrder), -1) + 1
  rows.value.push({ id: null, name: '', icon: '', url: '', sortOrder: nextOrder, isVisible: true, version: 0 })
}

function markRemoved(row: SocialRow) {
  rows.value = rows.value.filter((r) => r !== row)
}

async function saveLinks() {
  const invalid = rows.value.find((r) => !r.name.trim() || !r.url.trim())
  if (invalid) {
    errorMsg.value = '社交链接的名称与地址都不能为空'
    return
  }

  savingLinks.value = true
  errorMsg.value = ''
  linkMsg.value = ''
  try {
    // 先处理删除（软删除 + 乐观锁），再批量 upsert 剩余项
    const removed = originalRows.value.filter(
      (o) => o.id && !rows.value.some((r) => r.id === o.id),
    )
    for (const item of removed) {
      await deleteSocialLink(item.id as string, item.version)
    }

    await saveSocialLinks(
      rows.value.map((r, index) => ({
        id: r.id,
        name: r.name.trim(),
        icon: r.icon.trim(),
        url: r.url.trim(),
        sortOrder: index,
        isVisible: r.isVisible,
        version: r.id ? r.version : null,
      })),
    )

    await loadLinks()
    linkMsg.value = '已保存'
    await site.refreshAll()
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '保存失败'
  } finally {
    savingLinks.value = false
  }
}

// ---------- 初始化 ----------
async function load() {
  loading.value = true
  errorMsg.value = ''
  try {
    await Promise.all([loadConfig(), loadLinks()])
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <section class="admin-site">
    <p v-if="errorMsg" class="banner err">{{ errorMsg }}</p>

    <FormSkeleton v-if="loading" :fields="4" />

    <template v-else>
      <!-- 基本配置 -->
      <article class="card panel">
        <header class="panel-head">
          <h2>基本配置</h2>
          <span class="panel-hint">首屏 Hero 与 Footer 的数据来源</span>
        </header>

        <p v-if="basicMsg" class="banner ok">{{ basicMsg }}</p>

        <div class="grid-2">
          <label class="field">
            <span class="label">站点名称</span>
            <input v-model="basic.siteName" class="input" maxlength="50" placeholder="显示在导航栏 / 首屏大标题" />
          </label>

          <label class="field">
            <span class="label">Logo 文字</span>
            <input v-model="basic.logoName" class="input" maxlength="2" placeholder="k" />
            <span class="hint">与站点名称分开配置：圆点里只放得下 1~2 个字符，留空则用 "k"</span>
          </label>
        </div>

        <div class="field">
          <span class="label">Logo 图片</span>
          <div class="media-row">
            <div class="logo-preview">
              <img v-if="basic.siteLogo" :src="basic.siteLogo" alt="Logo 预览" />
              <span v-else class="logo-fallback">{{ basic.logoName || 'k' }}</span>
            </div>
            <div class="media-actions">
              <div class="media-buttons">
                <label class="btn-ghost">
                  {{ uploadingLogo ? '上传中...' : basic.siteLogo ? '更换 Logo' : '上传 Logo' }}
                  <input type="file" accept="image/*" hidden :disabled="uploadingLogo" @change="onPickLogo" />
                </label>
                <button
                  v-if="basic.siteLogo"
                  type="button"
                  class="link danger"
                  :disabled="uploadingLogo"
                  @click="basic.siteLogo = ''"
                >
                  移除
                </button>
              </div>
              <span class="hint">只能上传，不设置则显示「渐变圆点 + Logo 文字」</span>
            </div>
          </div>
        </div>

        <label class="field">
          <span class="label">首屏副标题（打字机）</span>
          <textarea
            v-model="basic.subtitlesText"
            class="input textarea"
            rows="4"
            placeholder="每行一条，循环打字播放"
          />
          <span class="hint">当前 {{ subtitles.length }} 条，每行一条</span>
        </label>

        <div class="field">
          <span class="label">首屏背景图</span>
          <div class="bg-list">
            <div v-for="(bg, index) in basic.heroBackgrounds" :key="bg" class="bg-item">
              <img :src="bg" alt="背景预览" />
              <button type="button" class="link danger" @click="removeBackground(index)">删除</button>
            </div>
            <label class="bg-add">
              {{ uploadingBg ? '上传中...' : '+ 上传' }}
              <input type="file" accept="image/*" hidden :disabled="uploadingBg" @change="onPickBackground" />
            </label>
          </div>
          <span class="hint">
            可上传多张（最多 20 张），每次打开首页随机展示一张；不设置则使用内置渐变背景。
          </span>
        </div>

        <label class="field narrow">
          <span class="label">建站日期</span>
          <input v-model="basic.foundingDate" class="input" type="date" />
          <span class="hint">用于 Footer 的「建站天数」统计</span>
        </label>

        <footer class="panel-actions">
          <button class="btn-primary" :disabled="savingBasic || uploadingBg" @click="saveBasic">
            {{ savingBasic ? '保存中...' : '保存基本配置' }}
          </button>
        </footer>
      </article>

      <!-- 社交链接 -->
      <article class="card panel">
        <header class="panel-head">
          <h2>社交链接</h2>
          <span class="panel-hint">按列表顺序展示在首屏底部，可隐藏</span>
        </header>

        <p v-if="linkMsg" class="banner ok">{{ linkMsg }}</p>

        <div v-if="rows.length" class="link-list">
          <div class="link-head">
            <span>预览</span>
            <span>名称</span>
            <span>图标 key</span>
            <span>链接地址</span>
            <span>显示</span>
            <span>操作</span>
          </div>
          <div v-for="(row, index) in rows" :key="row.id ?? `new-${index}`" class="link-row">
            <span class="icon-cell">
              <SocialIcon v-if="row.icon" :icon="row.icon" :name="row.name" />
              <span v-else class="icon-empty">?</span>
            </span>
            <input v-model="row.name" class="input" placeholder="GitHub" />
            <input v-model="row.icon" class="input" placeholder="github" />
            <input v-model="row.url" class="input" placeholder="https://..." />
            <label class="switch">
              <input v-model="row.isVisible" type="checkbox" />
              <span>{{ row.isVisible ? '显示' : '隐藏' }}</span>
            </label>
            <button class="link danger" @click="markRemoved(row)">删除</button>
          </div>
        </div>
        <p v-else class="muted-block small">还没有社交链接</p>

        <footer class="panel-actions">
          <button class="btn-ghost" @click="addRow">+ 新增一条</button>
          <button class="btn-primary" :disabled="savingLinks" @click="saveLinks">
            {{ savingLinks ? '保存中...' : '保存社交链接' }}
          </button>
        </footer>
      </article>
    </template>
  </section>
</template>

<style scoped>
.admin-site {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}

.banner {
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-sm);
  font: var(--text-body-sm);
}
.banner.err {
  background: color-mix(in srgb, #e35151 12%, transparent);
  color: #e35151;
}
.banner.ok {
  background: color-mix(in srgb, #16a34a 14%, transparent);
  color: #16a34a;
}

.muted-block {
  padding: var(--space-12) 0;
  text-align: center;
  color: var(--text-muted);
}
.muted-block.small {
  padding: var(--space-6) 0;
  font-size: 14px;
}

.panel {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding: var(--space-6);
}

.panel-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--space-4);
  flex-wrap: wrap;
}
.panel-head h2 {
  font: var(--text-h3);
  color: var(--text-strong);
  margin: 0;
}
.panel-hint {
  font: var(--text-caption);
  color: var(--text-subtle);
}

.field {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.field.narrow {
  max-width: 240px;
}
.label {
  font: var(--text-caption);
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 1px;
}

.input {
  width: 100%;
  padding: var(--space-3);
  font: var(--text-body-sm);
  color: var(--text-strong);
  background: var(--bg-canvas);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-sm);
  outline: none;
  transition: border-color var(--transition-fast);
}
.input:focus {
  border-color: var(--brand-500);
}
.textarea {
  resize: vertical;
  font-family: inherit;
}

.grid-2 {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: var(--space-4);
}

.hint {
  font: var(--text-caption);
  color: var(--text-subtle);
}

/* ---------- Logo 上传 ---------- */
.media-row {
  display: flex;
  align-items: center;
  gap: var(--space-4);
  flex-wrap: wrap;
}
.logo-preview {
  width: 56px;
  height: 56px;
  flex-shrink: 0;
  display: grid;
  place-items: center;
  overflow: hidden;
  border-radius: 50%;
  border: 1px solid var(--border-subtle);
  background: var(--bg-raised);
}
.logo-preview img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.logo-fallback {
  display: grid;
  place-items: center;
  width: 100%;
  height: 100%;
  font-size: 22px;
  font-weight: 700;
  color: #fff;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
}
.media-actions {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.media-buttons {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  flex-wrap: wrap;
}

/* ---------- 首屏背景图列表 ---------- */
.bg-list {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  flex-wrap: wrap;
}
.bg-item {
  position: relative;
  width: 160px;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-sm);
  overflow: hidden;
  background: var(--bg-raised);
}
.bg-item img {
  width: 100%;
  height: 90px;
  object-fit: cover;
}
.bg-item .link {
  display: block;
  width: 100%;
  padding: var(--space-1) 0;
  text-align: center;
  border-top: 1px solid var(--border-subtle);
}
.bg-add {
  display: grid;
  place-items: center;
  width: 160px;
  height: 90px;
  border: 1px dashed var(--border-default);
  border-radius: var(--radius-sm);
  font: var(--text-body-sm);
  color: var(--text-muted);
  cursor: pointer;
  transition: border-color var(--transition-fast), color var(--transition-fast);
}
.bg-add:hover {
  border-color: var(--brand-500);
  color: var(--brand-500);
}

/* 社交链接表 */
.link-list {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-sm);
  overflow-x: auto;
}
.link-head,
.link-row {
  display: grid;
  grid-template-columns: 52px 1fr 1fr 2fr 84px 64px;
  gap: var(--space-3);
  align-items: center;
  padding: var(--space-2) var(--space-3);
  min-width: 760px;
}
.link-head {
  background: var(--bg-raised);
  font: var(--text-caption);
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 1px;
}
.link-row {
  border-top: 1px solid var(--border-subtle);
}

.icon-cell {
  display: grid;
  place-items: center;
  width: 34px;
  height: 34px;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-sm);
  color: var(--text-muted);
}
.icon-empty {
  font-size: 12px;
}

.switch {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  font: var(--text-caption);
  color: var(--text-muted);
  cursor: pointer;
}

.panel-actions {
  display: flex;
  gap: var(--space-3);
  justify-content: flex-end;
  padding-top: var(--space-2);
}

.btn-primary,
.btn-ghost {
  padding: var(--space-2) var(--space-4);
  border-radius: var(--radius-sm);
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  transition: opacity var(--transition-fast), border-color var(--transition-fast);
}
.btn-primary {
  border: none;
  color: #fff;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
}
.btn-ghost {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--border-default);
  background: transparent;
  color: var(--text-default);
}
.btn-ghost:hover {
  border-color: var(--border-strong);
}
.btn-primary:disabled,
.btn-ghost:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.link {
  border: none;
  background: none;
  padding: 0;
  font-size: 13px;
  color: var(--brand-500);
  cursor: pointer;
}
.link.danger {
  color: #e35151;
}
</style>
