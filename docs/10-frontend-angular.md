# 10 — Frontend: Angular store feature in ta3lom-web

**Revision:** S1 draft r1 · 29 September 2026 (reconciled with `_changes-r1.md` and [11 — API contract](11-api-contract.md))

Implementation guide for the store UI inside `repos/ta3lom-web` (Angular 22, standalone, zoneless,
signals, `httpResource()`, Signal Forms, `@angular/localize` with `/ar` and `/en` builds, prerendered
static output). It covers the customer store (FR-S01–S35), the account (FR-S21–S28), main-site
integration (FR-S19, FR-S82) and the store admin (FR-S70–S79).

**Governing documents:** `ta3lom-web/CLAUDE.md` (non-negotiables, conventions, rendering), [03 — Business rules](03-business-rules.md)
(source of truth), [04 — Functional requirements](04-functional-requirements.md), ADR-S04 (customer identity),
ADR-S05 (hosted payments), ADR-S07 (store inside ta3lom-web), main ADR-002 (prerendering) and ADR-006 (token in memory).
**Visual reference:** `design/ta3lom-store-prototype.html` (approved). Do not redesign. Screens, components
and copy below are mapped from it. Where this guide differs from the prototype, it says why.

Endpoint paths and payloads follow [11 — API contract](11-api-contract.md), which is authoritative. Paths
in this guide are relative to `https://store-api.ta3lom.sa/v1`.

---

## 1. Store-specific reading of the non-negotiables

