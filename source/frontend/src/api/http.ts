/**
 * Thin fetch wrapper:
 * - attaches the access token, refreshes it once on 401 (single-flight) and retries the request
 * - per-request timeout (AbortController), correlation id header
 * - converts RFC 7807 ProblemDetails into ApiError with field errors for forms
 */
import { tokenStore } from './tokenStore'

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  code?: string
  correlationId?: string
  errors?: Record<string, string[]>
  [key: string]: unknown
}

export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly problem: ProblemDetails
  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail || problem.title || `HTTP ${status}`)
    this.status = status
    this.code = problem.code ?? (status === 0 ? 'network_error' : 'error')
    this.problem = problem
  }
  get fieldErrors(): Record<string, string> {
    const out: Record<string, string> = {}
    for (const [k, v] of Object.entries(this.problem.errors ?? {})) {
      out[k.charAt(0).toLowerCase() + k.slice(1)] = v.join(' ')
    }
    return out
  }
}

export interface RequestOptions {
  method?: string
  body?: unknown
  query?: Record<string, string | number | boolean | null | undefined>
  headers?: Record<string, string>
  timeoutMs?: number
  anonymous?: boolean
  raw?: boolean
}

const BASE = import.meta.env.VITE_API_BASE_URL ?? ''

let refreshing: Promise<boolean> | null = null
type SessionListener = () => void
const sessionExpiredListeners: SessionListener[] = []
export function onSessionExpired(listener: SessionListener) {
  sessionExpiredListeners.push(listener)
}

function newCorrelationId() {
  return crypto.randomUUID().replace(/-/g, '')
}

function buildUrl(path: string, query?: RequestOptions['query']) {
  const url = new URL(BASE + '/api/v1' + path, window.location.origin)
  for (const [k, v] of Object.entries(query ?? {})) {
    if (v !== undefined && v !== null && v !== '') url.searchParams.set(k, String(v))
  }
  return url.toString()
}

async function refreshAccessToken(): Promise<boolean> {
  const refreshToken = tokenStore.refreshToken
  if (!refreshToken) return false
  try {
    const res = await fetch(buildUrl('/auth/refresh'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Correlation-Id': newCorrelationId() },
      body: JSON.stringify({ refreshToken }),
    })
    if (!res.ok) return false
    const data = await res.json()
    tokenStore.save(data)
    return true
  } catch {
    return false
  }
}

async function send(path: string, opts: RequestOptions, retried: boolean): Promise<Response> {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), opts.timeoutMs ?? 20000)
  const headers: Record<string, string> = { Accept: 'application/json', 'X-Correlation-Id': newCorrelationId(), ...opts.headers }
  if (opts.body !== undefined) headers['Content-Type'] = 'application/json'
  if (!opts.anonymous && tokenStore.accessToken) headers.Authorization = `Bearer ${tokenStore.accessToken}`

  let res: Response
  try {
    res = await fetch(buildUrl(path, opts.query), {
      method: opts.method ?? 'GET',
      headers,
      body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
      signal: controller.signal,
    })
  } catch (e) {
    const aborted = (e as Error).name === 'AbortError'
    throw new ApiError(0, {
      title: aborted ? 'Hết thời gian chờ' : 'Mất kết nối',
      detail: aborted ? 'Máy chủ phản hồi quá lâu, vui lòng thử lại.' : 'Không thể kết nối tới máy chủ. Kiểm tra mạng và thử lại.',
      code: aborted ? 'timeout' : 'network_error',
    })
  } finally {
    clearTimeout(timer)
  }

  if (res.status === 401 && !opts.anonymous && !retried && tokenStore.refreshToken) {
    refreshing ??= refreshAccessToken().finally(() => (refreshing = null))
    if (await refreshing) return send(path, opts, true)
    tokenStore.clear()
    sessionExpiredListeners.forEach((l) => l())
  }
  return res
}

export async function request<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const res = await send(path, opts, false)
  if (!res.ok) {
    let problem: ProblemDetails = { title: res.statusText, status: res.status }
    try {
      problem = await res.json()
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(res.status, problem)
  }
  if (opts.raw) return res as unknown as T
  if (res.status === 204) return undefined as T
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(path: string, query?: RequestOptions['query'], opts: RequestOptions = {}) => request<T>(path, { ...opts, query }),
  post: <T>(path: string, body?: unknown, opts: RequestOptions = {}) => request<T>(path, { ...opts, method: 'POST', body: body ?? {} }),
  put: <T>(path: string, body?: unknown, opts: RequestOptions = {}) => request<T>(path, { ...opts, method: 'PUT', body: body ?? {} }),
  del: <T>(path: string, opts: RequestOptions = {}) => request<T>(path, { ...opts, method: 'DELETE' }),
  /** Downloads a binary response (PDF/CSV) with auth and saves it using the server-provided filename. */
  async download(path: string, query?: RequestOptions['query'], fallbackName = 'download') {
    const res = await request<Response>(path, { query, raw: true, timeoutMs: 60000 })
    const disposition = res.headers.get('Content-Disposition') ?? ''
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)
    const name = match ? decodeURIComponent(match[1]) : fallbackName
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = name
    document.body.appendChild(a)
    a.click()
    a.remove()
    setTimeout(() => URL.revokeObjectURL(url), 2000)
  },
  async blobUrl(path: string, query?: RequestOptions['query']) {
    const res = await request<Response>(path, { query, raw: true, timeoutMs: 60000 })
    return URL.createObjectURL(await res.blob())
  },
}

export function errorMessage(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.code === 'validation_failed' && e.problem.errors) {
      return Object.values(e.problem.errors).flat().join(' ')
    }
    return e.message
  }
  return 'Đã xảy ra lỗi không mong muốn.'
}
