/**
 * Token persistence. Access token lives in memory + sessionStorage (survives reload in the same tab only);
 * refresh token in localStorage so staff stay signed in on a shared POS device until they log out.
 * Trade-off documented in docs/assumptions-and-decisions.md (no HttpOnly cookie because API and SPA may be on
 * different origins in Azure; mitigated by strict CSP-friendly SPA, short access token TTL and rotation + reuse detection).
 */
export interface AuthPayload {
  accessToken: string
  accessTokenExpiresAtUtc: string
  refreshToken: string
  refreshTokenExpiresAtUtc: string
  user?: unknown
}

const ACCESS_KEY = 'brewline.at'
const REFRESH_KEY = 'brewline.rt'

export const tokenStore = {
  accessToken: sessionStorage.getItem(ACCESS_KEY) as string | null,
  refreshToken: localStorage.getItem(REFRESH_KEY) as string | null,
  save(p: AuthPayload) {
    this.accessToken = p.accessToken
    this.refreshToken = p.refreshToken
    sessionStorage.setItem(ACCESS_KEY, p.accessToken)
    localStorage.setItem(REFRESH_KEY, p.refreshToken)
  },
  clear() {
    this.accessToken = null
    this.refreshToken = null
    sessionStorage.removeItem(ACCESS_KEY)
    localStorage.removeItem(REFRESH_KEY)
  },
}
