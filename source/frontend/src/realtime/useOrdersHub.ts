import * as signalR from '@microsoft/signalr'
import { onBeforeUnmount, ref } from 'vue'
import { tokenStore } from '@/api/tokenStore'

export type HubState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

/**
 * Creates a SignalR connection to /hubs/orders with automatic reconnect. `onJoin` runs after every (re)connect so
 * groups are re-joined, and `onResync` lets the page reload its REST snapshot (events missed while offline are not replayed).
 */
export function useOrdersHub(options: {
  onJoin: (conn: signalR.HubConnection) => Promise<void>
  onResync?: () => void
  handlers: Record<string, (payload: any) => void>
  authenticated?: boolean
}) {
  const state = ref<HubState>('connecting')
  const base = import.meta.env.VITE_API_BASE_URL ?? ''
  const conn = new signalR.HubConnectionBuilder()
    .withUrl(`${base}/hubs/orders`, options.authenticated === false ? {} : { accessTokenFactory: () => tokenStore.accessToken ?? '' })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000])
    .configureLogging(signalR.LogLevel.Warning)
    .build()

  for (const [event, handler] of Object.entries(options.handlers)) conn.on(event, handler)

  conn.onreconnecting(() => (state.value = 'reconnecting'))
  conn.onreconnected(async () => {
    state.value = 'connected'
    await options.onJoin(conn)
    options.onResync?.()
  })
  conn.onclose(() => (state.value = 'disconnected'))

  let stopped = false
  async function start() {
    state.value = 'connecting'
    try {
      await conn.start()
      state.value = 'connected'
      await options.onJoin(conn)
    } catch {
      state.value = 'disconnected'
      if (!stopped) setTimeout(start, 5000)
    }
  }

  async function stop() {
    stopped = true
    await conn.stop()
  }

  onBeforeUnmount(stop)
  return { state, start, stop, conn }
}
