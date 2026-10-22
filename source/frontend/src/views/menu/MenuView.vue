<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useToast } from 'primevue/usetoast'
import { api, ApiError, errorMessage } from '@/api/http'
import type { AddOn, Category, MenuItem, Paged, Variant } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { money } from '@/utils/format'

const auth = useAuthStore()
const toast = useToast()
const canEdit = computed(() => auth.isSuperAdmin)
const categories = ref<Category[]>([])
const addOns = ref<AddOn[]>([])
const items = ref<MenuItem[]>([])
const loading = ref(false)
const search = ref('')
const categoryFilter = ref<string | null>(null)

const itemDialog = ref(false)
const editingItem = ref<MenuItem | null>(null)
const fieldErrors = ref<Record<string, string>>({})
const itemForm = reactive({
  categoryId: '', sku: '', name: '', description: '', imageUrl: '', isActive: true, sortOrder: 0,
  variants: [] as Variant[], addOnIds: [] as string[],
})

const catDialog = ref(false)
const catForm = reactive({ id: '', name: '', sortOrder: 0, isActive: true })
const addOnDialog = ref(false)
const addOnForm = reactive({ id: '', name: '', price: 0, isActive: true })

const visibleItems = computed(() =>
  items.value.filter(
    (i) => (!categoryFilter.value || i.categoryId === categoryFilter.value) && (!search.value || (i.name + i.sku).toLowerCase().includes(search.value.toLowerCase())),
  ),
)

async function load() {
  loading.value = true
  try {
    const [c, a, i] = await Promise.all([
      api.get<Category[]>('/menu/categories'),
      api.get<AddOn[]>('/menu/add-ons'),
      api.get<Paged<MenuItem>>('/menu/items', { pageSize: 200 }),
    ])
    categories.value = c
    addOns.value = a
    items.value = i.items
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  } finally {
    loading.value = false
  }
}

function openItem(item?: MenuItem) {
  editingItem.value = item ?? null
  fieldErrors.value = {}
  Object.assign(
    itemForm,
    item
      ? { ...item, description: item.description ?? '', imageUrl: item.imageUrl ?? '', variants: item.variants.map((v) => ({ ...v })), addOnIds: [...item.addOnIds] }
      : {
          categoryId: categoryFilter.value ?? categories.value[0]?.id ?? '', sku: '', name: '', description: '', imageUrl: '', isActive: true, sortOrder: 0,
          variants: [{ name: 'M', basePrice: 35000, sortOrder: 0, isActive: true }], addOnIds: [],
        },
  )
  itemDialog.value = true
}

function addVariant() {
  itemForm.variants.push({ name: '', basePrice: 0, sortOrder: itemForm.variants.length, isActive: true })
}

async function saveItem() {
  fieldErrors.value = {}
  const body = { ...itemForm, description: itemForm.description || null, imageUrl: itemForm.imageUrl || null, rowVersion: editingItem.value?.rowVersion ?? null }
  try {
    if (editingItem.value) await api.put(`/menu/items/${editingItem.value.id}`, body)
    else await api.post('/menu/items', body)
    itemDialog.value = false
    toast.add({ severity: 'success', summary: 'Đã lưu món', detail: 'Đơn đã tạo trước đó giữ nguyên giá snapshot.', life: 3000 })
    load()
  } catch (e) {
    if (e instanceof ApiError) fieldErrors.value = e.fieldErrors
    toast.add({ severity: 'error', summary: 'Không lưu được', detail: errorMessage(e), life: 5000 })
  }
}

function openCategory(c?: Category) {
  Object.assign(catForm, c ?? { id: '', name: '', sortOrder: categories.value.length, isActive: true })
  catDialog.value = true
}
async function saveCategory() {
  try {
    const body = { name: catForm.name, sortOrder: catForm.sortOrder, isActive: catForm.isActive }
    if (catForm.id) await api.put(`/menu/categories/${catForm.id}`, body)
    else await api.post('/menu/categories', body)
    catDialog.value = false
    load()
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  }
}

function openAddOn(a?: AddOn) {
  Object.assign(addOnForm, a ?? { id: '', name: '', price: 5000, isActive: true })
  addOnDialog.value = true
}
async function saveAddOn() {
  try {
    const body = { name: addOnForm.name, price: addOnForm.price, isActive: addOnForm.isActive }
    if (addOnForm.id) await api.put(`/menu/add-ons/${addOnForm.id}`, body)
    else await api.post('/menu/add-ons', body)
    addOnDialog.value = false
    load()
  } catch (e) {
    toast.add({ severity: 'error', summary: 'Lỗi', detail: errorMessage(e), life: 5000 })
  }
}

