<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { deleteSocialLink, getSiteConfig, getSocialLinks, saveSocialLinks, updateSiteConfig } from '@/api/site'
import { uploadFile } from '@/api/files'
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

const basic = ref({
  siteName: '',
  subtitlesText: '',
  heroBackground: '',
  foundingDate: '',
})

async function loadConfig() {
  const cfg = await getSiteConfig()
  versions.value = cfg.versions ?? {}
  basic.value = {
    siteName: cfg.siteName ?? '',
    subtitlesText: (cfg.heroSubtitles ?? []).join('\n'),
    heroBackground: cfg.heroBackground ?? '',
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
    basic.value.heroBackground = await uploadFile(file)
    basicMsg.value = '背景图上传成功，记得点击保存'
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : '上传失败'
  } finally {
    uploadingBg.value = false
    input.value = ''
  }
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

    // 建站日期存 yyyy-MM-dd，后端按 DateTimeOffset 解析（用于 Footer 建站天数）
    await updateSiteConfig(
      SiteConfigKey.FoundingDate,
      basic.value.foundingDate ? `${basic.value.foundingDate}T00:00:00+08:00` : '',
      versionOf(SiteConfigKey.FoundingDate),
    )
    // 最后一项的响应即包含全部配置项的最新版本号
    const latest = await updateSiteConfig(
      SiteConfigKey.HeroBackground,
      basic.value.heroBackground.trim(),
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

    <div v-if="loading" class="muted-block">加载中...</div>

    <template v-else>
      <!-- 基本配置 -->
      <article class="card panel">
        <header class="panel-head">
          <h2>基本配置</h2>
          <span class="panel-hint">首屏 Hero 与 Footer 的数据来源</span>
        </header>

        <p v-if="basicMsg" class="banner ok">{{ basicMsg }}</p>

        <label class="field">
          <span class="label">站点名称</span>
          <input v-model="basic.siteName" class="input" maxlength="50" placeholder="显示在导航栏 / 首屏大标题" />
        </label>

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
          <div class="inline-row">
            <input v-model.trim="basic.heroBackground" class="input" placeholder="留空则使用内置渐变背景" />
            <label class="btn-ghost">
              {{ uploadingBg ? '上传中...' : '上传' }}
              <input type="file" accept="image/*" hidden :disabled="uploadingBg" @change="onPickBackground" />
            </label>
          </div>
          <div v-if="basic.heroBackground" class="preview">
            <img :src="basic.heroBackground" alt="背景预览" />
          </div>
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

.inline-row {
  display: flex;
  gap: var(--space-3);
  align-items: center;
}
.inline-row .input {
  flex: 1;
}

.hint {
  font: var(--text-caption);
  color: var(--text-subtle);
}

.preview {
  margin-top: var(--space-2);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-sm);
  overflow: hidden;
  max-width: 360px;
}
.preview img {
  width: 100%;
  height: 120px;
  object-fit: cover;
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
