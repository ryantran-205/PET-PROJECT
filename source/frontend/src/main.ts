import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import Tooltip from 'primevue/tooltip'
import 'primeicons/primeicons.css'
import './styles/main.css'

import App from './App.vue'
import { router } from './router'
import { BrewlinePreset } from './theme'
import { registerComponents } from './components/registry'

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.use(PrimeVue, {
  theme: {
    preset: BrewlinePreset,
    options: { darkModeSelector: ':root', cssLayer: false },
  },
  ripple: true,
})
app.use(ToastService)
app.use(ConfirmationService)
app.directive('tooltip', Tooltip)
registerComponents(app)
app.mount('#app')
