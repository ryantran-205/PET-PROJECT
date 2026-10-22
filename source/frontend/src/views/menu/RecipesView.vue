<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, errorMessage } from '@/api/http'
import type { AddOn, Ingredient, MenuItem, Paged, RecipeLine } from '@/api/types'

const toast = useToast()
const items = ref<MenuItem[]>([])
const addOns = ref<AddOn[]>([])
const ingredients = ref<Ingredient[]>([])
const target = ref<{ kind: 'variant' | 'addon'; id: string; label: string } | null>(null)
const lines = ref<RecipeLine[]>([])
const loadingLines = ref(false)
const saving = ref(false)
const ingDialog = ref(false)
const ingForm = reactive({ id: '', code: '', name: '', unit: 'g', isActive: true })
const unitOptions = ['g', 'ml', 'pcs', 'kg', 'l']

const activeIngredients = computed(() => ingredients.value.filter((i) => i.isActive))
const ingredientById = (id: string) => ingredients.value.find((i) => i.id === id)

async function load() {
  try {
    const [i, a, g] = await Promise.all([
      api.get<Paged<MenuItem>>('/menu/items', { pageSize: 200 }),
      api.get<AddOn[]>('/menu/add-ons'),
      api.get<Ingredient[]>('/ingredients'),
    ])
    items.value = i.items
    addOns.value = a
    ingredients.value = g
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  }
}

async function select(kind: 'variant' | 'addon', id: string, label: string) {
  target.value = { kind, id, label }
  loadingLines.value = true
  try {
    lines.value = await api.get<RecipeLine[]>(kind === 'variant' ? `/menu/variants/${id}/recipe` : `/menu/add-ons/${id}/recipe`)
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loadingLines.value = false
  }
}

