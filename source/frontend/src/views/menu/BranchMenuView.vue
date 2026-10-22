<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, errorMessage } from '@/api/http'
import type { BranchMenuItem } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { money } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const rows = ref<BranchMenuItem[]>([])
const loading = ref(false)
const saving = ref(false)
const dirty = ref(false)
const search = ref('')
const filtered = computed(() => rows.value.filter((r) => !search.value || (r.name + r.sku + r.categoryName).toLowerCase().includes(search.value.toLowerCase())))

async function load() {
  if (!auth.currentBranch) return
  loading.value = true
  try {
    rows.value = await api.get<BranchMenuItem[]>(`/branches/${auth.currentBranch.id}/menu`)
    dirty.value = false
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

async function save() {
  if (!auth.currentBranch) return
  saving.value = true
  try {
    await api.put(`/branches/${auth.currentBranch.id}/menu`, {
      items: rows.value.map((r) => ({
        menuItemId: r.menuItemId,
        isAvailable: r.isAvailable,
        variantPrices: r.variants.map((v) => ({ variantId: v.variantId, price: v.branchPrice ?? null })),
      })),
    })
    toast.add({ severity: 'success', summary: 'Đã cập nhật menu chi nhánh', detail: 'Menu công khai được làm mới ngay.', life: 3000 })
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không lưu được', detail: errorMessage(e), life: 5000 })
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Menu chi nhánh</h1>
        <p>Bật/tắt món và đặt giá riêng tại {{ auth.currentBranch?.name }}. Để trống giá chi nhánh để dùng giá chuẩn.</p>
      </div>
      <div class="toolbar">
        <InputText v-model="search" placeholder="Tìm món" />
        <Button label="Hoàn tác" text severity="secondary" :disabled="!dirty" @click="load" />
        <Button v-if="auth.isAdmin" id="btn-save-branch-menu" label="Lưu thay đổi" icon="pi pi-save" :disabled="!dirty" :loading="saving" @click="save" />
      </div>
    </div>
    <div class="card flush">
      <DataTable :value="filtered" :loading="loading" data-key="menuItemId" row-group-mode="subheader" group-rows-by="categoryName" sort-field="categoryName" :sort-order="1">
        <template #groupheader="{ data }"><strong class="gradient-text">{{ data.categoryName }}</strong></template>
        <template #empty><EmptyState icon="pi-sliders-h" title="Chưa có món" /></template>
        <Column header="Món">
          <template #body="{ data: r }">
            <div style="font-weight: 600">{{ r.name }}</div>
            <div class="faint small mono">{{ r.sku }} <span v-if="!r.itemActive" class="pill Cancelled">ngừng toàn chuỗi</span></div>
          </template>
        </Column>
        <Column header="Đang bán" style="width: 120px">
          <template #body="{ data: r }"><ToggleSwitch v-model="r.isAvailable" :disabled="!auth.isAdmin || !r.itemActive" @update:model-value="dirty = true" /></template>
        </Column>
        <Column header="Giá theo size">
          <template #body="{ data: r }">
            <div class="variants">
              <div v-for="v in r.variants" :key="v.variantId" class="variant">
                <span class="vname">{{ v.name }}</span>
                <InputNumber
                  v-model="v.branchPrice"
                  :placeholder="money(v.basePrice)"
                  :min="0"
                  :step="1000"
                  locale="vi-VN"
                  :disabled="!auth.isAdmin"
                  input-class="price-input"
                  @update:model-value="dirty = true"
                />
                <span class="faint small">→ {{ money(v.branchPrice ?? v.basePrice) }}</span>
              </div>
            </div>
          </template>
        </Column>
      </DataTable>
    </div>
  </div>
</template>

<style scoped>
.variants { display: flex; flex-wrap: wrap; gap: 10px 18px; }
.variant { display: flex; align-items: center; gap: 8px; }
.vname { font-weight: 600; min-width: 28px; }
:deep(.price-input) { width: 120px; }
</style>
