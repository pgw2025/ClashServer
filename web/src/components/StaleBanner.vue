<template>
  <div v-if="isStale" class="banner-stale">
    <div class="banner-icon">
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"></path>
        <line x1="12" y1="9" x2="12" y2="13"></line>
        <line x1="12" y1="17" x2="12.01" y2="17"></line>
      </svg>
    </div>
    <div class="banner-content">
      <span class="banner-title">数据为历史缓存，上游订阅暂时不可达</span>
      <span v-if="lastGoodUpdate" class="banner-time">（最近成功更新于：{{ formatTime(lastGoodUpdate) }}）</span>
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{ isStale: boolean; lastGoodUpdate: string | null }>()

function formatTime(iso: string | null): string {
  if (!iso) return '未知'
  const d = new Date(iso)
  return d.toLocaleString('zh-CN')
}
</script>

<style scoped>
.banner-stale {
  display: flex;
  align-items: center;
  gap: 12px;
  background: var(--warning-subtle);
  border: 1px solid rgba(245, 158, 11, 0.25);
  color: var(--warning);
  padding: 10px 16px;
  border-radius: 10px;
  margin-bottom: 20px;
  font-size: 13.5px;
}
.banner-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.banner-content {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}
.banner-title {
  font-weight: 500;
  color: inherit;
}
.banner-time {
  opacity: 0.85;
  font-size: 12.5px;
}
</style>