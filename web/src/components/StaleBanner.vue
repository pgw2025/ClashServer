<template>
  <div v-if="isStale" class="banner-stale">
    ⚠️ 数据为缓存，上游订阅不可达
    <span v-if="lastGoodUpdate" class="muted">（最近成功：{{ formatTime(lastGoodUpdate) }}）</span>
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