| CLAUDE.md rule | What it means in the store |
|---|---|
| 1 Logical CSS | The prototype's `top:` offsets become `inset-block-start`. Arrows (`→`) are mirrored with the existing `.arr` rule. Nothing uses `left`/`right` |
| 2 Arabic first | Screens are built and reviewed in `/ar` first. Numerals are Western everywhere, including money, seat counts, OTP and order references |
| 3 `pick()` | Every `*_ar`/`*_en` pair from the store API goes through `pick()`. The API contract uses `titleAr`/`titleEn`, like the main API |
| 4 SEO | Every store route calls `SeoService.set()`. Non-public routes set `robots: 'noindex'` (§14) |
| 5 One `<h1>` | Product title, page title, or a state-specific heading on the thank-you page. Card titles are `<h3>`, section titles `<h2>` |
| 6, 12 Alt | Covers and cause images are `MediaAsset`s with `altAr`/`altEn` (DDL `ck_media_alt`). The prototype's generated SVG art is placeholder only and is not ported |
| 7 44 px | The prototype's `.stepper.sm` (32 × 34), `.share` (34 × 34) and `.cartbtn` (40 × 40) keep their visual size but get a 44 × 44 hit area (§12.3) |
| 8 Reduced motion | Goal-bar fill, card hover lift, cart-badge bump, accordion auto-advance and count-up all stop |
| 9 Token in a signal | `StoreAuth` and `StaffAuth` hold access tokens in signals. Refresh tokens are HttpOnly cookies set by the store API |
| 10 Budgets | Store routes are lazy. The only code added to the initial bundle is the header badge and `CartSummary` (§15) |
| 11 No `bypassSecurityTrust*` | Cause updates and product bodies arrive as sanitised `bodyArHtml`/`bodyEnHtml` and are bound with `[innerHTML]` inside `.prose` |
| 13 Categories are data | Filter chips come from `GET /v1/catalog/categories?type=…`. No category array is hard-coded (the prototype's `CATS` is demo data) |
| 15 CSP | `connect-src` gains `https://store-api.ta3lom.sa`. Payment integration must not add inline script (§8) |
| Selector prefix | `ta-` for every store component, for example `<ta-money>` (canonical decision r1-17) |

---

## 2. Routes

All paths live under the locale base (`/ar/…`, `/en/…`). Render modes are declared in
`src/app/app.routes.server.ts`. Store routes are lazy-loaded from `features/store/store.routes.ts`.

### 2.1 Route table

| Path | Component | Render | Guard | Data source (store API `/v1`) | SEO |
|---|---|---|---|---|---|
| `/store` | `StoreHomePage` | Prerender | — | `GET /catalog/home` (featured, 4 causes, 6 courses, 3 sponsorable, books, impact numbers) · live: `GET /catalog/live?slugs=…` | Indexed. `WebPage` + `ItemList` JSON-LD |
| `/store/courses` | `CatalogPage` (`data.type = 'course'`) | Prerender | — | `GET /catalog/products?type=course&category=&sort=&page=` · `GET /catalog/categories?type=course` · live | Indexed. Canonical without query string |
| `/store/books` | `CatalogPage` (`'book'`) | Prerender | — | same, `type=book` | Indexed |
| `/store/sponsor` | `CatalogPage` (`'sponsor'`) | Prerender | — | `GET /catalog/products?sponsorable=true` | Indexed |
| `/store/give` | `CatalogPage` (`'cause'`) | Prerender | — | `GET /catalog/products?type=cause` | Indexed |
| `/store/search` | `SearchPage` | Client | — | `GET /catalog/products?q=…` (FR-S03) | `noindex` |
| `/store/p/:slug` | `ProductPage` | Prerender (slugs from `routes.txt`, §2.3) | — | `GET /catalog/products/{slug}` (build + hydration reload) · live: `GET /catalog/live?slugs={slug}` · `GET /catalog/products/{slug}/feed` · `GET /catalog/products/{slug}/updates` | Indexed. `Course` / `Book` / `Product` + `DonateAction` JSON-LD, OG image = cover (FR-S09) |
| `/store/cart` | `CartPage` | Client | — | `CartStore` (`GET/POST/PATCH/DELETE /cart…`) | `noindex` |
| `/store/checkout` | `CheckoutPage` | Client | `cartNotEmptyGuard` | `CartStore`, `StoreAuth`, `GET /catalog/config`, `GET /me/addresses`, `POST /checkout/quote`, `POST /checkout` | `noindex` |
| `/store/orders/:ref/thanks` | `ThanksPage` | Client | `thanksAccessGuard` | `GET /orders/{ref}/status` with the bearer, or `?t={statusToken}` (polling, §9); `POST /orders/{ref}/retry-payment` | `noindex` |
| `/store/account` | `AccountShell` → redirects to `courses` | Client | `customerGuard` | `GET /me` | `noindex` |
| `/store/account/courses` | `MyCoursesPage` | Client | (parent) | `GET /me/enrolments` | `noindex` |
| `/store/account/books` | `MyBooksPage` | Client | (parent) | `GET /me/books` (grants + shipments), `POST /me/downloads/{grantId}/link` | `noindex` |
| `/store/account/giving` | `MyGivingPage` | Client | (parent) | `GET /me/giving` | `noindex` |
| `/store/account/orders` | `MyOrdersPage` | Client | (parent) | `GET /me/orders?page=`, document downloads | `noindex` |
| `/store/account/profile` | `ProfilePage` | Client | (parent) | `GET/PATCH /me`, `/me/addresses`, `GET /me/export`, `DELETE /me`, `POST /auth/logout-all` | `noindex` |
| `/store/card/:token` | `DedicationCardPage` | Client | — | `GET /cards/{token}` | `noindex` (FR-S60) |
| `/store/login` | `StoreLoginPage` | Client | `guestOnlyGuard` | `POST /auth/otp/request`, `POST /auth/otp/verify` | `noindex` |
| `/admin/store/**` | see §16 | Client | `staffGuard` + role | `/v1/admin/…` | `noindex`, excluded from sitemap |

`/store/search` is not in the brief's route list. It is added for FR-S03, because the prototype's
search spans all product types and none of the typed catalogue routes can hold it (open point 1).

### 2.2 Route configuration

```ts
// src/app/features/store/store.routes.ts
export const STORE_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./home/store-home.page'), title: storeTitle('home') },
  { path: 'courses', loadComponent: () => import('./catalog/catalog.page'), data: { type: 'course' } },
  { path: 'books',   loadComponent: () => import('./catalog/catalog.page'), data: { type: 'book' } },
  { path: 'sponsor', loadComponent: () => import('./catalog/catalog.page'), data: { type: 'sponsor' } },
  { path: 'give',    loadComponent: () => import('./catalog/catalog.page'), data: { type: 'cause' } },
  { path: 'search',  loadComponent: () => import('./catalog/search.page') },
  { path: 'p/:slug', loadComponent: () => import('./product/product.page'), providers: [ProductData] },
  { path: 'cart',     loadComponent: () => import('./cart/cart.page') },
  { path: 'checkout', loadComponent: () => import('./checkout/checkout.page'), canActivate: [cartNotEmptyGuard] },
  { path: 'orders/:ref/thanks', loadComponent: () => import('./thanks/thanks.page'), canActivate: [thanksAccessGuard] },
  { path: 'account', canActivate: [customerGuard], loadChildren: () => import('./account/account.routes') },
  { path: 'card/:token', loadComponent: () => import('./card/dedication-card.page') },
  { path: 'login', loadComponent: () => import('./login/store-login.page'), canActivate: [guestOnlyGuard] },
  { path: '**', loadComponent: () => import('./ui/store-not-found.page') },
];

// src/app/app.routes.ts (excerpt)
{ path: 'store', loadChildren: () => import('./features/store/store.routes').then((m) => m.STORE_ROUTES) },
{ path: 'admin/store', loadChildren: () => import('./features/admin/store/admin-store.routes') },
```

`withComponentInputBinding()` is already on, so `:slug`, `:ref` and `:token` arrive as `input.required<string>()`.

### 2.3 Prerendered slugs: extending `scripts/routes.mjs` → `routes.txt`

Store product slugs are added to the main site's **existing** prerender route source. No second
mechanism is introduced (canonical decision r1-16). `scripts/routes.mjs` already runs as `prebuild`,
queries the main API for every slug and writes `routes.txt`. It gains a store section that runs after
the main-site section:

```js
// scripts/routes.mjs (excerpt; the store section is appended to the existing route list)
const STORE_API = process.env.STORE_API_URL ?? 'https://store-api.ta3lom.sa/v1';

async function storeRoutes() {
  const routes = ['/store', '/store/courses', '/store/books', '/store/sponsor', '/store/give'];
  for (let page = 1; ; page++) {
    const res = await fetch(`${STORE_API}/catalog/products?fields=slug&page=${page}&pageSize=200`,
                            { signal: AbortSignal.timeout(15_000) });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const body = await res.json();                         // { items: [{ slug }], total, page, pageSize }
    routes.push(...body.items.map(({ slug }) => `/store/p/${slug}`));
    if (page * body.pageSize >= body.total) break;
  }
  return routes;
}

try {
  routes.push(...(await storeRoutes()));                   // same format as the existing main-site entries
} catch (err) {
  console.error(`routes.mjs: store API unreachable, build stopped (${err.message})`);
  process.exit(1);                                         // never ship a stale or empty product list silently
}
```

- **Build failure.** If the store API is unreachable, returns an error or times out, **the build
  fails**. The nightly rebuild (main FR-62) and the `repository_dispatch` rebuild then stop with an
  error. Hostinger keeps serving the last good build, and the failed workflow alerts the team. A
  prerender with no products is never published.
- **What the slug query returns.** `GET /v1/catalog/products?fields=slug` returns **published**
  products only (RUL-02). Archived products drop out of the prerender, and their URLs fall back to
  client rendering. That page shows "no longer available" with a link to the account, because
  archived products stay visible in history (RUL-02).
- **Server routes.** `app.routes.server.ts` gives `store`, the four catalogues and `store/p/:slug`
  `RenderMode.Prerender`, with `fallback: PrerenderFallback.Client` on `store/p/:slug`. The slug
  parameters come from `routes.txt`, exactly as for the main site's slug routes. The server-route
  file makes no network call of its own. Every other `store/**` route and all of `admin/store/**` are
  `RenderMode.Client`.
- **Static hosting fallback.** `deploy/.htaccess` must rewrite a missing `/{ar,en}/store/p/*` to the
  locale's client-render entry (`index.csr.html`). Without it, a product published after the last
  build returns Hostinger's 404.
- **Tests.** A CI unit test runs `routes.mjs` against a mocked store API. It checks that store routes
  are appended, and that an unreachable API makes the script exit non-zero.

### 2.4 Freshness: nightly rebuild and live figures

| Content | Freshness for a browser | Freshness for a crawler / no-JS | Mechanism |
|---|---|---|---|
| Titles, descriptions, curriculum, SEO tags | Next page load (hydration reload, `core/refresh-after-hydration.ts`) | Next rebuild | Main FR-62 nightly rebuild (06:00 Riyadh) and the `repository_dispatch` "content-published" trigger |
| Price, compare-at price, stock, goal, donor count, offer open/closed | Next page load. Never taken from the build snapshot (FR-S07) | Build-time value, which is correct as of the last rebuild | `…/live` resources, browser-only (§5.2) |
| New product | Immediate through the client fallback | Next rebuild | `PrerenderFallback.Client` + `.htaccess` |

The store resources that are prerendered (`CatalogData.list`, `ProductData.product`, `HomeData.home`)
register with the existing `refresh-after-hydration` mechanism, as every public resource does.
Live resources are never requested at build time, so they cannot be replayed from `ng-state`.

The store admin shows the same wording rule as the main admin: "Prices, stock and goals update on the
next page load. Titles, descriptions and search-engine text update after the next site rebuild (last
build: …)." Whether publishing a store product triggers `repository_dispatch` is open point 3.

---

## 3. Folder structure

```
src/app/
├── core/
│   └── store/
│       ├── store-api-url.ts          STORE_API_URL InjectionToken (env: https://store-api.ta3lom.sa/v1)
│       ├── store-api.types.ts        generated from Ta3lom.Store.Contracts OpenAPI (npm run types:pull:store)
│       ├── money.ts                  Money = string decimal; toHalalas / fromHalalas / compare (no floats)
│       ├── store-format.ts           formatMoney, formatCount, formatDate (Intl, nu-latn, Asia/Riyadh)
│       ├── store-auth.ts             StoreAuth: customer session (token signal, silent refresh, OTP)
│       ├── staff-auth.ts             StaffAuth: staff session (token signal, password + TOTP)
│       ├── cart-summary.ts           CartSummary: header count (tiny, in the initial bundle)
│       ├── cart.store.ts             CartStore: full cart, optimistic updates (lazy: injected by store features only)
│       ├── cart.calc.ts              cartTotals, mergeOrAppend (RUL-14 mirror), limits (RUL-12/18)
│       ├── store-config.ts           GET /catalog/config: dedication templates, min gift, shipping fee, presets, payment methods
│       ├── store-problem.ts          ProblemDetails → typed StoreProblem, code → $localize message
│       ├── interceptors/
│       │   ├── store-credentials.interceptor.ts
│       │   ├── store-auth.interceptor.ts
│       │   └── store-refresh.interceptor.ts
│       └── guards/
│           ├── customer.guard.ts     customerGuard, guestOnlyGuard, thanksAccessGuard
│           ├── cart-not-empty.guard.ts
│           └── staff.guard.ts        staffGuard, staffRoleGuard(...roles)
├── layout/header/
│   └── cart-badge.ts                 <ta-cart-badge> (reads CartSummary)
├── features/
│   ├── store/
│   │   ├── store.routes.ts
│   │   ├── home/          store-home.page.ts, home.data.ts
│   │   ├── catalog/       catalog.page.ts, search.page.ts, catalog.data.ts
│   │   ├── product/       product.page.ts, product.data.ts, course-body.ts, book-body.ts, cause-body.ts
│   │   ├── cart/          cart.page.ts
│   │   ├── checkout/      checkout.page.ts, checkout.form.ts, checkout.data.ts
│   │   ├── thanks/        thanks.page.ts, thanks.data.ts
│   │   ├── account/       account.routes.ts, account-shell.ts,
│   │   │                  courses/, books/, giving/, orders/, profile/   (each: *.page.ts + *.data.ts)
│   │   ├── card/          dedication-card.page.ts, card.data.ts
│   │   ├── login/         store-login.page.ts
│   │   └── ui/            store presentational components (§11) — no HTTP, no router injection
│   │       ├── buy-box/   buy-box.ts, buy-box.calc.ts, buy-box.calc.spec.ts
│   │       ├── goal-bar.ts, product-card.ts, book-card.ts, donation-card.ts, sponsor-card.ts, …
│   │       └── otp-sign-in.ts
│   └── admin/
│       └── store/
│           ├── admin-store.routes.ts, admin-store-shell.ts, staff-login.page.ts
│           ├── dashboard/ products/ offers/ causes/ cause-updates/ stock/ orders/ payment-review/
│           ├── refunds/ shipments/ beneficiaries/ allocation/ zakat/ reports/ customers/
│           └── settings/ templates/ audit/                      (each: *.page.ts + *.data.ts)
└── shared/ui/                        only generic, presentational pieces promoted from features/store/ui
                                      (candidates: stepper, amount-chips, segment, empty-state, timeline)
```

**Dependency direction.** `features → shared → core`. `layout` imports from `core` only.

- `CartSummary` and `CartStore` live in `core/store` because the header (in `layout`) needs the count
  and nothing may import from `features`.
- `features/admin/store` must not import from `features/store`. Anything both need (money
  formatting, `ta-money`, `ta-goal-bar`) moves to `shared/ui` (presentational) or `core/store` (logic).
- ESLint boundaries: add the element types `core/store` (core), `features/store` and
  `features/admin/store` (features). The existing rule already forbids `features → features`.

---

## 4. Core services

### 4.1 Store API URL, types and errors

```ts
export const STORE_API_URL = new InjectionToken<string>('STORE_API_URL');
// app.config.ts: { provide: STORE_API_URL, useValue: environment.storeApiUrl }
```

- **Types.** `core/store/store-api.types.ts` is generated from the store API's OpenAPI document
  (`Ta3lom.Store.Contracts`). Add the script `types:pull:store`. Never hand-write DTOs.
- **Money.** Money is `string` in every DTO (`"350.00"`). It is never converted to a floating-point
  number for arithmetic (§12). Display-only ratios such as "equals 0.29 seats" are the one exception.
- **Errors.** The API returns RFC 9457 ProblemDetails with a stable `code` (for example
  `line.campaign_closed`, `cart.limit_total`, `otp.rate_limited`, `checkout.price_changed`).
  `store-problem.ts` maps a known `code` to a `$localize` message (`@@store.error.<code>`). An unknown
  code falls back to the server's localised `detail`, and then to a generic message.

### 4.2 `StoreAuth` — customer session (ADR-S04, RUL-60)

The access token (15 min, audience `store-customer`) lives only in a signal. The rotating refresh
token lives in the HttpOnly cookie `t3s_rt` (path `/v1/auth`, 60 days), which JavaScript cannot read.

```ts
@Injectable({ providedIn: 'root' })
export class StoreAuth {
  private readonly http = inject(HttpClient);
  private readonly api = inject(STORE_API_URL);
  private readonly token = signal<string | null>(null);          // never persisted (ADR-006)
  readonly customer = signal<CustomerSummary | null>(null);
  readonly isSignedIn = computed(() => this.token() !== null);

  private inflight: Promise<boolean> | null = null;
  private silentTried = false;
  private timer?: ReturnType<typeof setTimeout>;

  accessToken(): string | null { return this.token(); }

  /** Silent refresh via the cookie. Single-flight per tab, serialised across tabs. */
  refresh(): Promise<boolean> {
    this.inflight ??= crossTabLock('t3s-refresh', () =>
      firstValueFrom(this.http.post<TokenResponse>(`${this.api}/auth/refresh`, null, { context: skipRefresh() }))
        .then((r) => (this.apply(r), true))
        .catch((e) => (isRefreshRace(e) ? this.retryOnce() : (this.clear(), false))),   // auth.refresh_race: retry once
    ).finally(() => (this.inflight = null));
    return this.inflight;
  }

  /** Guards call this: at most one silent attempt per page load when there is no token. */
  async ensureSession(): Promise<boolean> {
    if (this.token()) return true;
    if (this.silentTried) return false;
    this.silentTried = true;
    return this.refresh();
  }

  requestOtp(mobile: string, locale: 'ar' | 'en') {                 // 202 { expiresInSeconds }
    return this.http.post<{ expiresInSeconds: number }>(`${this.api}/auth/otp/request`, { mobile, locale });
  }

  async verifyOtp(mobile: string, code: string): Promise<CartNotice[]> {
    const r = await firstValueFrom(this.http.post<VerifyResponse>(`${this.api}/auth/otp/verify`, { mobile, code }));
    this.apply(r);
    return r.cartNotices ?? [];                                      // RUL-17 merge notices
  }

  async signOut(everywhere = false) {                                // FR-S28
    const url = everywhere ? `${this.api}/auth/logout-all` : `${this.api}/auth/logout`;
    await firstValueFrom(this.http.post(url, null)).catch(() => undefined);
    this.clear();
  }

  private apply(r: TokenResponse) {
    this.token.set(r.accessToken);
    this.customer.set(r.customer);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.refresh(), Math.max(5, r.expiresIn - 60) * 1000);
  }
  private clear() { clearTimeout(this.timer); this.token.set(null); this.customer.set(null); }

  /** auth.refresh_race: another tab rotated the cookie a moment ago; the new cookie is already in the jar. */
  private retryOnce(): Promise<boolean> {
    return firstValueFrom(this.http.post<TokenResponse>(`${this.api}/auth/refresh`, null, { context: skipRefresh() }))
      .then((r) => (this.apply(r), true))
      .catch(() => (this.clear(), false));
  }
}

/** Web Locks keep two tabs from presenting the same rotating refresh token at once. */
function crossTabLock<T>(name: string, fn: () => Promise<T>): Promise<T> {
  return typeof navigator !== 'undefined' && navigator.locks ? navigator.locks.request(name, fn) : fn();
}
```

- **When the silent refresh runs.** It runs only when it is needed:
  - in `customerGuard` and `thanksAccessGuard`;
  - on the checkout page, to prefill details;
  - once at idle for the header badge (§15);
  - when an interceptor gets a 401.
- **Cross-tab rotation.** Refresh-token reuse detection revokes the whole family
  (`auth.refresh_reused`). The Web Lock serialises refreshes across tabs, and because the cookie jar
  is shared, the second tab presents the already-rotated cookie. A concurrent rotation that still
  happens is reported as `auth.refresh_race`, and the client retries it once.
- **Merge notices.** `cartNotices` are shown after sign-in, for example
  `cart.merge_dropped_owned_course`: "A course you already own was removed from your cart" (RUL-17).
- **Blocked accounts.** A blocked customer gets `auth.account_blocked` on verify and on checkout
  (RUL-61). The UI shows a message with the association's contact details.

### 4.3 `StaffAuth` — staff session (RUL-62, FR-S70)

`StaffAuth` is separate from `StoreAuth`. It has its own token signal (audience `store-staff`, 10 min)
and its own refresh cookie, **`t3s_srt`** (`Path=/v1/admin/auth`, HttpOnly, 8 h). Its endpoints are
`POST /v1/admin/auth/refresh` and `POST /v1/admin/auth/logout`. Staff sign in with email and password,
then a mandatory TOTP step:

1. `POST /v1/admin/auth/login {email, password}` → `{ mfaToken, totpEnrolled }`. The lockout matches FR-53.
2. If `totpEnrolled = false`: `POST /v1/admin/auth/totp/enrol {mfaToken}` → `{ otpauthUri, qrPngDataUrl }`.
   Show the QR code as `<img>` (a data URL; no QR library, no `innerHTML`) and the secret for manual entry.
3. `POST /v1/admin/auth/totp/verify {mfaToken, code}` → access token plus the staff refresh cookie.

`StaffAuth.roles` is a computed signal read from the token claims. `StaffAuth.can(policy)` maps the
roles to the API's policy tags (§16.1). Together they drive what the UI shows. The API
enforces every permission on its own. The main-site admin editor session (main API) and the store staff
session are unrelated: signing in to one does not sign in to the other.

### 4.4 HTTP interceptors

They are registered in `app.config.ts` after the main-site interceptors, in this order. They act only on
URLs that start with `STORE_API_URL`, so main API calls are untouched.

```ts
/** 1. Cookies (t3s_cart, t3s_rt) must travel cross-origin to store-api.ta3lom.sa. */
export const storeCredentialsInterceptor: HttpInterceptorFn = (req, next) =>
  req.url.startsWith(inject(STORE_API_URL)) ? next(req.clone({ withCredentials: true })) : next(req);

/** 2. Bearer token: staff token for /v1/admin/**, customer token for everything else. */
export const storeAuthInterceptor: HttpInterceptorFn = (req, next) => {
  const api = inject(STORE_API_URL);
  if (!req.url.startsWith(api)) return next(req);
  const token = req.url.startsWith(`${api}/admin`) ? inject(StaffAuth).accessToken() : inject(StoreAuth).accessToken();
  return next(token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req);
};

/** 3. On 401: refresh once, then retry the original request once. Never loops. */
export const storeRefreshInterceptor: HttpInterceptorFn = (req, next) => {
  const api = inject(STORE_API_URL);
  if (!req.url.startsWith(api) || req.context.get(SKIP_REFRESH)) return next(req);
  const isAdmin = req.url.startsWith(`${api}/admin`);
  const auth: { refresh(): Promise<boolean>; accessToken(): string | null } = isAdmin ? inject(StaffAuth) : inject(StoreAuth);
  return next(req).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse) || err.status !== 401) return throwError(() => err);
      return from(auth.refresh()).pipe(
        switchMap((ok) => {
          const t = auth.accessToken();
          return ok && t ? next(req.clone({ setHeaders: { Authorization: `Bearer ${t}` } })) : throwError(() => err);
        }),
      );
    }),
  );
};
```

The auth services are injected at the top of each function, inside the injection context, never in
`catchError`. Refresh calls carry `SKIP_REFRESH`, so a failed refresh cannot recurse. Registration
order: `withInterceptors([...main, storeCredentialsInterceptor, storeRefreshInterceptor, storeAuthInterceptor])`.
Angular runs interceptors in order, so `storeAuthInterceptor` sits closest to the backend and the retry
issued from `storeRefreshInterceptor` sets its own header.

### 4.5 Guards

| Guard | Behaviour |
|---|---|
| `customerGuard` | `await StoreAuth.ensureSession()`. If it fails, return `UrlTree('/store/login', { returnUrl })` |
| `thanksAccessGuard` | `ensureSession()`. If that fails, the route is allowed when the URL carries `?t=` (the per-order status token). Otherwise it redirects to login with `returnUrl` (§9) |
| `guestOnlyGuard` | A signed-in customer on `/store/login` is redirected to `returnUrl` or `/store/account` |
| `cartNotEmptyGuard` | Loads the cart once. An empty cart redirects to `/store/cart` (as the prototype does) |
| `staffGuard` | `StaffAuth.ensureSession()`, otherwise `/admin/store/login` |
| `staffRoleGuard(...roles)` | The route's `data.roles` must intersect `StaffAuth.roles()`. Otherwise it shows a 403 screen inside the admin shell (no redirect loop) |

`returnUrl` is validated as a same-origin path starting with `/{locale}/store` or `/{locale}/admin/store`
to prevent open redirects.

---

## 5. Data services

Each feature has one `*.data.ts` with `httpResource()` for its reads, as the main site does.
Components read `value()`, `isLoading()` and `error()`, and never call `HttpClient`. Mutations (cart,
checkout, profile, admin commands) are methods on the same data service or on `CartStore`, and use
`HttpClient` there.

### 5.1 Overview

| File | Resources (reads) | Mutations |
|---|---|---|
| `core/store/store-config.ts` | `config` → `GET /catalog/config` (dedication templates, free-text flag, `minGiftAmount`, `shippingFlatFee`, `cartMaxTotal`, `cartMaxLines`, presets, `paymentMethods`, VAT flag) | — |
| `core/store/cart-summary.ts` | `summary` → `GET /cart/summary` → `{ lineCount, grandTotal }` (browser only) | `refresh()` |
| `home/home.data.ts` | `home` → `GET /catalog/home` · `live` → `GET /catalog/live?slugs=…` (browser only) | — |
| `catalog/catalog.data.ts` | `categories`, `list` (signals: `type`, `category`, `sort`, `q`, `page`), `live` | — |
| `product/product.data.ts` | `product`, `live`, `feed`, `updates`, `related` | — (adding goes through `CartStore`) |
| `checkout/checkout.data.ts` | `addresses` → `GET /me/addresses` (signed in only) | `quote()` → `POST /checkout/quote`, `placeOrder()` → `POST /checkout` (with `acceptTerms: true`) |
| `thanks/thanks.data.ts` | `status` (polled, bearer or status token) | `retryPayment()` → `POST /orders/{ref}/retry-payment` |
| `account/courses/courses.data.ts` | `enrolments` → `GET /me/enrolments` | `openCourse(id)` → `POST /me/enrolments/{id}/launch` (LMS URL, FR-S23) |
| `account/books/books.data.ts` | `books` → `GET /me/books` | `download(grantId)` → `POST /me/downloads/{grantId}/link` (§10) |
| `account/giving/giving.data.ts` | `giving` → `GET /me/giving` (totals, lines, unit status counts, pooled shares, cause updates) | — |
| `account/orders/orders.data.ts` | `orders` → `GET /me/orders?page=`, `GET /me/orders/{ref}` | `GET /me/documents/{number}` (PDF) |
| `account/profile/profile.data.ts` | `me`, `addresses` | `save()`, `addAddress()`, `setDefault()`, `deleteAddress()`, `exportData()`, `deleteAccount()`, `signOutEverywhere()` |
| `card/card.data.ts` | `card` → `GET /cards/{token}` | — |

### 5.2 `ProductData`: prerendered content plus live figures (FR-S07)

`ProductData` is provided on the `p/:slug` route (`providers: [ProductData]`), so each product page
gets its own instance.

```ts
@Injectable()
export class ProductData {
  private readonly api = inject(STORE_API_URL);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));
  readonly slug = toSignal(inject(ActivatedRoute).paramMap.pipe(map((p) => p.get('slug') ?? '')), { initialValue: '' });

  /** Content: rendered at build time, embedded in ng-state, reloaded after hydration (refresh-after-hydration). */
  readonly product = httpResource<ProductDetail>(() => (this.slug() ? `${this.api}/catalog/products/${this.slug()}` : undefined));

  /** Live figures: browser only, so they never enter the prerendered snapshot. Batch endpoint (11 §2.5) with one slug. */
  readonly liveRes = httpResource<{ items: ProductLive[]; asOf: string }>(() =>
    this.browser && this.slug() ? `${this.api}/catalog/live?slugs=${encodeURIComponent(this.slug())}` : undefined,
  );
  readonly live = computed(() => this.liveRes.value()?.items.find((i) => i.slug === this.slug()) ?? null);

  /** Ownership (RUL-13): signed-in customers only, from their enrolments; never part of the public live call. */
  private readonly auth = inject(StoreAuth);
  readonly enrolments = httpResource<Enrolment[]>(() =>
    this.browser && this.auth.isSignedIn() && this.product.value()?.type === 'course' ? `${this.api}/me/enrolments` : undefined,
  );
  readonly owned = computed(() =>
    (this.enrolments.value() ?? []).some((e) => e.product.slug === this.slug() && e.status !== 'revoked' && e.status !== 'expired'),
  );

  /** Below the fold, browser only; loaded when the @defer block enters the viewport. */
  readonly feed = httpResource<GiftFeedItem[]>(() =>
    this.browser && this.feedWanted() ? `${this.api}/catalog/products/${this.slug()}/feed` : undefined,
  );
  readonly feedWanted = signal(false);

  // Display values: the live figure wins; the build-time figure is the fallback (and what crawlers see).
  readonly price = computed(() => this.live()?.price ?? this.product.value()?.price ?? null);
  readonly formats = computed(() => mergeFormats(this.product.value()?.formats, this.live()?.formats));
  readonly goal = computed(() => this.live()?.goal ?? this.product.value()?.goal ?? null);
  readonly sponsorship = computed(() => this.live()?.sponsorship ?? this.product.value()?.sponsorship ?? null);
  readonly canGive = computed(() => (this.live()?.goal ?? this.live()?.sponsorship)?.isOpen ?? true); // false after ends_at (RUL-09)
}
```

`ProductLive` is one item of the 11 §2.5 response (`{ items, asOf }`, generated types): `slug`, `price` and
`compareAtPrice` (courses), `formats[{ kind, price, available }]` (books; `available = stock_on_hand −
stock_reserved`, print only, `null` for digital), `sponsorship{ fundedUnits, targetUnits, percent, donorCount,
isOpen }` (course or book with an offer) and `goal{ raised, target, percent, donorCount, daysLeft, isOpen }`
(causes); goal and sponsorship figures come from `v_goal` with seeds included. Unknown or unpublished slugs are omitted. The response is public
and cached for 15 s, so it carries nothing customer-specific. Offer terms that are not live figures (seat price,
end date, partial allowed) come from the product DTO (11 §2.4).

- **No layout shift.** The live value replaces text in an element that is already rendered with the
  build-time value, so its size is the same. A product page that loads from the client fallback (no
  build value) reserves the buy box height with a skeleton that matches `.buybox`.
- **Ownership (RUL-13).** `owned` is not part of the live call. For a signed-in customer on a course page,
  `ProductData` reads `GET /me/enrolments` (11 §5.3) and sets `owned` when an enrolment for the slug is not
  `revoked` or `expired`. The course buy box then shows "You're enrolled — open in My courses" in place of
  "Add to cart". A guest finds out when the line is added: the API rejects it (`cart.course_owned`, or
  `cart.duplicate_course` when already in the cart as `buy`, 11 §1.6), which stays the final guard, as does
  the merge on sign-in (RUL-17).

### 5.3 `CatalogData`

`type` comes from route data. `category`, `sort` and `page` are two-way bound to query parameters
(`?category=tech&sort=newest`). The prerendered HTML is the unfiltered first page. Filtered views are
client-side, and their canonical is the unfiltered route. `live` batches the slugs on the current page
into one `GET /catalog/live?slugs=a,b,c` so that cards show current goals and prices.

---

## 6. `CartStore` (server is the source of truth)

The cart lives on the server (RUL-11, FR-S17). A guest is identified by the `t3s_cart` cookie, a customer
by the token. `CartStore` shows a local view with optimistic edits. Every response from the server
**replaces** the local view.

```ts
@Injectable({ providedIn: 'root' })
export class CartStore {
  private readonly http = inject(HttpClient);
  private readonly api = inject(STORE_API_URL);
  private readonly summary = inject(CartSummary);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));

  /** The server cart. Read only in the browser (cart routes are client-rendered). */
  private readonly remote = httpResource<Cart>(() => (this.browser ? `${this.api}/cart` : undefined));

  /** Local view: resets to the server value whenever `remote` changes; optimistic edits write here. */
  readonly lines = linkedSignal<readonly CartLine[]>(() => this.remote.value()?.lines ?? []);

  readonly count = computed(() => this.lines().length);                       // badge counts lines, as the prototype does
  readonly purchases = computed(() => this.lines().filter((l) => l.kind === 'buy'));
  readonly gifts = computed(() => this.lines().filter((l) => l.kind !== 'buy'));
  readonly hasPrint = computed(() => this.purchases().some((l) => l.formatKind === 'print'));
  readonly hasGifts = computed(() => this.gifts().length > 0);
  readonly totals = computed(() => cartTotals(this.lines(), this.remote.value()?.shippingFlatFee ?? '0.00'));
  readonly warnings = computed(() => this.remote.value()?.warnings ?? []);    // e.g. campaign ended (RUL-09)
  readonly loading = this.remote.isLoading;
  readonly pending = signal(0);
  readonly lastProblem = signal<StoreProblem | null>(null);

  private queue: Promise<unknown> = Promise.resolve();

  add(draft: CartLineDraft): Promise<boolean> {
    return this.mutate(
      (lines) => mergeOrAppend(lines, optimisticLine(draft)),                  // mirrors RUL-14 so the count does not jump
      () => this.http.post<Cart>(`${this.api}/cart/lines`, draft),
    );
  }

  setQuantity(lineId: string, quantity: number): Promise<boolean> {
    const q = Math.min(Math.max(Math.trunc(quantity) || 1, 1), 999);         // RUL-12 bounds; print 1–20 enforced by the server
    return this.mutate(
      (lines) => lines.map((l) => (l.id === lineId ? withQuantity(l, q) : l)),
      () => this.http.patch<Cart>(`${this.api}/cart/lines/${lineId}`, { quantity: q }),
    );
  }

  remove(lineId: string): Promise<boolean> {
    return this.mutate(
      (lines) => lines.filter((l) => l.id !== lineId),
      () => this.http.delete<Cart>(`${this.api}/cart/lines/${lineId}`),
    );
  }

  /** After sign-in (the server merged the guest cart, RUL-17) or after checkout. */
  reload(): void { this.remote.reload(); }

  /** Mutations are serialised, so a slow response can never overwrite a newer one. */
  private mutate(apply: (l: readonly CartLine[]) => readonly CartLine[], call: () => Observable<Cart>): Promise<boolean> {
    const run = async (): Promise<boolean> => {
      const before = this.lines();
      this.lines.set(apply(before));
      this.summary.setCount(this.lines().length);
      this.pending.update((n) => n + 1);
      try {
        const cart = await firstValueFrom(call());
        this.remote.value.set(cart);                                           // server wins; linkedSignal resets `lines`
        this.summary.setCount(cart.lines.length);
        this.lastProblem.set(null);
        return true;
      } catch (e) {
        this.lines.set(before);                                                // roll back
        this.summary.setCount(before.length);
        this.lastProblem.set(toStoreProblem(e));                               // shown as an error toast
        return false;
      } finally {
        this.pending.update((n) => n - 1);
      }
    };
    const p = this.queue.then(run, run);
    this.queue = p;
    return p;
  }
}
```

- **Totals.** `cartTotals()` (in `cart.calc.ts`) adds halalas as integers: `purchases` (VAT-inclusive,
  RUL-27), `shipping` (flat fee only if a `print` line exists, RUL-26) and `gifts`, with
  `grand = purchases + shipping + gifts` (RUL-31). It returns string decimals. The cart page shows these
  values. **Checkout reprices on the server (RUL-15)**, and the checkout summary shows the server's
  numbers.
- **Client-side pre-checks.** These exist only to give the user an immediate message. The server
  decides every one of them again:
  - amount lines of at least `min_gift_amount` or the cause minimum (RUL-12);
  - at most 50 lines, and a cart total of at most `cart_max_total` (RUL-18), with the "contact the
    association" prompt;
  - course `buy` qty = 1, digital qty = 1, print qty 1–20;
  - a course already in the cart as `buy` (RUL-13, the prototype's "already in your cart" toast).
- **Stepper debounce.** The cart page debounces stepper clicks by 300 ms, so that five taps become one
  `PATCH`.
- **Toasts.** "Added to cart" or "Added to your giving", with a "View cart" link (from the prototype).
  They are announced through the existing polite live region. Errors use `role="alert"`.

`CartSummary` (initial bundle, used by the header):

```ts
@Injectable({ providedIn: 'root' })
export class CartSummary {
  private readonly api = inject(STORE_API_URL);
  private readonly auth = inject(StoreAuth);
  private readonly enabled = signal(false);                    // switched on after first idle, browser only
  private readonly res = httpResource<{ lineCount: number; grandTotal: Money }>(() =>
    this.enabled() ? `${this.api}/cart/summary` : undefined);
  readonly count = linkedSignal(() => this.res.value()?.lineCount ?? 0);
  setCount(n: number) { this.count.set(n); }
  refresh() { this.res.reload(); }

  /** Called by <ta-cart-badge> in afterNextRender + requestIdleCallback. */
  async start() {
    await this.auth.ensureSession();                           // a signed-in visitor's cart needs the bearer (§15)
    this.enabled.set(true);
  }
}
```

### 6.1 Restored lines after an expired or failed order (RUL-29)

When an order expires, or its payment fails for good, the server puts its lines back into the
customer's active cart and skips any product that is already there. The customer does not rebuild
the cart by hand. Because the server is the source of truth, the client only has to **reload**. It
never re-adds lines itself, which could duplicate them.

| When the client learns of it | What the client does |
|---|---|
| The thank-you page sees `status: expired` (or a final failure) with `cartRestored: true` (11 §6.3) | `CartStore.reload()` and `CartSummary.refresh()` once. The page shows "Your items are back in your cart" with a "Go to cart" button |
| The customer left before expiry and the `ExpireUnpaidOrders` job restored the lines later | Nothing special. The next page load fetches `/cart/summary` (badge) and `/cart` (cart page) fresh |
| The app stayed open in another tab | `CartStore` reloads when the cart or checkout route is entered (the page constructors call `reload()` whenever the store was already loaded) and when the tab becomes visible again on those routes |

A restored course `buy` line is still subject to RUL-13 at the next checkout. If the late capture is
later accepted from `payment_review`, the course is already owned, and checkout rejects the duplicate
(open point 15).

---

## 7. Checkout (FR-S30–S33)

### 7.1 Screen

The screen follows the prototype's checkout: steps (Cart → Details & payment → Confirmation), then
"Your details", shipping (only with print), payment, terms, and the pay button showing the total, with
the order summary alongside.

- **Identity (RUL-25).** If the customer is not signed in, the mobile field is followed by the inline
  `<ta-otp-sign-in>` (§7.3). The details fields unlock after verification. Name and email are
  prefilled from `/me`.
- **Anonymity (RUL-23).** The "Hide my name in the donor list (anonymous)" checkbox is shown only when
  `CartStore.hasGifts()`. It defaults to `customer.anonymous_default`.
- **Shipping (RUL-26).** The shipping section is shown only when `CartStore.hasPrint()`. A saved
  address can be picked from `/me/addresses`, or a new one entered. The city list comes from the API
  (Q-06 decides coverage), not from the prototype's five hard-coded cities.
- **Payment methods (RUL-36).** mada, Apple Pay, Visa/Mastercard and STC Pay. The prototype's Tabby and
  SADAD options are **not** rendered in S1. BNPL is S2 (FR-S39), and SADAD is not in `payment.method`.
  Whether the selection is passed to the gateway or the hosted page shows its own picker depends on
  Q-01 (§8).
- **Terms (FR-S30).** The terms checkbox is required. Submitting sends `acceptTerms: true`, and the
  server records `orders.terms_accepted_at` (§7.4).
- **Blocked customers (RUL-61).** A blocked customer cannot check out. The API answers
  `auth.account_blocked`, and the page shows the contact message in place of the form.

### 7.2 Signal Form

```ts
// features/store/checkout/checkout.form.ts
const SA_MOBILE = /^05[0-9]{8}$/;                  // RUL-21, RUL-60 (as typed; normalised to +9665XXXXXXXX before sending)
const SHORT_ADDRESS = /^[A-Z]{4}[0-9]{4}$/;        // Saudi National Address short code, DDL customer_address.short_address
const POSTAL_CODE = /^[0-9]{5}$/;

export interface CheckoutModel {
  name: string; mobile: string; email: string; anonymous: boolean;
  address: { savedId: string | null; recipientName: string; recipientMobile: string; city: string;
             district: string; shortAddress: string; street: string; buildingNo: string; postalCode: string };
  saveAddress: boolean;
  method: 'mada' | 'applepay' | 'card' | 'stcpay';
  acceptTerms: boolean;                            // must be true; sent as acceptTerms: true (FR-S30)
}

export function checkoutForm(model: WritableSignal<CheckoutModel>, cart: CartStore) {
  return form(model, (p) => {
    required(p.name, { message: $localize`:@@store.checkout.name.required:أدخل الاسم الكامل` });
    minLength(p.name, 2);
    required(p.mobile);
    pattern(p.mobile, SA_MOBILE, { message: $localize`:@@store.checkout.mobile.invalid:رقم الجوال بصيغة 05XXXXXXXX` });
    required(p.email);
    email(p.email, { message: $localize`:@@store.checkout.email.invalid:بريد إلكتروني غير صحيح` });

    // Shipping only when the cart has a print line (RUL-26), and only for a new address.
    applyWhen(p.address, () => cart.hasPrint() && model().address.savedId === null, (a) => {
      required(a.recipientName);
      required(a.recipientMobile);
      pattern(a.recipientMobile, SA_MOBILE);
      required(a.city);
      required(a.shortAddress, { message: $localize`:@@store.checkout.shortAddress.required:أدخل العنوان الوطني المختصر` });
      pattern(a.shortAddress, SHORT_ADDRESS, { message: $localize`:@@store.checkout.shortAddress.invalid:أربعة أحرف ثم أربعة أرقام، مثل RRRD2929` });
      pattern(a.postalCode, POSTAL_CODE);
    });

    validate(p.acceptTerms, ({ value }) =>
      value() ? null : customError({ kind: 'terms', message: $localize`:@@store.checkout.terms.required:يلزم قبول الشروط وسياسة الاسترجاع` }));
  });
}
```

- **Input normalisation, before validation.** Arabic-Indic digits (`٠١٢٣٤٥٦٧٨٩`) become Western digits,
  spaces are stripped from mobiles, and the short address is upper-cased. This is done with a
  `normaliseDigits()` transform on input, because users on Arabic keyboards type Arabic-Indic digits.
  Mobiles are sent as E.164 (`+9665XXXXXXXX`).
- **Fields.** Mobile, short address, postal code and email use `dir="ltr"` and `inputmode` (`tel`,
  `numeric`, `email`), with `autocomplete` values `tel-national`, `postal-code`, `email`, `name`.
- **Errors.** Each error is tied to its field with `aria-describedby`. On submit, focus moves to the
  first invalid field. The Signal Forms API is written as of Angular 22. Adjust it to the installed
  version's names.

### 7.3 OTP sign-in (`<ta-otp-sign-in>`, FR-S21, RUL-60)

The same component is used inline at checkout and on `/store/login`.

| Step | UI | API (11 §4) | Errors |
|---|---|---|---|
| `mobile` | Mobile field (`05XXXXXXXX`) and a "Send code" button | `POST /auth/otp/request {mobile, locale}` → `202 { expiresInSeconds: 300 }`. The response is the same whether or not an account exists | `otp.mobile_invalid`. `429 otp.rate_limited` with `retryAfterSeconds`: show "Try again in N minutes" (limits: 3 per mobile per 15 min, 10 per IP per hour) |
| `code` | 6-digit field (`autocomplete="one-time-code"`, `inputmode="numeric"`, `dir="ltr"`, `maxlength=6`). "Code sent to 05•••••67". Buttons to resend and to change the number. A 5:00 countdown | `POST /auth/otp/verify {mobile, code}` → access token, `customer`, `cartNotices` | `otp.invalid` (`attemptsLeft`). `otp.too_many_attempts` after 5 wrong codes ("Request a new code"). `otp.expired`. `auth.account_blocked` (RUL-61): "This account can't sign in. Contact the association" |
| `done` | Emits `signedIn`. At checkout the details fields unlock; on the login page the app navigates to `returnUrl` | `CartStore.reload()` (RUL-17 merge), `CartSummary.refresh()` | — |

- **Focus and announcements.** When the step changes, focus moves to the code field. "Code sent" is
  announced politely. Errors use `role="alert"`.
- **Resending.** The resend button becomes active after 60 s. This is UI pacing only. The server
  enforces the RUL-60 limits.
- **Pasting.** A pasted code works. The form submits automatically once six digits are present.
- **Beneficiaries (RUL-52).** A beneficiary's `customer` row is created at allocation. The invitation
  SMS links to `/store/login?invite=1`, which only changes the page copy ("a seat has been allocated
  to you"). The first OTP login simply signs them in, and the sponsored course is already under
  "My courses".

### 7.4 Placing the order

The pay button is enabled only when the form is valid. That includes the terms checkbox, which must
be ticked: the request always sends **`acceptTerms: true`**, and the server stores
`orders.terms_accepted_at` from it (FR-S30, E-Commerce Law evidence). If the flag were ever missing,
the server would answer `checkout.terms_not_accepted`, and the form would show the terms error.

```ts
// features/store/checkout/checkout.data.ts
@Injectable({ providedIn: 'root' })
export class CheckoutData {
  private readonly http = inject(HttpClient);
  private readonly api = inject(STORE_API_URL);
  private readonly locale: 'ar' | 'en' = inject(LOCALE_ID).startsWith('en') ? 'en' : 'ar';
  private idempotencyKey = crypto.randomUUID();

  quote() { return this.http.post<Quote>(`${this.api}/checkout/quote`, {}); }         // repriced totals (RUL-15)

  async placeOrder(model: CheckoutModel, quote: Quote): Promise<CheckoutOutcome> {
    const body: CheckoutRequest = {
      contact: { fullName: model.name, email: model.email },
      isAnonymous: model.anonymous,
      shippingAddressId: model.address.savedId,
      shippingAddress: model.address.savedId ? null : toAddressInput(model.address),   // only with a print line
      saveAddress: model.saveAddress,
      paymentMethod: model.method,
      acceptTerms: true,                          // the form cannot submit unless the checkbox is ticked
      expectedGrandTotal: quote.grandTotal,       // server answers 409 checkout.price_changed if it moved
      locale: this.locale,
    };
    try {
      const r = await firstValueFrom(this.http.post<CheckoutResponse>(`${this.api}/checkout`, body,
        { headers: { 'Idempotency-Key': this.idempotencyKey } }));
      return { kind: 'created', reference: r.reference, statusToken: r.statusToken, redirectUrl: r.payment.redirectUrl };
    } catch (e) {
      const p = toStoreProblem(e);
      // A 4xx answer created no order: the next attempt is a new request and gets a new key.
      // A network error or 5xx keeps the key, so a retry cannot create a second order.
      if (p.status >= 400 && p.status < 500) this.idempotencyKey = crypto.randomUUID();
      switch (p.code) {
        case 'checkout.price_changed': return { kind: 'priceChanged', quote: p.extensions['quote'] as Quote };
        case 'checkout.lines_invalid': return { kind: 'linesInvalid', lines: p.extensions['lines'] as LineError[] };
        case 'checkout.gateway_unavailable': return { kind: 'gatewayDown', reference: p.extensions['reference'] as string,
                                                      statusToken: p.extensions['statusToken'] as string };
        default: return { kind: 'error', problem: p };
      }
    }
  }
}
```

| Outcome | UI |
|---|---|
| `created` | Hand off to payment (§8). The pay button stays disabled and shows "Redirecting to the gateway…" (prototype copy) |
| `priceChanged` | The summary switches to the server quote, and changed lines are highlighted with "Prices changed since you added these items. Review the new total." The customer must press Pay again (FR-S31) |
| `linesInvalid` | Each failing line is listed with its reason: campaign ended, out of stock, already owned, `cart.course_in_unpaid_order` (RUL-13), or over a limit. A link goes to the cart. Nothing is charged (RUL-24) |
| `gatewayDown` | The order exists but has no payment session. Navigate to `/store/orders/{ref}/thanks?t={statusToken}`, which offers "Try again" (§9) |
| `error` | An error summary at the top of the form, focused. `auth.account_blocked` (RUL-61) shows the blocked-account message |

The pay button is disabled while the request is in flight. The Idempotency-Key (stored server-side in
`idempotency_key`, scope `checkout:{customerId}`) protects against retries after a network failure.

---

## 8. Payment on the client (depends on Q-01)

Card data never touches ta3lom.sa or the store API (RUL-32). The browser is never the proof of payment
(RUL-33). `POST /checkout` returns `payment.redirectUrl`. An SDK variant is possible only if Q-01 and a
later ADR choose it.

| Integration | Client behaviour | CSP / platform impact | Recommendation |
|---|---|---|---|
| Hosted page (`redirectUrl`) | `location.assign(redirectUrl)` to the gateway's hosted page, which offers mada, Apple Pay, cards and STC Pay on the gateway's domain. The gateway returns to the return URL | None. Top-level navigation is not restricted by `connect-src` or `script-src`. If the gateway needs a form POST, add its host to `form-action` | **S1 default.** Keeps the hash-only `script-src` and PCI SAQ-A scope. Apple Pay domain validation stays on the gateway's domain |
| SDK sheet | Lazy-load the gateway SDK in a `@defer (on interaction)` block and open its Apple Pay or card sheet on ta3lom.sa | Needs the gateway's script and frame hosts in `script-src`/`frame-src`, `TrustedFrameUrl` for any iframe, the Apple Pay domain-association file at `https://ta3lom.sa/.well-known/apple-developer-merchantid-domain-association`, and a new ADR (ADR-010/011 process) | Only if the association wants in-page Apple Pay after Q-01 |

**Return URL.** It is `https://ta3lom.sa/{locale}/store/orders/{ref}/thanks?t={statusToken}`, built by
the store API. The gateway may add its own parameters (`id`, `status`, `message`). The thank-you page
treats `t` only as a credential for the status call. It ignores every other parameter, and the status
it shows always comes from `GET /v1/orders/{ref}/status`.

**Retry (RUL-37, FR-S36).** When the status is `payment_failed` and `canRetry` is true (the order is
still before `payment_due_at`), the thank-you page offers "Try again". This calls
`POST /v1/orders/{ref}/retry-payment {paymentMethod}`, which creates a new `payment` row on the same
order, and hands off in the same way. Retry needs the customer bearer, so if only the status token is
available the page first asks the customer to sign in (OTP) and then retries.

---

## 9. Thank-you page: polling the order status (FR-S35)

After the gateway redirect the page loads fresh, so the access token is no longer in memory.

**How the status call is authorised (canonical decision r1-15).**

- `GET /v1/orders/{ref}/status` accepts the customer's **bearer** or the per-order **status token**
  (`?t=`, an HMAC of the order id returned by `POST /v1/checkout`). It never answers to the reference
  alone.
- The route guard `thanksAccessGuard` first tries `StoreAuth.ensureSession()` (silent refresh through
  `t3s_rt`). If that works, the page uses the bearer, as it normally will because the customer signed
  in at checkout.
- If the refresh fails, the page continues with the `t` query parameter. With the token only, the
  response omits dedications and document numbers. The page then shows the status and a "Sign in to
  see your receipt" link.
- With neither a session nor a token, the guard redirects to `/store/login?returnUrl=…`.

```ts
// features/store/thanks/thanks.data.ts
const TERMINAL = new Set<OrderStatus>(['paid', 'fulfilled', 'payment_failed', 'payment_review', 'expired',
                                        'cancelled', 'refunded', 'partially_refunded']);
const FAST_MS = 2_000, FAST_FOR_MS = 60_000, SLOW_MS = 10_000;    // cadence from 11 §6.3
const GIVE_UP_MS = 10 * 60_000;                                   // reconciliation bound (RUL-38)

@Injectable()
export class ThanksData {
  private readonly api = inject(STORE_API_URL);
  private readonly auth = inject(StoreAuth);
  private readonly cart = inject(CartStore);
  private readonly summary = inject(CartSummary);
  readonly ref = signal('');
  readonly statusToken = signal<string | null>(null);              // from ?t=, used only without a session
  private readonly startedAt = Date.now();
  readonly gaveUp = signal(false);

  readonly status = httpResource<OrderStatusDto>(() => {
    if (!this.ref()) return undefined;
    const url = `${this.api}/orders/${this.ref()}/status`;
    return this.auth.isSignedIn() || !this.statusToken() ? url : { url, params: { t: this.statusToken()! } };
  });
  readonly isTerminal = computed(() => TERMINAL.has(this.status.value()?.status as OrderStatus));

  constructor() {
    // Poll with the documented cadence until a terminal state, or give up after 10 minutes.
    effect((onCleanup) => {
      if (this.status.isLoading() || this.isTerminal() || this.gaveUp()) return;
      const elapsed = Date.now() - this.startedAt;
      if (elapsed > GIVE_UP_MS) { this.gaveUp.set(true); return; }
      const id = setTimeout(() => this.status.reload(), elapsed < FAST_FOR_MS ? FAST_MS : SLOW_MS);
      onCleanup(() => clearTimeout(id));
    });
    // Lines restored after final expiry or failure (RUL-29): refresh the cart once.
    effect(() => {
      if (this.status.value()?.cartRestored) untracked(() => { this.cart.reload(); this.summary.refresh(); });
    });
    // Poll at once when the tab becomes visible again (mobile browsers pause timers).
    fromEvent(document, 'visibilitychange').pipe(takeUntilDestroyed(), filter(() => !document.hidden && !this.isTerminal()))
      .subscribe(() => this.status.reload());
  }
}
```

The status response is `{ reference, status, grandTotal, paidAt, paymentDueAt, payment, canRetry,
cartRestored?, summary }` (11 §6.3). The effect re-runs whenever a reload completes. Errors (network or
5xx) keep polling at the same cadence.

| Status | `<h1>` and content |
|---|---|
| `pending_payment`, still polling | "Confirming your payment…" with a spinner that respects reduced motion, and the order reference. The live region announces only the final state |
| `paid` / `fulfilled` | The prototype's success view. The heading is "May it be accepted, {first name}" (تقبّل الله منك) if the order has gifts, otherwise "Thank you, {first name}". Below it: the receipt summary from `summary`, a note that gifts get a separate donation receipt, dedication card previews (bearer only), and the actions My account, Download receipt PDF and Share |
| `payment_failed` | "Payment didn't go through". A retry button when `canRetry` (RUL-37) |
| `payment_review` | "We're reviewing your payment". Finance will contact you, and nothing is charged twice (RUL-35). This also covers a capture that arrived after the order expired |
| `expired` / `cancelled` | "This order has expired". When `cartRestored` is true: "Your items are back in your cart" with a "Go to cart" button (RUL-29). Otherwise a link back to the store |
| `gaveUp` (10 min, not terminal) | "This is taking longer than usual. We'll send an SMS as soon as the payment is confirmed." The reference is shown, with a "Check again" button |

---

## 10. Account pages (FR-S22–S28)

The layout is the prototype's `.acct`: a side menu with a name/mobile header and counts per tab, and
the content alongside. On narrow screens the side menu becomes a horizontal scrolling tab list.
The tabs are Angular Aria tabs linked to the child routes.

| Tab | Content | Notes |
|---|---|---|
| My courses | `.lib` rows: title, progress bar (`progress_pct`), Start/Resume, certificate link when `completed`, "Access being prepared" when `pending_sync` (RUL-42) | Start/Resume calls `openCourse()`, then `location.assign(lmsUrl)` |
| My books | Digital: "Download" plus "N of 5 downloads left" (RUL-41). Print: shipment status tag (`preparing` / `shipped` / `delivered` / `returned`) with carrier and tracking number | Download: `POST /me/downloads/{grantId}/link` returns `{ url, expiresAt, remaining }`. The link (`/v1/downloads/{token}`) is valid 10 minutes, and the browser navigates to it, so large PDFs stream as a file response rather than an XHR blob. Errors: `download.limit_reached`, `download.revoked`, `download.link_expired` |
| My giving | Stat tiles: total given, seats & copies sponsored, dedications sent. For each gift line: for `units` sponsorships, `<ta-seat-map>` with awaiting / allocated / completed counts plus group and region; for causes, `<ta-timeline-feed>` of delivery updates; for pooled shares, "Pooled — 0.34 of 1 seat" and conversion status; for a swept remainder, the RUL-46 note | Donors never see beneficiary identities (RUL-50). The API returns counts, group and region only |
| Orders & receipts | `.tbl` table: reference (mono), date, lines, total, status, and document buttons (invoice INV/CN, donation receipt DR/DRC) | Documents open as PDF downloads from signed URLs |
| Profile | Name, email, locale, default anonymity, marketing consent. Saved addresses (add, edit, set default, delete). "Export my data" (JSON download). "Delete my account" in a confirmation dialog that states what is kept for 10 years (RUL-61). "Sign out on all devices" (FR-S28) | Delete requires typing the confirmation word. Focus returns to the trigger |

The empty state ("Nothing here yet", with a "Browse the store" button) comes from the prototype's
`emptyAcct()`.

---

## 11. Component catalogue

Selector prefix `ta-` (CLAUDE.md), one component per file, `input()`/`output()`/`model()` functions,
`ChangeDetectionStrategy.OnPush` (zoneless). Components in `features/store/ui` are presentational: they
receive data and emit events, and the pages call `CartStore` and the data services. This keeps them
testable, and lets the admin preview reuse them after they are promoted to `shared/ui`.

| Component | Prototype source | Inputs | Outputs | Accessibility |
|---|---|---|---|---|
| `ta-product-card` | `courseCard()` `.pcard` | `product: CourseCardVm`, `live?: LiveFigures` | — | The whole card is one `<a>`. Title `<h3>`. Badge (`best`/`new`/`urgent`) is text, not colour only. Meta icons `aria-hidden`. Struck price has a visually hidden "was" label |
| `ta-book-card` | `bookCard()` | `product: BookCardVm`, `live?` | — | "from" prefix when there is more than one format. Cover `<img>` alt from the asset |
| `ta-donation-card` | `donCard()` `.dcard` | `product: CauseCardVm`, `live?`, `minAmount: Money` | `quickGive: Money`, `share: void` | Title link and image link point to the same URL; the image link is `tabindex="-1"` and `aria-hidden` to avoid a duplicate stop. The quick-give field has a label ("Donation amount") and inline validation (≥ cause minimum, RUL-12). Share button (44 × 44) has an `aria-label`. Hidden when `acceptingLines = false`, replaced by "Campaign ended" |
| `ta-sponsor-card` | `sponsorCard()` | `product: SponsorCardVm`, `live?` | — | CTA "Sponsor seats" / "Sponsor copies" links to `/store/p/{slug}?mode=sponsor` |
| `ta-goal-bar` | `goalBlock()` `.goal` | `raised`, `target` (money or units), `unit: 'money' \| 'units'`, `unitKind: 'seat' \| 'copy'`, `donors`, `endsAt`, `reached`, `size: 'md' \| 'lg'` | — | `role="progressbar"`, `aria-valuemin=0`, `aria-valuemax=100`, `aria-valuenow` = the RUL-08 percentage (floor, capped at 100), `aria-valuetext` = "12,400 of 30,000 SAR raised". The visible percentage may exceed 100. The fill animates once on first view; under `prefers-reduced-motion` it is set without a transition. "Ends 12 October 2026" / "Goal reached" / "Campaign ended" |
| `ta-stepper` | `stepper()` `.stepper` | `value = model<number>()`, `min`, `max`, `label`, `size: 'md' \| 'sm'` | (model) | Native `<input type="number" inputmode="numeric">` with a visible or `aria-label` label. Buttons are labelled "Decrease quantity" / "Increase quantity" (the prototype used "-"/"+") and disabled at the bounds. `sm` keeps its visual size with a 44 px hit area |
| `ta-amount-chips` | `.chips` presets | `presets: number[]`, `value = model<number \| null>()`, `unit: 'currency' \| 'count'` | (model) | Radio group (Angular Aria) with arrow-key navigation. The selected chip uses `aria-checked`, not colour only |
| `ta-mode-segment` | `.seg` | `options: {value,label}[]`, `value = model()`, `kind: 'tabs' \| 'radio'` | (model) | `tabs` for Enrol/Sponsor and Buy/Sponsor (FR-S10/11), because the panel content changes; `radio` for units/amount and sadaqa/zakat |
| `ta-dedication-form` | `dedBlock()` `.dedic` | `templates: DedicationTemplate[]`, `freeTextEnabled: boolean`, `value = model<DedicationDraft \| null>()` | `validityChange: boolean` | Toggle is a checkbox "Give on behalf of someone". Fields: recipient name (2–60), recipient mobile (`05XXXXXXXX`), sender name (optional), message template (`<select>` from `dedication_templates`) or free text (≤ 140, with a character counter announced at 20 left) (RUL-21). Nested Signal Form. The error summary is announced when the user tries to add |
| `ta-buy-box` | `buyBox()` `.buybox` | `variant: 'course' \| 'book' \| 'cause'`, `product`, `live`, `settings`, `owned` | `add: CartLineDraft`, `buyNow: CartLineDraft`, `share: void` | See §11.1. Totals in an `aria-live="polite"` `aria-atomic="true"` region. Buttons: "Add to cart" / "Add to giving", then "Buy now" / "Give now" |
| `ta-sticky-buy-bar` | `.stickybar` | `total: Money`, `label`, `tone: 'buy' \| 'give'` | `activate: void` | Mobile only. It is a duplicate control, so it is hidden from assistive technology when the buy box is in view (`IntersectionObserver`), otherwise it is a labelled region |
| `ta-cart-line` | `lineHtml()` `.cline` | `line: CartLineVm`, `editable: boolean` | `quantityChange: number`, `remove: void` | "Remove" says which line (`aria-label="Remove {title}"`). Focus moves to the next line or to the group heading after removal. The dedication row reads "On behalf of {name} · card to 05…" |
| `ta-order-summary` | `summary()` `.sumbox` | `totals: CartTotals`, `showCta: boolean`, `giftsNote: boolean` | — | Rows: Purchases (VAT incl.), Shipping, Gifts & sponsorships, Total. The total sits in a polite live region. Payment marks are text, not images |
| `ta-checkout-steps` | `steps()` `.steps` | `current: 1 \| 2 \| 3` | — | `<ol>` with `aria-current="step"`. Completed steps have visually hidden "(completed)" text |
| `ta-seat-map` | account `.seatmap` + `.legend` | `awaiting`, `allocated`, `completed`, `unitKind` | — | A grid of up to 60 squares, then "+N". `role="img"` with an `aria-label` that states the three counts. The legend is real text. Colours come from tokens and are backed by the legend (no colour-only meaning) |
| `ta-timeline-feed` | `.feed` | `items: {text: string, at: string, meta?: string}[]` | — | `<ol>` with `<time datetime>`. Used for latest gifts (anonymised per RUL-23), cause updates and unit events |
| `ta-share-dialog` | `shareP()` modal | `url`, `title` | `closed` | Native `<dialog>` opened with `showModal()`, which provides the focus trap and Esc. Focus starts on the heading and returns to the trigger. Uses `navigator.share` when available. Fallback links: WhatsApp, X, Telegram, email, "Copy link" (Clipboard API, announced "Link copied"). The URL is `https://ta3lom.sa/{locale}/store/p/{slug}` (FR-S08); the prototype's `store.ta3lom.sa` host is not used |
| `ta-money` | `money()` + `.cur` | `amount: Money`, `strike?: boolean` | — | Renders `1,750` + `<span class="cur">ر.س</span>` / `SAR` (§12). `dir="ltr"` on the number span only, so it is not reordered in RTL |
| `ta-dedication-card` | `.gcard` | `card: DedicationCardVm` | — | Used on the thank-you page and `/store/card/:token`. Never shows the amount (RUL-22). The logo mark is decorative |
| `ta-receipt` | `.receipt` | `order: MyOrderDto` | — | A definition list for the header (order no., date, payment), then the lines |
| `ta-otp-sign-in` | — (new) | `mode: 'inline' \| 'page'`, `invite: boolean` | `signedIn: void` | §7.3 |
| `ta-empty-state` | `.empty` | `icon`, `heading`, `headingLevel` | — | Projected text and actions. The heading level is an input so that the page's heading outline stays correct |
| `ta-cart-badge` (layout) | `.cartbtn` | — (reads `CartSummary`) | — | §15 |

The `money` pipe (`{{ amount | money }}`) exists for attribute and text contexts where a component
cannot be used (for example `aria-valuetext`). It shares its formatter with `ta-money`.

### 11.1 `ta-buy-box` behaviour

The pure calculation lives in `buy-box.calc.ts` and is unit-tested (§17). The component holds its
state in signals (`mode`, `by`, `qty`, `amount`, `format`, `intent`, `dedication`).

| Variant / mode | Controls | Line produced (`CartLineDraft`) | Rules |
|---|---|---|---|
| Course · Enrol myself | Price (+ compare-at, struck through only if higher, RUL-04), inclusions from the product | `kind: 'buy', mode: 'units', quantity: 1` | RUL-13. If `owned`, show "Open in My courses" |
| Course · Sponsor seats | Group statement, goal bar, units/amount segment, presets **1/5/10/25**, stepper, "N seats × 350 SAR", or amount presets 50/100/250/500 + input with "Your share equals 0.29 seats" | `kind: 'sponsor', mode: 'units', quantity: N` or `mode: 'amount', amount` | The Sponsor tab appears only if the offer is open (FR-S10). The amount tab appears only if `allowPartial`. Amount ≥ `min_gift_amount` (RUL-12). The pooling explanation is stated (RUL-45/46) |
| Book · Buy a copy | Format radio (print "Ships in 3–5 working days" / digital "Instant download"), stepper for print only | `kind: 'buy', mode: 'units', formatId, quantity` (digital always 1) | Print 1–20. "Only N left" when `available` is low. Out of stock disables Add (RUL-30) |
| Book · Sponsor copies | As for courses, presets **10/25/50/100** | `kind: 'sponsor', …` | FR-S12 |
| Cause | Units/amount segment when `unitAmount` exists; unit presets **1/2/5/10** + stepper; amount presets **50/100/250/500** + input; amount only for the general fund; sadaqa/zakat segment only if `zakatEligible`, with the zakat notice | `kind: 'donate', mode, quantity \| amount, intent` | RUL-07, RUL-19, FR-S13. Amount ≥ the cause's `min_amount` |
| Any gift | `ta-dedication-form` | `dedication: { toName, toMobile (E.164), fromName, templateKey, text }` | RUL-21. Dedicated lines never merge (RUL-14) |

The "Sponsor now" link in the product page's sponsorable banner switches the buy box to sponsor mode,
scrolls it into view (smooth scrolling only without reduced motion) and moves focus to the segment.
`?mode=sponsor` does the same on load (the prototype used `?m=sponsor`).

---

## 12. Money, numbers and dates

### 12.1 `Money` arithmetic

```ts
// core/store/money.ts — string decimals in, string decimals out; integers (halalas) in between
export type Money = string; // "350.00"
export const toHalalas = (m: Money): number => {
  const neg = m.startsWith('-'); const [i, f = ''] = (neg ? m.slice(1) : m).split('.');
  const h = Number(i) * 100 + Number((f + '00').slice(0, 2));
  return neg ? -h : h;
};
export const fromHalalas = (h: number): Money =>
  `${h < 0 ? '-' : ''}${Math.trunc(Math.abs(h) / 100)}.${String(Math.abs(h) % 100).padStart(2, '0')}`;
export const times = (m: Money, qty: number): Money => fromHalalas(toHalalas(m) * qty);
export const sumMoney = (ms: readonly Money[]): Money => fromHalalas(ms.reduce((s, m) => s + toHalalas(m), 0));
/** Display only (FR-S12): 100 / 350 → "0.29". Never fed back into an amount. */
export const unitsEquivalent = (amount: Money, unit: Money): string => (toHalalas(amount) / toHalalas(unit)).toFixed(2);
```

Integers are exact up to 2^53 halalas, which is far above the 100,000 SAR cart limit (RUL-18).

### 12.2 Formatting

| What | Format | Example (ar) | Example (en) |
|---|---|---|---|
| Money | `Intl.NumberFormat('en', { minimumFractionDigits: whole ? 0 : 2, maximumFractionDigits: 2 })` on the integer and fraction parts, then the currency label | `1,750 ر.س` · `12.50 ر.س` | `1,750 SAR` · `12.50 SAR` |
| Counts | `Intl.NumberFormat('en')` | `1,840` | `1,840` |
| Dates | `Intl.DateTimeFormat(locale === 'ar' ? 'ar-SA-u-ca-gregory-nu-latn' : 'en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'Asia/Riyadh' })` | `12 أكتوبر 2026` | `12 October 2026` |
| Order reference, OTP, mobiles, short address | Monospace (`.mono`), `dir="ltr"` | `T3-26-004187` | same |

- **Where the formatters live.** They are in `core/store/store-format.ts` and are used by `ta-money`,
  the `money` pipe and a `storeDate` pipe. Angular's `DatePipe`/`DecimalPipe` locale data is not
  relied on for Arabic numerals.
- **Dates.** Campaign end dates are shown in Asia/Riyadh, because RUL-09 is evaluated there.
- **Whole amounts.** The prototype prints `12.5` for 12.50 SAR. The build prints `12.50`, and whole
  amounts stay without decimals, as in the prototype.

### 12.3 Hit areas

Controls that are visually smaller than 44 px (`.stepper.sm` buttons, `.share`, `.cartbtn`) get an
`::after` hit area with `inset: -6px` (or a minimum `inline-size`/`block-size` of 44 px), checked by the
existing Playwright tap-target test.

---

## 13. Styling

- **Tokens.** Reuse `src/styles/tokens.css`. Components use semantic tokens only (`--c-*`). The
  prototype's store CSS uses palette tokens (`--gold`, `--gray-2`, `--brand-900`) and literals
  (`#F6F1E2`, `#FCFAF3`, `#E6F4EC`, `#FBEAEA`, `#fff`, `rgba(19,101,120,.12)`). Each becomes a semantic
  token below, with light and dark values in `tokens.css`. The contrast check
  (`scripts/contrast-check.mjs`) must cover every new text/surface pair.
- **Placement.** Styles are component-scoped. `.price`, `.cur` and `.tag` variants, which are used
  across many store components, go into `src/styles/store.css`, imported once by the global styles.
- **Direction.** Logical properties only. `top:88px` on `.buybox` becomes `inset-block-start`. Transforms
  that suggest direction (the book cover's `rotate(-1.5deg)`) are mirrored with `:dir(rtl)`.
- **Motion.** Every transition and animation (`.bar i` width, `.pcard`/`.dcard` hover lift, `bump`) is
  wrapped in `@media (prefers-reduced-motion: no-preference)`.

### 13.1 New semantic tokens

| Token | Light value (from the prototype) | Used by |
|---|---|---|
| `--c-gift-surface` / `--c-gift-text` / `--c-gift-border` | `#F6F1E2` / `--gold-700` / `--gold` | Giving cart group header, selected chips, sponsorable banner, `.tag.gold` |
| `--c-dedication-surface` / `--c-dedication-border` | `#FCFAF3` / `--gold-200` (dashed) | `.dedic` |
| `--c-success-surface` / `--c-success-text` | `#E6F4EC` / `--green` | `.tag.green`, completed seats legend, success tick |
| `--c-urgent-surface` / `--c-urgent-text` | `#FBEAEA` / `--danger` | `urgent` badge (the prototype used an inline style) |
| `--c-goal-track` / `--c-goal-fill` / `--c-goal-deadline` | `--gray-2` / `--grad` / `--danger` | `ta-goal-bar` |
| `--c-calc-surface` / `--c-calc-text` | `--tint` / `--brand-900` | `.calc` explanations, `.unit` value strip |
| `--c-count-badge-bg` / `--c-count-badge-text` | `--gold` / `#fff` | Header cart badge |
| `--c-seat-completed` / `--c-seat-allocated` / `--c-seat-awaiting` | `--green` / `--gold-200` / `--gray-2` | `ta-seat-map` and its legend |
| `--c-card-surface` / `--shadow-card-hover` | `#fff` / `0 24px 50px rgba(19,101,120,.12)` | `.pcard`, `.dcard`, `.buybox`, `.cgroup`, `.sumbox` |
| `--c-share-surface` | `rgba(255,255,255,.92)` | Share button over images |

Map these to existing semantic tokens wherever an equivalent already exists. Add a new token only
when none fits.

### 13.2 Prototype classes to port

| Prototype class | Becomes |
|---|---|
| `.pcard`, `.bookcov`, `.ribbon`, `.meta`, `.price`, `.cur` | `ta-product-card`, `ta-book-card` (`.price`/`.cur` global) |
| `.dcard`, `.unit`, `.qamt`, `.share` | `ta-donation-card`, `ta-sponsor-card` |
| `.goal`, `.bar`, `.nums`, `.pct`, `.sub .end` | `ta-goal-bar` |
| `.buybox`, `.blk`, `.seg`, `.opt`, `.chips`, `.stepper`, `.calc`, `.total`, `.dedic` | `ta-buy-box` and its children |
| `.stickybar` | `ta-sticky-buy-bar` |
| `.kv`, `.curric`, `.hero-media`, `.person` | Product page bodies |
| `.feed` | `ta-timeline-feed` |
| `.cgrid`, `.cgroup`, `.cline`, `.sumbox`, `.steps`, `.paygrid` | Cart and checkout pages, `ta-cart-line`, `ta-order-summary`, `ta-checkout-steps` |
| `.success`, `.ok`, `.receipt`, `.gcard`, `.mk` | Thank-you page, `ta-receipt`, `ta-dedication-card` |
| `.acct`, `.acct-side`, `.who`, `.lib`, `.libi`, `.seatmap`, `.legend`, `.tbl`, `.tblwrap` | Account pages, `ta-seat-map` |
| `.empty` | `ta-empty-state` |
| `.cartbtn`, `.bump`, `.backsite` | `ta-cart-badge`. `.backsite` is not needed, because the store is inside the main site |

The header, footer, hero, band, accordion, pillars, stats and `.sec-head` already exist on the main
site. The store reuses them and does not copy them.

---

## 14. Internationalisation

### 14.1 Message ids

Every string has an explicit id: `@@store.<area>.<element>[.<state>]`, for example
`store.nav.store`, `store.buybox.addToCart`, `store.buybox.addToGiving`, `store.buybox.shareEquals`,
`store.cart.empty.title`, `store.cart.group.giving`, `store.checkout.mobile.hint`, `store.thanks.title.gift`,
`store.goal.reached`, `store.goal.ended`, `store.account.giving.pooled`. Admin strings use
`store.admin.<screen>.<element>`. Error codes use `store.error.<api code>`. Source copy is the prototype's
Arabic, and the English XLF takes the prototype's English.

### 14.2 Plurals (ICU in templates)

Arabic has six plural categories. Unit words always go through ICU, never through string
concatenation. The prototype's `seatWord()` approximates this and is wrong for 3–10 vs 11–99 in some
places.

```html
<span i18n="@@store.units.seats">{count, plural,
  zero {لا مقاعد}
  one {مقعد واحد}
  two {مقعدان}
  few {{{countText}} مقاعد}
  many {{{countText}} مقعداً}
  other {{{countText}} مقعد}
}</span>
```

`countText` is the count formatted with Western digits (`formatCount(count)`).

| `count` | Category (ar) | Rendered |
|---|---|---|
| 0 | zero | لا مقاعد |
| 1 | one | مقعد واحد |
| 2 | two | مقعدان |
| 5 | few (3–10) | 5 مقاعد |
| 11 | many (11–99) | 11 مقعداً |
| 100 | other | 100 مقعد |
| 103 | few (n % 100 = 3) | 103 مقاعد |

English uses `one {1 seat} other {{{countText}} seats}`. The same pattern is used for copies
(`store.units.copies`: نسخة واحدة / نسختان / 3 نسخ / 11 نسخة / 100 نسخة), donors
(`store.units.donors`), lessons and days left. A cause's own unit label (`unit_label_ar`, for example
برّاد) is admin data with no plural forms. It is shown as "{qty} × {label}", which is what the
prototype does.

### 14.3 Numbers, dates, direction

- **Numerals.** Western numerals everywhere (§12.2).
- **Dates.** Gregorian: `ar-SA-u-ca-gregory-nu-latn`.
- **Mixed direction.** Mobile numbers, short addresses, references and amounts inside Arabic
  sentences are wrapped in `<bdi>` or `dir="ltr"` spans.
- **Locale-aware URLs.** Share links, return URLs and SMS links always include the locale segment.
  The locale sent to the API (`Accept-Language` and `orders.locale`) is the build locale.

---

## 15. Header and main-site integration (FR-S19, FR-S82, Q-09)

| Change | Where | Detail |
|---|---|---|
| "المتجر / Store" nav item | `layout/header` main nav and mobile nav | Links to `/{locale}/store`. Active on every `/store/**` route |
| Cart badge | `layout/header`, next to the language switch | `<ta-cart-badge>` is a link to `/store/cart` with `aria-label` "Cart, 3 items" (ICU plural). The count comes from `CartSummary`, which calls `GET /v1/cart/summary` |
| "تبرع الآن / Donate now" CTA and `/donate` | Header CTA, footer, `deploy/.htaccess` | If Q-09 is accepted: the CTA links to `/{locale}/store/p/education-fund`, and `/{locale}/donate` returns a 301 to it in `.htaccess` (a static host cannot run a server redirect). Until Q-09 closes, the CTA stays as it is |
| Store links in the footer | `layout/footer` | Courses, Books, Sponsor a seat, Give. Transparency links (refund policy, how sponsorships are allocated) point to main-site pages |

**The badge is client-only and causes no layout shift.**

- The badge button has a fixed 44 × 44 box. The count is absolutely positioned over it, and hidden
  with `:empty` while it is 0 or unknown.
- The prerendered HTML contains the empty badge, so nothing moves when the count arrives.
- `CartSummary.start()` runs in `afterNextRender`, then in `requestIdleCallback` (with a `setTimeout`
  fallback). The request therefore never competes with LCP on main-site pages.
- **Guests** are identified by the `t3s_cart` cookie. **Customers** are identified by the bearer:
  after sign-in the API deletes `t3s_cart` and the customer cart wins (11 §3, §4.2).
  `CartSummary.start()` therefore makes one `StoreAuth.ensureSession()` attempt at idle before it
  asks for `/cart/summary`. For a guest, that attempt costs one 401 per page load (open point 14).
- The API must not create a cart when there is neither a cookie nor a token. It returns
  `{ lineCount: 0 }` instead.
- The badge "bump" animation plays after an add, only without reduced motion. Count changes are not
  announced, because the add toast already announces them.

**CSP.** Add `https://store-api.ta3lom.sa` to `connect-src`. If product images are served from the store
API's public volume, also add it to `img-src`. Changing the CSP means updating `deploy/.htaccess`,
and `check-csp.mjs` runs as usual.

---

## 16. Store admin (`/admin/store/**`)

The store admin lives inside the existing ta3lom-web admin area, but it has its own shell and its own
guard. Staff sign in against the store API (`StaffAuth`, §4.3), not with the main admin's editor
session. The main admin's route guard must not wrap `/admin/store/**` (open point 6).

### 16.1 Segregation of duties (RUL-62)

Staff roles are an array, so one person can hold several. The UI shows screens and actions from the
API's policy tags (11 §1.2). `StaffAuth.can(policy)` evaluates them from the token's roles with the
same table. The UI is a convenience, and the API enforces every permission itself.

| Policy tag | Roles | Note |
|---|---|---|
| `staff:read` | any staff role, including `viewer` | Read-only screens and lists |
| `staff:catalogue` | `admin`, `store_manager` | Products, offers, causes, updates, media, categories |
| `staff:stock` | `admin`, `store_manager`, `fulfilment` | Stock movements |
| `staff:fulfilment` | `admin`, `fulfilment` | Shipments |
| `staff:finance` | `admin`, `finance` | Orders (cancel), payment review, refund eligibility, zakat, reports, customer block, goal adjustments |
| `staff:refund` | **`finance` only** | Executing a refund (RUL-56). An `admin` who does not also hold `finance` cannot refund |
| `staff:beneficiaries` | `admin`, `allocation_officer` | Beneficiary register (masked), candidates, batches |
| `staff:allocate` | **`allocation_officer` only** | Allocating and completing units (RUL-47). An `admin` without the role cannot allocate |
| `staff:reveal-pii` | **`allocation_officer` only** | Decrypting a beneficiary's name or mobile (FR-S52). Audited |
| `staff:resend` | `admin`, `finance`, `store_manager` | Resending documents and dedication cards, resetting download counts (proposed in 11) |
| `staff:admin` | `admin` | Staff accounts, settings, templates, audit log |

The three **bold** tags are never satisfied by `admin` alone. For an admin who lacks the role, the UI
does not hide those actions silently. It shows them disabled, with the hint "Requires the finance
role" or "Requires the allocation officer role", so the separation is visible.

### 16.2 Screens and visibility

| Path | Screen | FR | Screen visible with | Actions and the policy each needs |
|---|---|---|---|---|
| `/admin/store/login` | Staff sign-in: email + password, then TOTP (enrolment on first login) | FR-S70 | anonymous | — |
| `/admin/store` | Dashboard: sales and gifts (today/7/30 days), top products, campaign progress, allocation backlog, alerts | FR-S74 | `staff:read` | — |
| `/admin/store/products` (`/new`, `/:id`) | Courses, books (formats, stock fields, low-stock threshold, files), causes; categories; media with mandatory alt; draft/publish/archive; preview with the `ta-*` store components | FR-S72 | `staff:read` | Edit, publish, archive: `staff:catalogue` |
| `/admin/store/offers` | Sponsorship offers: seat price, target, group, dates, partial allowed, close on target, close now (runs the pool sweep) | FR-S50, FR-S56 | `staff:read` | Create, edit, close: `staff:catalogue` |
| `/admin/store/causes` | Cause settings: unit, target, dates, zakat flag, general fund, seed figures | FR-S58 | `staff:read` | Edit, goal seed: `staff:catalogue`. Goal adjustments (`correction`, and viewing the sweep pairs): `staff:finance` |
| `/admin/store/cause-updates` | Delivery updates: text, photo, units delivered, date, public/private, notify donors | FR-S59 | `staff:read` | Create, edit, delete: `staff:catalogue` |
| `/admin/store/stock` | On hand, reserved and available; restock and adjustment with a reason; low-stock flags | FR-S43 | `staff:read` | Movements: `staff:stock` |
| `/admin/store/orders` (`/:ref`) | Search by reference, mobile, status or date. The detail view shows lines, payments, events, documents and notifications | FR-S73 | `staff:read` | Cancel `pending_payment`/`payment_failed`: `staff:finance`. Resend documents or dedication card: `staff:resend` |
| `/admin/store/payment-review` | Orders in `payment_review` (amount mismatch, or a capture after expiry or cancellation). Actions: accept (→ `paid`, full settlement) or refund | FR-S38 | `staff:finance` | Accept: `staff:finance`. Refund: `staff:refund` |
| `/admin/store/refunds` | Refund eligibility per line (RUL-57), with the reason when a line is not refundable. Submit with an Idempotency-Key. Includes refunds after fulfilment and RUL-13 duplicate lines | FR-S64 | `staff:finance` | Execute: **`staff:refund`** |
| `/admin/store/shipments` | Queue, packing slip PDF, ship (carrier + tracking), deliver, return | FR-S42 | `staff:read` | Actions: `staff:fulfilment` |
| `/admin/store/beneficiaries` | Register, CSV import, verify/approve/reject, zakat eligibility, group, region, mobile (own or guardian's, RUL-47e). PII is masked | FR-S52 | `staff:beneficiaries` | Edit, import, invite: `staff:beneficiaries`. Reveal PII: **`staff:reveal-pii`** |
| `/admin/store/allocation` | Allocation console per product: FIFO backlog (awaiting, overdue), candidates, allocate a batch. Book copies: mark delivered with proof | FR-S53, FR-S55 | `staff:read` (backlog), `staff:beneficiaries` (candidates, batches) | Allocate, complete: **`staff:allocate`** |
| `/admin/store/zakat` | Zakat ledger per product, balances (`v_zakat_balance`), disbursement vouchers (`zakat_ledger` direction `out`, voucher number in `reference`) | FR-S61 | `staff:finance` | Record a voucher: `staff:finance` |
| `/admin/store/reports` | Sales & VAT, gifts by product, zakat, allocation backlog & completion, refunds, shipments; CSV export | FR-S75 | `staff:finance`. The allocation backlog is also on the allocation console (`staff:read`) | Export: `staff:finance` |
| `/admin/store/customers` | Lookup by mobile: orders, giving, enrolments | FR-S79 | `staff:read` | Block / unblock (RUL-61): `staff:finance` |
| `/admin/store/staff` | Staff accounts, roles, reset password or TOTP, unlock | FR-S70, FR-S71 | `staff:admin` | All: `staff:admin` |
| `/admin/store/settings` | Keys from RUL §M | FR-S76 | `staff:read` | Edit: `staff:admin` |
| `/admin/store/templates` | SMS/email templates (ar/en) with a test send | FR-S77 | `staff:read` | Edit, test: `staff:admin` |
| `/admin/store/audit` | Audit log filtered by actor, entity and date | FR-S78 | `staff:admin` | — |

**Shared behaviour on every screen**

- **Confirmations.** Destructive and financial actions (refund, cancel, accept a review, allocate,
  reveal PII, publish, close an offer) use a confirmation dialog that restates the effect in words.
- **Tables.** Admin tables use server pagination (`?page=&pageSize=` → `{items,total,page,pageSize}`)
  with `httpResource`, driven by filter signals. Filters are kept in the query string.
- **Freshness notice.** Product and campaign edit screens show the prerender notice from §2.4.
- **Visibility test.** A Playwright test signs in as `admin` alone and checks that Refund, Allocate
  and Reveal are disabled with their hints. It then signs in as `admin` + `finance` and checks that
  Refund is enabled.

---

## 17. Performance, SEO and testing

### 17.1 Performance (main NFR-01 to NFR-04)

| Measure | Target / technique |
|---|---|
| Initial JS | Unchanged within 150 KB (NFR-04). The initial bundle gains only `ta-cart-badge`, `CartSummary`, `STORE_API_URL` and the interceptors. `CartStore`, `StoreAuth` UI, the buy box and the admin are in lazy chunks |
| Page weight | ≤ 400 KB transfer for `/store` and product pages (NFR-01). Covers use `NgOptimizedImage` with `priority` on the LCP image only (hero or product cover) |
| Core Web Vitals | LCP ≤ 2.0 s, INP ≤ 200 ms, CLS ≤ 0.05 (NFR-02) on `/store`, `/store/p/{course}`, `/store/p/{cause}` in Lighthouse CI |
| `@defer` | Related products (`on viewport`), latest-gifts feed and cause updates (`on viewport`, browser only), share dialog (`on interaction`), dedication form (`on interaction` with its toggle), seat maps in the account (`on viewport`), payment SDK if Q-01 chooses it (`on interaction`) |
| Not deferred | SEO content: title, summary, body, curriculum, price, goal. These must be in the prerendered HTML |
| Live figures | One request per page. Catalogue cards are batched through `/catalog/live?slugs=` |
| Store chunk budget | Add a `bundle` budget for the store chunk (open point 8) |

### 17.2 SEO (FR-S09)

- **Tags.** `SeoService.set()` on every route: title, description (`seo_title_*`/`seo_desc_*`, falling
  back to title and summary), canonical `https://ta3lom.sa/{locale}/store/…` without query string,
  `hreflang` ar/en/x-default, Open Graph (image = cover asset), Twitter card.
- **JSON-LD** through `SeoService.setJsonLd()`:
  - `Course` with `provider` = the association and `offers` (`price`, `priceCurrency: "SAR"`);
  - `Book` with `workExample` per format (`bookFormat` `Paperback` / `EBook`) and its offer;
  - causes as `Product` with `potentialAction: DonateAction` (`recipient` = the association);
  - `BreadcrumbList` on product and catalogue pages.

  Prices in JSON-LD are the build-time values.
- **CSP and JSON-LD.** JSON-LD is a non-executable inline `<script type="application/ld+json">`.
  Confirm that `check-csp.mjs` ignores that type (open point 9).
- **`noindex`.** Cart, checkout, thanks, account, card, login, search and admin are `noindex` and
  excluded from the sitemap. The sitemap includes the store home, the four catalogues and every
  prerendered product in both locales.

### 17.3 Testing

**Vitest (unit)**

| Subject | Cases |
|---|---|
| `money.ts` | `times('350.00', 5) === '1750.00'`; `sumMoney(['0.10','0.20']) === '0.30'` (no float drift); `unitsEquivalent('100.00','350.00') === '0.29'`; negative and single-digit fractions |
| `buy-box.calc.ts` (RUL-12, FR-S12/13) | Seat 350: amount 100 gives a line of 100 SAR, `mode: 'amount'`, qty 1, "0.29 seats"; 5 seats gives 1,750 SAR, `mode: 'units'`, qty 5. Amount 9.99 is rejected (min 10). Qty 1000 is clamped to 999. Digital qty is forced to 1. Course `buy` qty = 1. `allowPartial = false` hides the amount mode. A cause without `unitAmount` is amount-only. Zakat is offered only when `zakatEligible`. Presets per variant (1/5/10/25, 10/25/50/100, 1/2/5/10, 50/100/250/500) |
| Pooled share display (RUL-45, §07 c) | Seat 350, contributions 100, 150, 120 arriving in that order. Fractions shown: 0.29, 0.43, 0.34 of a seat. After conversion, A and B are "fully converted", and C is "100 converted, 20 waiting in the pool" |
| `goal-bar` (RUL-08) | 12,450 / 30,000 → 41% (floor; rounding would give 42). 31,000 / 30,000 → bar 100%, text 103%. `reached` shows "Goal reached". A past `endsAt` shows "Campaign ended" |
| `cart.calc.ts` | Totals with and without a print line (shipping 25.00 once per order, RUL-26); `grand = purchases + shipping + gifts` (RUL-31); `mergeOrAppend` merges identical undedicated `units` lines and never merges `amount` or dedicated lines (RUL-14) |
| `CartStore` | An optimistic add increments `count` at once. Server success replaces lines with the server cart. Server error rolls back and sets `lastProblem`. Two quick mutations resolve in order (the queue). `setQuantity` clamps. `HttpTestingController` is used |
| `StoreAuth` | Single-flight refresh (two concurrent 401s cause one refresh). `auth.refresh_race` is retried once. A failed refresh clears the token. The token is never written to `localStorage` or `sessionStorage` (spy) |
| Interceptors | `withCredentials` only for the store API. Bearer is staff for `/admin`, customer otherwise. 401 → refresh → one retry. A second 401 propagates |
| Checkout form | Mobile `05XXXXXXXX` accepted and normalised to `+9665…`; Arabic-Indic digits normalised; short address `RRRD2929` accepted, `rrrd2929` upper-cased, `RRR2929` rejected; address rules active only with a print line; terms required, and the request body always carries `acceptTerms: true` |
| Thanks polling | Cadence of 2 s for 60 s, then 10 s (fake timers). Stops on each terminal status. Gives up at 10 minutes. Uses the bearer when signed in, otherwise `?t=`. Ignores the gateway's `status` query parameter. `cartRestored: true` triggers exactly one `CartStore.reload()` |
| `routes.mjs` | Store slugs are appended to `routes.txt`. An unreachable or erroring store API exits non-zero |
| `StaffAuth.can()` | `admin` alone fails `staff:refund`, `staff:allocate` and `staff:reveal-pii`. `admin` + `finance` passes `staff:refund` (RUL-62) |
| ICU | Seats and copies plural output for 0, 1, 2, 5, 11, 100, 103 in `ar`, 1 and 2 in `en` |
| SEO | Every store route calls `SeoService.set()` (extends the existing route test) |

**Playwright (e2e)**

Store API responses are mocked with `page.route()` fixtures generated from the OpenAPI examples. A
nightly job repeats the journeys against staging with the gateway sandbox.

| Journey | Checks |
|---|---|
| Buy a course | Catalogue → course → Enrol myself → cart (Your purchases) → checkout → OTP (mocked code) → details → Pay → mocked gateway redirect → thanks polls `pending_payment` twice, then `paid` → success view → My courses shows the course |
| Sponsor seats with a dedication | Course → Sponsor seats → preset 5 → total 1,750 → dedication with an invalid mobile shows an error, then a valid one → cart shows the "On behalf of" row → checkout (anonymous checkbox visible) → thanks shows the dedication card preview without the amount |
| Partial share | Amount 100 on a 350 seat shows "0.29 seats" and adds a line of mode `amount` |
| Quick give | Donation card → 5 SAR shows an error → 100 SAR → toast announced → badge increments without layout shift (CLS measured) |
| Checkout OTP | Rate-limited response shows the wait from `retryAfterSeconds`; a wrong code shows `attemptsLeft`; `otp.too_many_attempts` asks for a new code; `auth.account_blocked` shows the blocked message; success merges the cart (a `cartNotices` notice appears) |
| Price changed | `POST /checkout` returns `checkout.price_changed`; the summary updates; Pay must be pressed again; the Idempotency-Key changes |
| Payment failed and retry | Thanks shows `payment_failed` with `canRetry` → Try again (`/retry-payment`) → new redirect |
| Expired order restored | Thanks shows `expired` with `cartRestored: true` → "Your items are back in your cart" → the cart page shows the lines, and the badge count updates |
| Status token only | Silent refresh mocked to 401; thanks at `?t=` shows the status without dedications; with no `t`, it redirects to login |
| Admin segregation | `admin` alone sees Refund, Allocate and Reveal disabled with hints; `admin` + `finance` can refund |
| Print book | Shipping section appears; short address validation; shipping 25 SAR in the summary |
| RTL and keyboard | Every journey runs in `/ar` first, then `/en`; keyboard-only buy box and dialog; tap targets ≥ 44 px; no horizontal scroll at 320 px |

**Accessibility.** axe-core (`npm run a11y`) runs on every store route, in both locales and both themes.
That includes the prerendered pages and client routes rendered with mocked data: cart with lines,
checkout at each step, thanks in every state, the account tabs, the card page, and the main admin
screens. Any violation fails the build (main NFR-07).

---

## 18. Open points

1. **Search route.** Still open. `/store/search` is added for FR-S03, but it is not in the brief's
   route list. Confirm it, or make search a query parameter on the typed catalogues (which would
   limit a search to one type).
2. **Prerender route source.** Resolved — canonical decision r1-16. `scripts/routes.mjs` →
   `routes.txt` is extended (§2.3), and the build fails if the store API is unreachable.
3. **Rebuild on publish.** Still open. Should publishing or archiving a store product trigger
   `repository_dispatch`? If so, decide whether the store API holds a GitHub token or calls the main
   API's `/admin/rebuild`.
4. **Staff cookie and multi-tab refresh.** Resolved — RUL-60/62 and 11 §4.3.
   - Staff use `t3s_srt` with `Path=/v1/admin/auth`, and endpoints under `/v1/admin/auth/*`.
   - The API reports a concurrent rotation as `auth.refresh_race`. The client serialises refreshes
     with Web Locks and retries once on `auth.refresh_race`.
5. **Cart after a failed or expired order.** Resolved — RUL-29. The lines are restored to the active
   cart, and products already there are skipped. The client reloads on `cartRestored` (§6, §9).
6. **Admin shell.** Still open. The existing admin route tree may have a parent guard for the
   main-site editor session. `/admin/store/**` needs its own guard, and staff who are not main-site
   editors must be able to reach it.
7. **Role gaps.** Resolved — RUL-62 (segregation of duties) and 11 §1.2.
   - `admin` does not refund, allocate or reveal PII.
   - Customer lookup is `staff:read`, and block/unblock is `staff:finance`.
   - Templates are `staff:admin`.
   - `staff:resend` remains a proposal in 11.
8. **Store chunk budget.** Still open. Agree a lazy-chunk budget for `features/store` (proposal:
   120 KB gzipped).
9. **JSON-LD and `check-csp.mjs`.** Still open. Confirm that `application/ld+json` blocks are exempt
   from the inline-script hash check.
10. **Selector prefix.** Resolved — canonical decision r1-17. The prefix is `ta-` (`<ta-money>`).
11. **RUL-08 rounding.** Decided. The build rounds down, as RUL-08 says, where the prototype rounds to
    the nearest percent.
12. **Order status endpoint.** Resolved — canonical decision r1-15 (RUL-33). The endpoint takes the
    customer bearer or the per-order status token (`?t=`), never the reference alone (§9).
13. **Payment method selection (Q-01).** Still open. Is the method chosen at our checkout and passed to
    the gateway, or does the hosted page show its own picker?
14. **Header badge for a signed-in visitor without a token in memory.** New. After sign-in the API
    deletes `t3s_cart` (11 §4.2), so `GET /v1/cart/summary` needs the bearer. §15 therefore attempts
    one silent refresh at idle time. For guests with no `t3s_rt`, that refresh is a wasted 401 on every
    main-site page load. Proposal: the API sets a non-HttpOnly, value-free hint cookie
    (`t3s_si=1`, `Domain=.ta3lom.sa`) while a customer session exists, so the header can skip the
    refresh for guests.
15. **Restored cart lines after a `payment_review` acceptance.** New (see 07, open point 12). Lines
    restored at expiry stay in the cart if finance later accepts the late capture.
