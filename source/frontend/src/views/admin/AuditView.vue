<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, errorMessage } from '@/api/http'
import type { AuditLog, Paged } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { dateTime } from '@/utils/format'

const auth = useAuthStore()
const rows = ref<AuditLog[]>([])
const total = ref(0)
const page = ref(1)
const action = ref('')
const onlyBranch = ref(true)
const loading = ref(false)
const error = ref('')
const detail = ref<AuditLog | null>(null)

async function load() {
  loading.value = true
  error.value = ''
  try {
    const res = await api.get<Paged<AuditLog>>('/audit-logs', {
      branchId: onlyBranch.value ? auth.currentBranch?.id : undefined,
      action: action.value,
      page: page.value,
      pageSize: 50,
    })
    rows.value = res.items
    total.value = res.totalCount
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    loading.value = false
  }
}

function pretty(json?: string | null) {
  if (!json) return '—'
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return json
  }
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Nhật ký thao tác</h1>
        <p>Audit log cho thao tác nhạy cảm: phân quyền, giá, kho, hủy/hoàn tiền, chấm công.</p>
      </div>
      <div class="toolbar">
        <div class="row"><ToggleSwitch v-model="onlyBranch" input-id="audit-branch" @change="load" /><label for="audit-branch" class="small">Chỉ chi nhánh hiện tại</label></div>
        <InputText v-model="action" placeholder="Lọc hành động, vd: order.cancel" @keyup.enter="(page = 1), load()" />
        <Button icon="pi pi-search" @click="(page = 1), load()" />
      </div>
    </div>
    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
    <div class="card flush">
      <DataTable :value="rows" :loading="loading" lazy paginator :rows="50" :total-records="total" @page="(e) => ((page = e.page + 1), load())" size="small">
        <template #empty><EmptyState icon="pi-shield" title="Chưa có bản ghi" /></template>
        <Column header="Thời gian"><template #body="{ data: r }"><span class="small nowrap">{{ dateTime(r.timestampUtc) }}</span></template></Column>
        <Column field="actorName" header="Người thực hiện" />
        <Column header="Hành động"><template #body="{ data: r }"><span class="mono small">{{ r.action }}</span></template></Column>
        <Column header="Đối tượng"><template #body="{ data: r }"><span class="small">{{ r.resourceType }}</span></template></Column>
        <Column header="IP"><template #body="{ data: r }"><span class="faint small mono">{{ r.ipAddress ?? '—' }}</span></template></Column>
        <Column header=""><template #body="{ data: r }"><Button icon="pi pi-eye" text rounded @click="detail = r" /></template></Column>
      </DataTable>
    </div>

    <Drawer :visible="!!detail" position="right" header="Chi tiết audit" style="width: 520px" @update:visible="(v) => !v && (detail = null)">
      <div v-if="detail" class="stack small">
        <div><span class="muted">Hành động:</span> <span class="mono">{{ detail.action }}</span></div>
        <div><span class="muted">Đối tượng:</span> {{ detail.resourceType }} <span class="mono faint">{{ detail.resourceId }}</span></div>
        <div><span class="muted">Correlation ID:</span> <span class="mono">{{ detail.correlationId ?? '—' }}</span></div>
        <pre class="json">{{ pretty(detail.changes) }}</pre>
      </div>
    </Drawer>
  </div>
</template>

<style scoped>
.json { background: rgba(0, 0, 0, .3); border: 1px solid var(--border); border-radius: 10px; padding: 12px; overflow: auto; max-height: 60vh; font-size: .8rem; }
</style>