async function save() {
  if (!target.value) return
  saving.value = true
  const t = target.value
  try {
    const body = { lines: lines.value.filter((l) => l.ingredientId).map((l) => ({ ingredientId: l.ingredientId, quantity: l.quantity })) }
    await api.put(t.kind === 'variant' ? `/menu/variants/${t.id}/recipe` : `/menu/add-ons/${t.id}/recipe`, body)
    await select(t.kind, t.id, t.label)
    toast.add({ severity: 'success', summary: 'Đã lưu định mức', detail: 'Áp dụng cho đơn xác nhận từ bây giờ.', life: 3000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không lưu được', detail: errorMessage(e), life: 5000 })
  } finally {
    saving.value = false
  }
}

function openIngredient(i?: Ingredient) {
  Object.assign(ingForm, i ?? { id: '', code: '', name: '', unit: 'g', isActive: true })
  ingDialog.value = true
}

async function saveIngredient() {
  try {
    const body = { code: ingForm.code, name: ingForm.name, unit: ingForm.unit, isActive: ingForm.isActive }
    if (ingForm.id) await api.put(`/ingredients/${ingForm.id}`, body)
    else await api.post('/ingredients', body)
    ingDialog.value = false
    ingredients.value = await api.get<Ingredient[]>('/ingredients')
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Không lưu được', detail: errorMessage(e), life: 5000 })
  }
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Định mức nguyên liệu</h1>
        <p>Định mức theo từng size và topping. Kho bị trừ khi đơn được xác nhận.</p>
      </div>
      <Button id="btn-new-ingredient" label="Nguyên liệu mới" icon="pi pi-plus" outlined @click="openIngredient()" />
    </div>

    <div class="layout">
      <div class="card picker">
        <h3 style="margin-bottom: 10px">Chọn món / topping</h3>
        <div v-for="i in items" :key="i.id" class="group">
          <div class="gname">{{ i.name }}</div>
          <div
            v-for="v in i.variants"
            :key="v.id"
            class="opt"
            :class="{ active: target?.kind === 'variant' && target.id === v.id }"
            @click="select('variant', v.id!, `${i.name} · ${v.name}`)"
          >
            Size {{ v.name }}
          </div>
        </div>
        <div class="group">
          <div class="gname">Topping</div>
          <div v-for="a in addOns" :key="a.id" class="opt" :class="{ active: target?.kind === 'addon' && target.id === a.id }" @click="select('addon', a.id, `Topping · ${a.name}`)">
            {{ a.name }}
          </div>
        </div>
      </div>

      <div class="stack">
        <div class="card">
          <EmptyState v-if="!target" icon="pi-sitemap" title="Chọn một size hoặc topping để chỉnh định mức" />
          <template v-else>
            <div class="card-title">
              <h2>{{ target.label }}</h2>
              <div class="row">
                <Button label="Thêm dòng" icon="pi pi-plus" text @click="lines.push({ ingredientId: '', quantity: 0 })" />
                <Button id="btn-save-recipe" label="Lưu" icon="pi pi-save" :loading="saving" @click="save" />
              </div>
            </div>
            <Skeleton v-if="loadingLines" height="120px" />
            <EmptyState v-else-if="!lines.length" icon="pi-inbox" title="Chưa có định mức" message="Món không có định mức sẽ không trừ kho." />
            <div v-else class="stack">
              <div v-for="(l, idx) in lines" :key="idx" class="row">
                <Select v-model="l.ingredientId" :options="activeIngredients" option-label="name" option-value="id" filter placeholder="Nguyên liệu" style="flex: 1" />
                <InputNumber v-model="l.quantity" :min="0" :max-fraction-digits="3" :suffix="' ' + (ingredientById(l.ingredientId)?.unit ?? '')" style="width: 160px" />
                <Button icon="pi pi-trash" text rounded severity="danger" @click="lines.splice(idx, 1)" />
              </div>
            </div>
          </template>
        </div>

        <div class="card flush">
          <div style="padding: 14px 16px"><h3>Danh mục nguyên liệu</h3></div>
          <DataTable :value="ingredients" size="small" paginator :rows="10">
            <Column field="code" header="Mã"><template #body="{ data: i }"><span class="mono">{{ i.code }}</span></template></Column>
            <Column field="name" header="Tên" />
            <Column field="unit" header="ĐVT" />
            <Column header="Trạng thái"><template #body="{ data: i }"><span class="pill" :class="i.isActive ? 'Ready' : 'Cancelled'">{{ i.isActive ? 'Dùng' : 'Ngừng' }}</span></template></Column>
            <Column header=""><template #body="{ data: i }"><Button icon="pi pi-pencil" text rounded @click="openIngredient(i)" /></template></Column>
          </DataTable>
        </div>
      </div>
    </div>

    <Dialog v-model:visible="ingDialog" modal :header="ingForm.id ? 'Sửa nguyên liệu' : 'Nguyên liệu mới'" :style="{ width: '420px' }">
      <div class="stack">
        <div class="field"><label for="ing-code">Mã</label><InputText id="ing-code" v-model="ingForm.code" /></div>
        <div class="field"><label for="ing-name">Tên</label><InputText id="ing-name" v-model="ingForm.name" /></div>
        <div class="field"><label for="ing-unit">Đơn vị</label><Select input-id="ing-unit" v-model="ingForm.unit" :options="unitOptions" editable /></div>
        <div class="row"><ToggleSwitch v-model="ingForm.isActive" input-id="ing-active" /><label for="ing-active">Đang dùng</label></div>
      </div>
      <template #footer><Button label="Lưu" icon="pi pi-check" @click="saveIngredient" /></template>
    </Dialog>
  </div>
</template>

<style scoped>
.layout { display: grid; grid-template-columns: 300px 1fr; gap: 18px; align-items: start; }
.picker { max-height: calc(100vh - 200px); overflow-y: auto; }
.group { margin-bottom: 12px; }
.gname { font-size: .78rem; text-transform: uppercase; letter-spacing: .06em; color: var(--text-faint); margin-bottom: 4px; }
.opt { padding: 7px 10px; border-radius: 9px; cursor: pointer; transition: background .15s; }
.opt:hover { background: var(--surface-hover); }
.opt.active { background: var(--accent-soft); font-weight: 600; }
@media (max-width: 900px) { .layout { grid-template-columns: 1fr; } }
</style>