const priceRange = (i: MenuItem) => {
  const prices = i.variants.filter((v) => v.isActive).map((v) => v.basePrice)
  if (!prices.length) return '—'
  const min = Math.min(...prices)
  const max = Math.max(...prices)
  return min === max ? money(min) : `${money(min)} – ${money(max)}`
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <h1>Thực đơn</h1>
        <p>Danh mục, món, size và topping dùng chung toàn chuỗi. {{ canEdit ? '' : 'Chỉ Super Admin được sửa.' }}</p>
      </div>
      <div class="toolbar">
        <InputText v-model="search" placeholder="Tìm món / SKU" />
        <Button v-if="canEdit" id="btn-new-item" label="Thêm món" icon="pi pi-plus" @click="openItem()" />
      </div>
    </div>

    <div class="layout">
      <aside class="stack">
        <div class="card">
          <div class="card-title"><h3>Danh mục</h3><Button v-if="canEdit" icon="pi pi-plus" text rounded size="small" @click="openCategory()" /></div>
          <div class="cat" :class="{ active: !categoryFilter }" @click="categoryFilter = null">Tất cả <span class="faint">{{ items.length }}</span></div>
          <div v-for="c in categories" :key="c.id" class="cat" :class="{ active: categoryFilter === c.id, off: !c.isActive }" @click="categoryFilter = c.id">
            {{ c.name }}
            <span class="spacer" />
            <span class="faint">{{ items.filter((i) => i.categoryId === c.id).length }}</span>
            <i v-if="canEdit" class="pi pi-pencil edit" @click.stop="openCategory(c)" />
          </div>
        </div>
        <div class="card">
          <div class="card-title"><h3>Topping</h3><Button v-if="canEdit" icon="pi pi-plus" text rounded size="small" @click="openAddOn()" /></div>
          <div v-for="a in addOns" :key="a.id" class="cat" :class="{ off: !a.isActive }" @click="canEdit && openAddOn(a)">
            {{ a.name }}<span class="spacer" /><span class="small muted">+{{ money(a.price) }}</span>
          </div>
          <div v-if="!addOns.length" class="faint small">Chưa có topping</div>
        </div>
      </aside>

      <div>
        <div v-if="loading" class="item-grid"><Skeleton v-for="i in 6" :key="i" height="150px" /></div>
        <EmptyState v-else-if="!visibleItems.length" icon="pi-book" title="Không có món phù hợp" />
        <div v-else class="item-grid">
          <div v-for="i in visibleItems" :key="i.id" class="card item pop-in" :class="{ off: !i.isActive }" @click="canEdit && openItem(i)">
            <div class="row">
              <div class="thumb"><img v-if="i.imageUrl" :src="i.imageUrl" :alt="i.name" /><i v-else class="pi pi-star" /></div>
              <div style="min-width: 0">
                <div style="font-weight: 700">{{ i.name }}</div>
                <div class="faint small mono">{{ i.sku }} · {{ i.categoryName }}</div>
              </div>
            </div>
            <div class="small muted desc">{{ i.description || 'Chưa có mô tả' }}</div>
            <div class="row wrap" style="gap: 6px">
              <span v-for="v in i.variants" :key="v.id" class="pill" :class="v.isActive ? 'Confirmed' : 'Unpaid'">{{ v.name }} · {{ money(v.basePrice) }}</span>
            </div>
            <div class="row"><strong class="gradient-text">{{ priceRange(i) }}</strong><span class="spacer" /><span v-if="!i.isActive" class="pill Cancelled">Ngừng bán</span></div>
          </div>
        </div>
      </div>
    </div>

    <Dialog v-model:visible="itemDialog" modal :header="editingItem ? 'Sửa món' : 'Thêm món'" :style="{ width: '720px' }">
      <div class="form-grid">
        <div class="field">
          <label for="mi-name">Tên món</label>
          <InputText id="mi-name" v-model="itemForm.name" :invalid="!!fieldErrors.name" />
          <small class="error">{{ fieldErrors.name }}</small>
        </div>
        <div class="field">
          <label for="mi-sku">SKU</label>
          <InputText id="mi-sku" v-model="itemForm.sku" :invalid="!!fieldErrors.sku" />
          <small class="error">{{ fieldErrors.sku }}</small>
        </div>
        <div class="field">
          <label for="mi-cat">Danh mục</label>
          <Select input-id="mi-cat" v-model="itemForm.categoryId" :options="categories" option-label="name" option-value="id" />
        </div>
        <div class="field"><label for="mi-sort">Thứ tự</label><InputNumber input-id="mi-sort" v-model="itemForm.sortOrder" :min="0" /></div>
        <div class="field full"><label for="mi-desc">Mô tả</label><Textarea id="mi-desc" v-model="itemForm.description" rows="2" auto-resize /></div>
        <div class="field full"><label for="mi-img">Ảnh (URL https)</label><InputText id="mi-img" v-model="itemForm.imageUrl" placeholder="https://…" /></div>
        <div class="field full">
          <label>Size / biến thể</label>
          <div v-for="(v, idx) in itemForm.variants" :key="idx" class="row variant-row">
            <InputText v-model="v.name" placeholder="Tên (S/M/L)" style="width: 140px" />
            <InputNumber v-model="v.basePrice" :min="0" :step="1000" locale="vi-VN" suffix=" ₫" placeholder="Giá" />
            <ToggleSwitch v-model="v.isActive" v-tooltip.top="'Đang bán'" />
            <Button v-if="!v.id" icon="pi pi-trash" text rounded severity="danger" @click="itemForm.variants.splice(idx, 1)" />
          </div>
          <small class="error">{{ fieldErrors.variants }}</small>
          <div><Button label="Thêm size" icon="pi pi-plus" text size="small" @click="addVariant" /></div>
        </div>
        <div class="field full">
          <label for="mi-addons">Topping áp dụng</label>
          <MultiSelect input-id="mi-addons" v-model="itemForm.addOnIds" :options="addOns" option-label="name" option-value="id" display="chip" />
        </div>
        <div class="row"><ToggleSwitch v-model="itemForm.isActive" input-id="mi-active" /><label for="mi-active">Đang bán</label></div>
      </div>
      <template #footer>
        <Button label="Hủy" text severity="secondary" @click="itemDialog = false" />
        <Button id="btn-save-item" label="Lưu" icon="pi pi-check" @click="saveItem" />
      </template>
    </Dialog>

    <Dialog v-model:visible="catDialog" modal :header="catForm.id ? 'Sửa danh mục' : 'Thêm danh mục'" :style="{ width: '400px' }">
      <div class="stack">
        <div class="field"><label for="cat-name">Tên</label><InputText id="cat-name" v-model="catForm.name" /></div>
        <div class="field"><label for="cat-sort">Thứ tự</label><InputNumber input-id="cat-sort" v-model="catForm.sortOrder" :min="0" /></div>
        <div class="row"><ToggleSwitch v-model="catForm.isActive" input-id="cat-active" /><label for="cat-active">Hiển thị</label></div>
      </div>
      <template #footer><Button label="Lưu" icon="pi pi-check" :disabled="!catForm.name.trim()" @click="saveCategory" /></template>
    </Dialog>

    <Dialog v-model:visible="addOnDialog" modal :header="addOnForm.id ? 'Sửa topping' : 'Thêm topping'" :style="{ width: '400px' }">
      <div class="stack">
        <div class="field"><label for="ao-name">Tên</label><InputText id="ao-name" v-model="addOnForm.name" /></div>
        <div class="field"><label for="ao-price">Giá</label><InputNumber input-id="ao-price" v-model="addOnForm.price" :min="0" :step="1000" locale="vi-VN" /></div>
        <div class="row"><ToggleSwitch v-model="addOnForm.isActive" input-id="ao-active" /><label for="ao-active">Đang bán</label></div>
      </div>
      <template #footer><Button label="Lưu" icon="pi pi-check" :disabled="!addOnForm.name.trim()" @click="saveAddOn" /></template>
    </Dialog>
  </div>
</template>

<style scoped>
.layout { display: grid; grid-template-columns: 280px 1fr; gap: 18px; align-items: start; }
.cat { display: flex; align-items: center; gap: 8px; padding: 8px 10px; border-radius: 10px; cursor: pointer; transition: background .15s; }
.cat:hover { background: var(--surface-hover); }
.cat.active { background: var(--accent-soft); font-weight: 600; }
.cat.off { opacity: .5; }
.cat .edit { opacity: 0; font-size: .8rem; color: var(--text-faint); }
.cat:hover .edit { opacity: 1; }
.item-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 14px; }
.item { display: flex; flex-direction: column; gap: 10px; cursor: pointer; transition: transform .2s, box-shadow .2s, border-color .2s; padding: 16px; }
.item:hover { transform: translateY(-3px); box-shadow: var(--shadow-glow); border-color: var(--border-strong); }
.item.off { opacity: .55; }
.thumb { width: 46px; height: 46px; flex-shrink: 0; border-radius: 12px; overflow: hidden; display: grid; place-items: center; background: var(--accent-soft); color: var(--accent); }
.thumb img { width: 100%; height: 100%; object-fit: cover; }
.desc { display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; min-height: 2.6em; }
.variant-row { margin-bottom: 8px; }
@media (max-width: 900px) { .layout { grid-template-columns: 1fr; } }
</style>
