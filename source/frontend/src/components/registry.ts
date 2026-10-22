import type { App } from 'vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Password from 'primevue/password'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import MultiSelect from 'primevue/multiselect'
import DatePicker from 'primevue/datepicker'
import ToggleSwitch from 'primevue/toggleswitch'
import Checkbox from 'primevue/checkbox'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Dialog from 'primevue/dialog'
import Drawer from 'primevue/drawer'
import Toast from 'primevue/toast'
import ConfirmDialog from 'primevue/confirmdialog'
import Tag from 'primevue/tag'
import Message from 'primevue/message'
import Skeleton from 'primevue/skeleton'
import ProgressSpinner from 'primevue/progressspinner'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import Badge from 'primevue/badge'
import Avatar from 'primevue/avatar'
import SelectButton from 'primevue/selectbutton'
import Divider from 'primevue/divider'
import StatusPill from './StatusPill.vue'
import MoneyText from './MoneyText.vue'
import EmptyState from './EmptyState.vue'
import BranchPicker from './BranchPicker.vue'

/** Global registration keeps views concise; all components are tree-shaken per chunk by Vite anyway. */
export function registerComponents(app: App) {
  const components = {
    Button, InputText, InputNumber, Password, Textarea, Select, MultiSelect, DatePicker, ToggleSwitch, Checkbox,
    DataTable, Column, Dialog, Drawer, Toast, ConfirmDialog, Tag, Message, Skeleton, ProgressSpinner,
    Tabs, TabList, Tab, TabPanels, TabPanel, Badge, Avatar, SelectButton, Divider,
    StatusPill, MoneyText, EmptyState, BranchPicker,
  }
  for (const [name, component] of Object.entries(components)) {
    app.component(name, component)
  }
}
