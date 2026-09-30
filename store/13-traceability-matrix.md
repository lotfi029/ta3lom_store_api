# 13 — Traceability Matrix

**Revision:** S1 draft · 29 September 2026

Links from business requirements to rules, functional requirements, stories, tables, endpoints, screens, sessions and tests. Every link is taken from the documents below; nothing is inferred beyond them. A missing link is shown as "—" and listed under [Gaps](#gaps).

| Link | Source |
|---|---|
| FR-S → BR-S, RUL | [04](04-functional-requirements.md) "Traces" column |
| US-S → FR-S, RUL, BR-S | [05](05-user-stories.md) "Traces" line of each story |
| Tables | [09 §4](09-backend-dotnet.md#4-modules) aggregates and handlers, [11](11-api-contract.md) DTOs; names exactly as in [`001_store_schema.sql`](../../database/store/001_store_schema.sql) (49 tables, 4 views) |
| Endpoints | [11 §12](11-api-contract.md#12-endpoint-index) "Serves" column (index number `#n`) |
| Routes / screens | [10 §2.1](10-frontend-angular.md#21-route-table), [§15](10-frontend-angular.md#15-header-and-main-site-integration-fr-s19-fr-s82-q-09), [§16.2](10-frontend-angular.md#162-screens-and-visibility) |
| Sessions | [12](12-implementation-plan.md#sessions) "FR-S" column |
| Tests | [09 §10](09-backend-dotnet.md#10-testing-strategy), [10 §17.3](10-frontend-angular.md#173-testing) |

Test codes: **U** domain/application unit test class (09 §10.1) · **I** integration (09 §10.2) · **W** webhook replay (09 §10.4) · **C** concurrency (09 §10.5) · **V** Vitest (10 §17.3) · **P** Playwright journey (10 §17.3) · **X** axe (10 §17.3). In addition, every endpoint has a response-shape contract test (09 §10.3) and `RuleCoverageTests` asserts one test per RUL (09 §10.1); these are not repeated per row.

---

## 1. BR-S → RUL → FR-S

FR-S are those whose 04 "Traces" cite the BR-S. RUL are the rules cited by those FR-S. The last column lists stories that cite the BR-S directly (05).

| BR-S | Title | RUL (via the FR-S) | FR-S (04) | US-S citing the BR-S |
|---|---|---|---|---|
| BR-S01 | Sell courses online | RUL-04 | FR-S02, FR-S04 | — |
| BR-S02 | Sell books in print and digital formats | RUL-05 | FR-S02, FR-S05 | — |
| BR-S03 | Sponsor seats and copies for others | — | FR-S02 | — |
| BR-S04 | Donation products with goals | RUL-23, RUL-55 | FR-S02, FR-S06 | US-S06 |
| BR-S05 | Give on behalf of someone (إهداء) | RUL-21, RUL-22 | FR-S14, FR-S60 | US-S25 |
| BR-S06 | Zakat handled separately | RUL-07, RUL-19, RUL-20, RUL-53 | FR-S13, FR-S61 | — |
| BR-S07 | One cart, one checkout, correct paperwork | RUL-14, RUL-23, RUL-25, RUL-26, RUL-27, RUL-31, RUL-59 | FR-S18, FR-S30, FR-S62, FR-S63 | US-S61 |
| BR-S08 | Show donors where their gift went | RUL-23, RUL-50, RUL-55 | FR-S06, FR-S25 | US-S06, US-S19 |
| BR-S09 | Customer accounts with the lowest possible friction | RUL-60 | FR-S21 | US-S28 |
| BR-S10 | Run the store without a developer | RUL-01, RUL-02, RUL-03 | FR-S72, FR-S73 | US-S47 |
| BR-S11 | Finance-grade records | — | FR-S74, FR-S75, FR-S78 | US-S71, US-S72 |
| BR-S12 | Look and feel of ta3lom.sa | — | FR-S01, FR-S19, FR-S82 | US-S01, US-S76 |
| BR-S13 | Be ready for the tax authority | RUL-27, RUL-59 | FR-S62, FR-S65 | US-S61, US-S66 |
| BR-S14 | Recurring and endowment giving | — | — | — |

## 2. Main matrix (one row per FR-S)

| FR-S | Pri · Rel | RUL | US-S | Tables | API endpoints (11 §12) | Route / screen (10) | Session (12) | Tests |
|---|---|---|---|---|---|---|---|---|
| **FR-S01** Store home | M · S1 | — | US-S01 | `product`, `course_detail`, `book_format`, `cause_detail`, `sponsorship_offer`, `goal_progress`, `media_asset`, `v_goal` | #1 GET `/v1/catalog/home` | `/store` | S-08, S-10 | U GoalProgressTests; V SEO; X |
| **FR-S02** Catalogue | M · S1 | — | US-S01¹, US-S02 | `product`, `category`, `course_detail`, `book_format`, `cause_detail`, `sponsorship_offer`, `media_asset`, `v_goal` | #2 GET `/v1/catalog/categories`<br>#3 GET `/v1/catalog/products` | `/store/courses`, `/store/books`, `/store/sponsor`, `/store/give` | S-08, S-10 | U ProductTests; P Buy a course; X |
| **FR-S03** Search | S · S1 | — | US-S03 | `product` | #3 GET `/v1/catalog/products` | `/store/search` (open, 10 OP-1) | S-08, S-10 | — |
| **FR-S04** Course page | M · S1 | RUL-04 | US-S04 | `product`, `course_detail`, `course_module`, `category`, `media_asset`, `sponsorship_offer`, `goal_progress` | #4 GET `/v1/catalog/products/{slug}` | `/store/p/:slug` | S-08, S-11 | U ProductTests; P Buy a course |
| **FR-S05** Book page | M · S1 | RUL-05 | US-S05 | `product`, `book_detail`, `book_format`, `media_asset`, `sponsorship_offer` | #4 GET `/v1/catalog/products/{slug}` | `/store/p/:slug` | S-08, S-11 | U BookFormatTests; P Print book |
| **FR-S06** Cause page | M · S1 | RUL-23, RUL-55 | US-S06 | `product`, `cause_detail`, `goal_progress`, `cause_update`, `media_asset`, `orders`, `order_line`, `dedication` | #4 GET `/v1/catalog/products/{slug}`<br>#6 GET `/v1/catalog/products/{slug}/updates`<br>#7 GET `/v1/catalog/products/{slug}/feed` | `/store/p/:slug` | S-08, S-11, S-28 | U GiftFeedTests, CauseUpdateTests, OverTargetTests; V goal-bar |
| **FR-S07** Live figures on prerendered pages | M · S1 | RUL-08 | US-S07 | `v_goal`, `goal_progress`, `course_detail`, `book_format`, `sponsorship_offer`, `cause_detail` | #5 GET `/v1/catalog/live` | `/store`, catalogues, `/store/p/:slug` (`ProductData.live` = #5 with one slug, 10 §5.2) | S-08, S-11 | U GoalProgressTests; V goal-bar |
| **FR-S08** Share | S · S1 | — | US-S08 | — | — | `/store/p/:slug` (`ta-share-dialog`) | S-11 | — |
| **FR-S09** SEO | M · S1 | — | US-S09 | `product`, `media_asset` | #4 GET `/v1/catalog/products/{slug}` | all prerendered store routes | S-11 | V SEO, routes.mjs |
| **FR-S10** Course buy box | M · S1 | RUL-12, RUL-13 | US-S10 | `course_detail`, `sponsorship_offer`, `cart_line`, `enrolment`, `orders`, `order_line` | #12 POST `/v1/cart/lines` | `/store/p/:slug` (`ta-buy-box`; `owned` from #28 when signed in, 10 §5.2) | S-14 | U CartAddLineTests; V buy-box.calc; P Buy a course |
| **FR-S11** Book buy box | M · S1 | RUL-05, RUL-12 | US-S11, US-S18 | `book_format`, `sponsorship_offer`, `cart_line` | #12 POST `/v1/cart/lines` | `/store/p/:slug` (`ta-buy-box`) | S-14 | U CartAddLineTests; V buy-box.calc; P Print book |
| **FR-S12** Sponsor mode | M · S1 | RUL-06, RUL-12, RUL-45 | US-S16, US-S17, US-S18 | `sponsorship_offer`, `goal_progress`, `cart_line`, `setting` | #9 GET `/v1/catalog/config`<br>#12 POST `/v1/cart/lines` | `/store/p/:slug` (`ta-buy-box`) | S-14 | U CartAddLineTests; V buy-box.calc, pooled share; P Sponsor seats, Partial share |
| **FR-S13** Cause buy box | M · S1 | RUL-07, RUL-19, RUL-53 | US-S21, US-S22, US-S23, US-S24 | `cause_detail`, `cart_line`, `setting` | #9 GET `/v1/catalog/config`<br>#12 POST `/v1/cart/lines` | `/store/p/:slug` (`ta-buy-box`) | S-14 | U GiftIntentTests, CauseUnitsTests; V buy-box.calc |
| **FR-S14** Dedication form | M · S1 | RUL-21 | US-S25 | `cart_line`, `setting` | #9 GET `/v1/catalog/config`<br>#12 POST `/v1/cart/lines` | `/store/p/:slug` (dedication form) | S-14 | U DedicationTests; P Sponsor seats with a dedication |
| **FR-S15** Quick give | M · S1 | — | US-S20 | `cart_line` | #12 POST `/v1/cart/lines` | `/store`, `/store/give` (`ta-donation-card`) | S-14 | P Quick give |
| **FR-S16** Add to cart | M · S1 | — | US-S10, US-S20 | `cart`, `cart_line` | #12 POST `/v1/cart/lines` | `/store/p/:slug` | S-14 | P Buy a course |
| **FR-S17** Server-side cart | M · S1 | RUL-11, RUL-16, RUL-17 | US-S12 | `cart`, `cart_line`, `customer` | #10 GET `/v1/cart` | `/store/cart`, `/store/login` | S-13 | U CartMergeTests, CartExpiryTests; V CartStore |
| **FR-S18** Cart page | M · S1 | RUL-14, RUL-26, RUL-31 | US-S13 | `cart`, `cart_line`, `setting` | #10 GET `/v1/cart`<br>#13 PATCH `/v1/cart/lines/{lineId}`<br>#14 DELETE `/v1/cart/lines/{lineId}`<br>#15 DELETE `/v1/cart` | `/store/cart` | S-14 | V cart.calc, CartStore |
| **FR-S19** Header cart badge | M · S1 | — | US-S14 | `cart`, `cart_line` | #11 GET `/v1/cart/summary` | header on every page (10 §15) | S-14, S-36 | V CartStore; P Quick give (badge) |
| **FR-S20** Limits | M · S1 | RUL-12, RUL-18 | US-S15 | `cart_line`, `setting` | — | `/store/p/:slug`, `/store/cart` | S-13 | U CartLimitsTests; V buy-box.calc |
| **FR-S21** OTP sign-in | M · S1 | RUL-60 | US-S28 | `otp_challenge`, `customer`, `refresh_token`, `cart` | #16 POST `/v1/auth/otp/request`<br>#17 POST `/v1/auth/otp/verify` | `/store/login`, `/store/checkout` (`ta-otp-sign-in`) | S-04, S-07 | U OtpTests; V StoreAuth; P Checkout OTP |
| **FR-S22** Profile | M · S1 | RUL-61 | US-S39 | `customer`, `customer_address` | #21 GET `/v1/me`<br>#22 PATCH `/v1/me`<br>#23 GET `/v1/me/addresses`<br>#24 POST `/v1/me/addresses`<br>#25 PUT `/v1/me/addresses/{id}`<br>#26 DELETE `/v1/me/addresses/{id}`<br>#27 POST `/v1/me/addresses/{id}/default` | `/store/account/profile` | S-31, S-32 | X (account tabs) |
| **FR-S23** My courses | M · S1 | RUL-42 | US-S40 | `enrolment`, `course_detail` | #28 GET `/v1/me/enrolments`<br>#29 POST `/v1/me/enrolments/{id}/launch`<br>#30 GET `/v1/me/enrolments/{id}/certificate` | `/store/account/courses` | S-31, S-32, S-40 | U EnrolmentTests; P Buy a course |
| **FR-S24** My books | M · S1 | RUL-41 | US-S41 | `download_grant`, `download_log`, `shipment` | #31 GET `/v1/me/books`<br>#32 POST `/v1/me/downloads/{grantId}/link` | `/store/account/books` | S-31, S-32 | U DownloadGrantTests, FulfilmentStateTests |
| **FR-S25** My giving | M · S1 | RUL-50 | US-S19, US-S75 | `order_line`, `sponsored_unit`, `sponsored_unit_event`, `pool_contribution`, `cause_update`, `dedication` | #33 GET `/v1/me/giving`<br>#34 GET `/v1/me/giving/lines/{orderLineId}` | `/store/account/giving` | S-31, S-32, S-45 | U SponsoredUnitTests, UnitLifecycleTests; V pooled share |
| **FR-S26** Orders & receipts | M · S1 | RUL-59 | US-S42 | `orders`, `order_line`, `payment`, `invoice`, `donation_receipt`, `media_asset` | #35 GET `/v1/me/orders`<br>#36 GET `/v1/me/orders/{ref}`<br>#37 GET `/v1/me/documents/{number}` | `/store/account/orders` | S-31, S-32 | U DocumentSplitTests; X |
| **FR-S27** Data rights | M · S1 | RUL-61 | US-S43 | `customer`, `customer_address`, `refresh_token`, `orders` | #38 GET `/v1/me/export`<br>#39 DELETE `/v1/me` | `/store/account/profile` | S-31, S-32 | U AccountDeletionTests |
| **FR-S28** Sign out | S · S1 | — | US-S44 | `refresh_token` | #19 POST `/v1/auth/logout`<br>#20 POST `/v1/auth/logout-all` | `/store/account/profile` | S-04, S-32 | U RefreshRotationTests; C Refresh reuse |
| **FR-S30** Checkout page | M · S1 | RUL-23, RUL-25, RUL-26 | US-S29, US-S31¹, US-S65 | `orders`, `customer_address`, `setting` | #41 POST `/v1/checkout` | `/store/checkout` | S-15, S-18 | U OrderPlaceTests; V Checkout form; P Print book |
| **FR-S31** Server validation & repricing | M · S1 | RUL-15, RUL-24, RUL-27 | US-S24, US-S30 | `cart_line`, `product`, `course_detail`, `book_format`, `sponsorship_offer`, `cause_detail`, `enrolment`, `orders` | #40 POST `/v1/checkout/quote`<br>#41 POST `/v1/checkout` | `/store/checkout` | S-15 | U CheckoutValidationTests; P Price changed |
| **FR-S32** Order creation | M · S1 | RUL-28, RUL-30 | US-S31 | `orders`, `order_line`, `dedication`, `book_format`, `cart`, `idempotency_key` | #41 POST `/v1/checkout` | `/store/checkout` | S-15 | U OrderPlaceTests, OrderReferenceTests, StockReservationTests; C Last print copy, Idempotency key |
| **FR-S33** Hosted payment | M · S1 | RUL-32, RUL-36 | US-S32 | `payment` | #41 POST `/v1/checkout` | `/store/checkout` (10 §8) | S-16 | I SettlementTests; P Buy a course |
| **FR-S34** Webhook settlement | M · S1 | RUL-33, RUL-34, RUL-39 | US-S33 | `payment_event`, `payment`, `orders`, `order_line`; RUL-39 effects: `enrolment`, `download_grant`, `shipment`, `sponsored_unit`, `pool_contribution`, `unit_funding`, `goal_progress`, `zakat_ledger`, `invoice`, `donation_receipt`, `document_counter`, `outbox_message` | #44 POST `/v1/payments/webhooks/{provider}` | — (server) | S-16 | W all rows; C Two webhooks; I SettlementTests, SettlementEffectsTests |
| **FR-S35** Thank-you page | M · S1 | RUL-33, RUL-37 | US-S35 | `orders`, `payment`, `dedication` | #42 GET `/v1/orders/{ref}/status` | `/store/orders/:ref/thanks` | S-18 | V Thanks polling; P Status token only |
| **FR-S36** Payment retry | M · S1 | RUL-37 | US-S36 | `payment`, `orders`, `idempotency_key` | #43 POST `/v1/orders/{ref}/retry-payment` | `/store/orders/:ref/thanks` | S-16, S-18 | U RetryPaymentTests; W Failed then success; P Payment failed and retry |
| **FR-S37** Reconciliation job | M · S1 | RUL-29, RUL-38 | US-S37, US-S79 | `payment`, `orders`, `book_format`, `cart`, `cart_line` | — | — (job); `/store/orders/:ref/thanks` shows `cartRestored` | S-16 | U ReconciliationTests, OrderStateMachineTests; W Webhook lost, Order expires; P Expired order restored |
| **FR-S38** Payment review | M · S1 | RUL-35 | US-S34, US-S63 | `orders`, `payment`, `audit_log` | #89 POST `/v1/admin/orders/{ref}/payment-review/resolve` | `/admin/store/payment-review` | S-16 | U PaymentReviewTests; W amount mismatch, late capture |
| **FR-S39** BNPL | C · S2 | RUL-36 | US-S38 | `payment` | — | — (S2) | — | — (S2) |
| **FR-S40** Course enrolment | M · S1 | RUL-42 | US-S52 | `enrolment`, `outbox_message` | — | — (server); result in `/store/account/courses` | S-19 | U EnrolmentTests |
| **FR-S41** Digital download | M · S1 | RUL-41 | US-S41 | `download_grant`, `download_log`, `book_format`, `media_asset` | #46 GET `/v1/downloads/{token}` | `/store/account/books` | S-20 | U DownloadGrantTests |
| **FR-S42** Shipments | M · S1 | RUL-40 | US-S53 | `shipment`, `orders`, `order_line`, `outbox_message` | #95 GET `/v1/admin/shipments`<br>#96 GET `/v1/admin/shipments/{id}/packing-slip`<br>#97 POST `/v1/admin/shipments/{id}/ship`<br>#98 POST `/v1/admin/shipments/{id}/deliver`<br>#99 POST `/v1/admin/shipments/{id}/return` | `/admin/store/shipments` | S-21, S-22 | U FulfilmentStateTests |
| **FR-S43** Stock management | M · S1 | RUL-30 | US-S54 | `book_format`, `stock_movement`, `setting` | #78 GET `/v1/admin/stock`<br>#79 GET `/v1/admin/stock/{formatId}/movements`<br>#80 POST `/v1/admin/stock/{formatId}/movements` | `/admin/store/stock` | S-21, S-22, S-41 | U StockReservationTests, BookFormatTests; C Last print copy |
| **FR-S50** Sponsorship offers | M · S1 | RUL-06, RUL-09, RUL-10 | US-S48 | `sponsorship_offer`, `sponsorship_pool` | #68 GET `/v1/admin/products/{id}/offers`<br>#69 POST `/v1/admin/products/{id}/offers`<br>#70 PUT `/v1/admin/offers/{offerId}`<br>#71 POST `/v1/admin/offers/{offerId}/close` | `/admin/store/offers` | S-09, S-12 | U SponsorshipOfferTests |
| **FR-S51** Unit creation & pooling | M · S1 | RUL-43, RUL-45 | US-S17, US-S57 | `sponsored_unit`, `sponsorship_pool`, `pool_contribution`, `unit_funding`, `goal_progress` | — | — (settlement) | S-23 | U SponsorshipPoolTests, SponsoredUnitTests; C Pool race |
| **FR-S52** Beneficiary register | M · S1 | RUL-51 | US-S55 | `beneficiary`, `audit_log` | #100 GET `/v1/admin/beneficiaries`<br>#101 POST `/v1/admin/beneficiaries`<br>#102 GET `/v1/admin/beneficiaries/{id}`<br>#103 PUT `/v1/admin/beneficiaries/{id}`<br>#104 POST `/v1/admin/beneficiaries/{id}/eligibility`<br>#105 POST `/v1/admin/beneficiaries/{id}/reveal`<br>#107 POST `/v1/admin/beneficiaries/import`<br>#108 GET `/v1/admin/beneficiaries/import/template` | `/admin/store/beneficiaries` | S-24, S-27 | U BeneficiaryTests |
| **FR-S53** Allocation console | M · S1 | RUL-47, RUL-48 | US-S56 | `sponsored_unit`, `sponsored_unit_event`, `allocation_batch`, `beneficiary`, `enrolment`, `distribution`, `customer`, `zakat_ledger` | #109 GET `/v1/admin/allocation/backlog`<br>#110 GET `/v1/admin/allocation/products/{productId}/units`<br>#111 GET `/v1/admin/allocation/products/{productId}/candidates`<br>#112 POST `/v1/admin/allocation/products/{productId}/allocate`<br>#113 GET `/v1/admin/allocation/batches/{batchId}` | `/admin/store/allocation` | S-25, S-27 | U AllocationPolicyTests, CustomerProvisioningTests; I AllocationFifoTests; C Two allocators |
| **FR-S54** Beneficiary invitation | M · S1 | RUL-52 | US-S45 | `beneficiary`, `customer`, `outbox_message` | #106 POST `/v1/admin/beneficiaries/{id}/invite` | `/admin/store/beneficiaries` (invite); `/store/login` | S-25 | U CustomerProvisioningTests |
| **FR-S55** Unit lifecycle | M · S1 | RUL-50 | US-S46 | `sponsored_unit`, `sponsored_unit_event`, `enrolment`, `distribution` | #114 POST `/v1/admin/units/{unitId}/complete` | `/admin/store/allocation` | S-25, S-40 | U UnitLifecycleTests |
| **FR-S56** Pool sweep | M · S1 | RUL-46 | US-S58 | `sponsorship_offer`, `sponsorship_pool`, `pool_contribution`, `goal_adjustment`, `goal_progress` | #71 POST `/v1/admin/offers/{offerId}/close` | `/admin/store/offers` (close) | S-42, S-44 | U PoolSweepTests; C Sweep vs late contribution |
| **FR-S57** Allocation alerts | S · S1 | RUL-49 | US-S59 | `sponsored_unit`, `v_allocation_backlog`, `outbox_message` | — | `/admin/store` (dashboard alerts) | S-25 | U AllocationAlertTests |
| **FR-S58** Causes | M · S1 | RUL-07, RUL-09, RUL-10 | US-S49 | `cause_detail`, `goal_progress`, `goal_adjustment` | #63 PUT `/v1/admin/products/{id}/cause`<br>#67 PUT `/v1/admin/products/{id}/goal-seed`<br>#136 GET `/v1/admin/products/{id}/goal-adjustments`<br>#137 POST `/v1/admin/products/{id}/goal-adjustments` | `/admin/store/causes` | S-09, S-12 | U CauseDetailTests |
| **FR-S59** Delivery updates | M · S1 | RUL-54 | US-S60 | `cause_update`, `media_asset`, `outbox_message` | #81 GET `/v1/admin/products/{id}/updates`<br>#82 POST `/v1/admin/products/{id}/updates`<br>#83 PUT `/v1/admin/cause-updates/{updateId}`<br>#84 DELETE `/v1/admin/cause-updates/{updateId}` | `/admin/store/cause-updates`; `/store/p/:slug` | S-26, S-27 | U CauseUpdateTests |
| **FR-S60** Dedication cards | M · S1 | RUL-22 | US-S26, US-S27 | `dedication`, `outbox_message`, `notification` | #45 GET `/v1/cards/{token}`<br>#93 POST `/v1/admin/dedications/{id}/resend` | `/store/card/:token`; `/admin/store/orders` (resend) | S-26, S-28 | U DedicationCardHandlerTests |
| **FR-S61** Zakat ledger | M · S1 | RUL-20 | US-S22, US-S64, US-S78 | `zakat_ledger`, `v_zakat_balance` | #115 GET `/v1/admin/zakat`<br>#116 POST `/v1/admin/zakat/disbursements` | `/admin/store/zakat` | S-25, S-27, S-43, S-44 | U GiftIntentTests |
| **FR-S62** Sales invoice | M · S1 | RUL-27, RUL-59 | US-S61 | `invoice`, `document_counter`, `media_asset` | — | `/store/account/orders` (download) | S-17 | U DocumentNumberingTests, DocumentSplitTests, VatCalculatorTests; C Document numbers |
| **FR-S63** Donation receipt | M · S1 | RUL-59 | US-S61, US-S65 | `donation_receipt`, `document_counter`, `media_asset` | — | `/store/account/orders` (download) | S-17 | U DocumentSplitTests |
| **FR-S64** Refunds | M · S1 | RUL-56, RUL-57, RUL-58 | US-S62, US-S80 | `refund`, `refund_line`, `payment`, `orders`, `order_line`, `invoice`, `donation_receipt`, `goal_progress`, `pool_contribution`, `sponsorship_pool`, `zakat_ledger`, `idempotency_key` | #90 GET `/v1/admin/orders/{ref}/refund-eligibility`<br>#91 POST `/v1/admin/orders/{ref}/refunds`<br>#92 GET `/v1/admin/refunds` | `/admin/store/refunds` | S-29, S-30 | U RefundPolicyTests, RefundEffectsTests, RefundAuthorisationTests; P Admin segregation |
| **FR-S65** ZATCA Phase 2 | S · S2 | — | US-S66 | `invoice` | — | — (S2) | — | — (S2) |
| **FR-S70** Staff sign-in | M · S1 | RUL-62 | US-S67 | `staff_user`, `refresh_token`, `audit_log` | #47 POST `/v1/admin/auth/login`<br>#48 POST `/v1/admin/auth/totp/enrol`<br>#49 POST `/v1/admin/auth/totp/verify`<br>#50 POST `/v1/admin/auth/refresh`<br>#51 POST `/v1/admin/auth/logout`<br>#126 POST `/v1/admin/staff/{id}/reset-password`<br>#127 POST `/v1/admin/staff/{id}/reset-totp`<br>#128 POST `/v1/admin/staff/{id}/unlock` | `/admin/store/login`, `/admin/store/staff` | S-05, S-07 | U StaffLockoutTests |
| **FR-S71** Role-based access | M · S1 | RUL-62 | US-S68 | `staff_user` | #52 GET `/v1/admin/me`<br>#123 GET `/v1/admin/staff`<br>#124 POST `/v1/admin/staff`<br>#125 PUT `/v1/admin/staff/{id}` | `/admin/store/staff`; every admin screen (10 §16.1) | S-05 | U RefundAuthorisationTests; V StaffAuth.can(); P Admin segregation |
| **FR-S72** Product management | M · S1 | RUL-01, RUL-02, RUL-03 | US-S47 | `product`, `course_detail`, `course_module`, `book_detail`, `book_format`, `cause_detail`, `category`, `media_asset` | #54 GET `/v1/admin/products`<br>#55 POST `/v1/admin/products`<br>#56 GET `/v1/admin/products/{id}`<br>#57 PUT `/v1/admin/products/{id}`<br>#58 PUT `/v1/admin/products/{id}/course`<br>#59 PUT `/v1/admin/products/{id}/modules`<br>#60 PUT `/v1/admin/products/{id}/book`<br>#61 POST `/v1/admin/products/{id}/formats`<br>#62 PUT `/v1/admin/formats/{formatId}`<br>#64 POST `/v1/admin/products/{id}/publish`<br>#65 POST `/v1/admin/products/{id}/archive`<br>#66 GET `/v1/admin/products/{id}/preview`<br>#72 GET `/v1/admin/categories`<br>#73 POST `/v1/admin/categories`<br>#74 PUT `/v1/admin/categories/{id}`<br>#75 POST `/v1/admin/media`<br>#76 GET `/v1/admin/media`<br>#77 PATCH `/v1/admin/media/{id}` | `/admin/store/products` | S-09, S-12 | U ProductTests |
| **FR-S73** Order management | M · S1 | — | US-S27, US-S69 | `orders`, `order_line`, `payment`, `payment_event`, `invoice`, `donation_receipt`, `notification` | #85 GET `/v1/admin/orders`<br>#86 GET `/v1/admin/orders/{ref}`<br>#87 POST `/v1/admin/orders/{ref}/cancel`<br>#88 POST `/v1/admin/orders/{ref}/resend-documents` | `/admin/store/orders` | S-21, S-22, S-34 | — |
| **FR-S74** Dashboard | S · S1 | — | US-S70 | `v_sales_daily`, `v_goal`, `v_allocation_backlog` | #53 GET `/v1/admin/dashboard` | `/admin/store` | S-33, S-34 | — |
| **FR-S75** Reports | M · S1 | — | US-S71 | `v_sales_daily`, `v_zakat_balance`, `v_allocation_backlog`, `refund`, `shipment` | #92 GET `/v1/admin/refunds`<br>#109 GET `/v1/admin/allocation/backlog`<br>#117 GET `/v1/admin/reports/{name}` | `/admin/store/reports` | S-33, S-34 | — |
| **FR-S76** Settings | M · S1 | — | US-S50 | `setting` | #118 GET `/v1/admin/settings`<br>#119 PUT `/v1/admin/settings/{key}` | `/admin/store/settings` | S-09, S-12, S-34 | — |
| **FR-S77** Message templates | S · S1 | RUL-63 | US-S51 | `message_template` | #120 GET `/v1/admin/templates`<br>#121 PUT `/v1/admin/templates/{key}/{channel}/{locale}`<br>#122 POST `/v1/admin/templates/{key}/{channel}/{locale}/test` | `/admin/store/templates` | S-34, S-35 | — |
| **FR-S78** Audit log | M · S1 | — | US-S72 | `audit_log` | #133 GET `/v1/admin/audit` | `/admin/store/audit` | S-05, S-33, S-34 | — |
| **FR-S79** Customer lookup | S · S1 | — | US-S28¹, US-S73 | `customer`, `orders`, `enrolment`, `refresh_token` | #129 GET `/v1/admin/customers`<br>#130 GET `/v1/admin/customers/{id}`<br>#131 POST `/v1/admin/customers/{id}/block`<br>#132 POST `/v1/admin/customers/{id}/unblock` | `/admin/store/customers` | S-33, S-34 | — |
| **FR-S80** Transactional outbox | M · S1 | RUL-63 | US-S74, US-S75 | `outbox_message`, `notification`, `message_template` | — | — (server) | S-17, S-35 | U OutboxMappingTests, OutboxDispatcherTests |
| **FR-S81** SMS adapter | M · S1 | — | US-S74 | `notification` | — | — (server) | S-17 | U OutboxDispatcherTests |
| **FR-S82** Main-site integration | M · S1 | — | US-S14, US-S23, US-S76 | `cart` | — | main-site header, footer, `/donate` (10 §15) | S-36 | — |
| **FR-S83** Health endpoints | M · S1 | — | US-S77 | `outbox_message` | #134 GET `/health/live`<br>#135 GET `/health/ready` | — (server) | S-01 | — |

¹ Listed in the 05 coverage table but not in the story's own Traces line (see Gaps).

Endpoints whose 11 §12 Serves cell names only a rule (no FR-S), so they appear in no row above: #18 POST `/v1/auth/refresh` (RUL-60), #94 POST `/v1/admin/download-grants/{id}/reset` (RUL-41).

## 3. RUL coverage

FR-S from 04 "Traces"; US-S from 05 "Traces". ⚠ = no FR-S or no US-S cites the rule.

| RUL | FR-S | US-S | Flag |
|---|---|---|---|
| RUL-01 | FR-S72 | US-S47 |  |
| RUL-02 | FR-S72 | US-S01, US-S02, US-S47 |  |
| RUL-03 | FR-S72 | US-S47 |  |
| RUL-04 | FR-S04 | US-S04 |  |
| RUL-05 | FR-S05, FR-S11 | US-S05, US-S11 |  |
| RUL-06 | FR-S12, FR-S50 | US-S16, US-S18, US-S48 |  |
| RUL-07 | FR-S13, FR-S58 | US-S21, US-S23, US-S49 |  |
| RUL-08 | FR-S07 | US-S01, US-S06, US-S07 |  |
| RUL-09 | FR-S50, FR-S58 | US-S24, US-S48, US-S58 |  |
| RUL-10 | FR-S50, FR-S58 | US-S24, US-S48 |  |
| RUL-11 | FR-S17 | US-S12 |  |
| RUL-12 | FR-S10, FR-S11, FR-S12, FR-S20 | US-S10, US-S11, US-S15, US-S16, US-S17, US-S20, US-S21 |  |
| RUL-13 | FR-S10 | US-S10, US-S30, US-S33, US-S80 |  |
| RUL-14 | FR-S18 | US-S11, US-S12, US-S13 |  |
| RUL-15 | FR-S31 | US-S07, US-S30 |  |
| RUL-16 | FR-S17 | US-S12 |  |
| RUL-17 | FR-S17 | US-S12 |  |
| RUL-18 | FR-S20 | US-S15 |  |
| RUL-19 | FR-S13 | US-S22 |  |
| RUL-20 | FR-S61 | US-S22, US-S56, US-S64, US-S78 |  |
| RUL-21 | FR-S14 | US-S25 |  |
| RUL-22 | FR-S60 | US-S26 |  |
| RUL-23 | FR-S06, FR-S30 | US-S06, US-S26, US-S29, US-S65 |  |
| RUL-24 | FR-S31 | US-S24, US-S30 |  |
| RUL-25 | FR-S30 | US-S29 |  |
| RUL-26 | FR-S18, FR-S30 | US-S13, US-S29 |  |
| RUL-27 | FR-S31, FR-S62 | US-S61 |  |
| RUL-28 | FR-S32 | US-S31 |  |
| RUL-29 | FR-S37 | US-S31, US-S34, US-S37, US-S79 |  |
| RUL-30 | FR-S32, FR-S43 | US-S31, US-S37, US-S54 |  |
| RUL-31 | FR-S18 | US-S13, US-S31 |  |
| RUL-32 | FR-S33 | US-S32 |  |
| RUL-33 | FR-S34, FR-S35 | US-S33, US-S35 |  |
| RUL-34 | FR-S34 | US-S33 |  |
| RUL-35 | FR-S38 | US-S34, US-S63 |  |
| RUL-36 | FR-S33, FR-S39 | US-S32, US-S38 |  |
| RUL-37 | FR-S35, FR-S36 | US-S35, US-S36 |  |
| RUL-38 | FR-S37 | US-S37 |  |
| RUL-39 | FR-S34 | US-S33 |  |
| RUL-40 | FR-S42 | US-S53 |  |
| RUL-41 | FR-S24, FR-S41 | US-S41 |  |
| RUL-42 | FR-S23, FR-S40 | US-S40, US-S52 |  |
| RUL-43 | FR-S51 | US-S16, US-S33 |  |
| RUL-44 | FR-S51 | — | ⚠ no story (FR trace added 30 Sep) |
| RUL-45 | FR-S12, FR-S51 | US-S17, US-S57 |  |
| RUL-46 | FR-S56 | US-S17, US-S58 |  |
| RUL-47 | FR-S53 | US-S45, US-S56 |  |
| RUL-48 | FR-S53 | US-S56 |  |
| RUL-49 | FR-S57 | US-S59 |  |
| RUL-50 | FR-S25, FR-S55 | US-S19, US-S46 |  |
| RUL-51 | FR-S52 | US-S55 |  |
| RUL-52 | FR-S54 | US-S45 |  |
| RUL-53 | FR-S13 | US-S21 |  |
| RUL-54 | FR-S59 | US-S06, US-S60 |  |
| RUL-55 | FR-S06 | US-S06 |  |
| RUL-56 | FR-S64 | US-S62 |  |
| RUL-57 | FR-S64 | US-S62 |  |
| RUL-58 | FR-S64 | US-S62, US-S64 |  |
| RUL-59 | FR-S26, FR-S62, FR-S63 | US-S33, US-S42, US-S61 |  |
| RUL-60 | FR-S21 | US-S28, US-S45 |  |
| RUL-61 | FR-S22, FR-S27 | US-S28, US-S30, US-S39, US-S43, US-S73 |  |
| RUL-62 | FR-S70, FR-S71 | US-S56, US-S62, US-S67, US-S68 |  |
| RUL-63 | FR-S77, FR-S80 | US-S26, US-S28, US-S51, US-S53, US-S60, US-S74, US-S75 |  |

RUL §M (settings) → FR-S76, US-S50.

## 4. Table coverage

Module from 09 §4; endpoints from 11 (index numbers); FR-S from section 2 above. ⚠ = no endpoint reads or writes the table as its resource.

| Table | Module (09) | Endpoints (11 §12) | FR-S | Flag / note |
|---|---|---|---|---|
| `document_counter` | Documents | — | FR-S34, FR-S62, FR-S63 | ⚠ numbers issued inside settlement (#44) and refund (#91) transactions |
| `setting` | Admin/Reporting & Settings | #9, #118, #119 | FR-S12, FR-S13, FR-S14, FR-S18, FR-S20, FR-S30, FR-S43, FR-S76 |  |
| `audit_log` | Identity (Staff) interceptor; Admin/Reporting | #133 | FR-S38, FR-S52, FR-S70, FR-S78 | written by every admin write (11 §9) |
| `media_asset` | Catalog | #1, #3, #4, #6, #37, #75, #76, #77 | FR-S01, FR-S02, FR-S04, FR-S05, FR-S06, FR-S09, FR-S26, FR-S41, FR-S59, FR-S62, FR-S63, FR-S72 |  |
| `category` | Catalog | #2, #3, #72, #73, #74 | FR-S02, FR-S04, FR-S72 |  |
| `product` | Catalog | #1, #2, #3, #4, #5, #6, #7, #54, #55, #56, #57, #64, #65, #66 | FR-S01, FR-S02, FR-S03, FR-S04, FR-S05, FR-S06, FR-S09, FR-S31, FR-S72 |  |
| `course_detail` | Catalog | #4, #5, #58 | FR-S01, FR-S02, FR-S04, FR-S07, FR-S10, FR-S23, FR-S31, FR-S72 |  |
| `course_module` | Catalog | #4, #59 | FR-S04, FR-S72 |  |
| `book_detail` | Catalog | #4, #60 | FR-S05, FR-S72 |  |
| `book_format` | Catalog (+ Fulfilment threshold) | #4, #5, #61, #62, #78, #79, #80 | FR-S01, FR-S02, FR-S05, FR-S07, FR-S11, FR-S31, FR-S32, FR-S37, FR-S41, FR-S43, FR-S72 |  |
| `stock_movement` | Fulfilment | #79, #80, #99 | FR-S43 |  |
| `sponsorship_offer` | Catalog | #4, #5, #68, #69, #70, #71 | FR-S01, FR-S02, FR-S04, FR-S05, FR-S07, FR-S10, FR-S11, FR-S12, FR-S31, FR-S50, FR-S56 |  |
| `cause_detail` | Catalog | #4, #5, #63 | FR-S01, FR-S02, FR-S06, FR-S07, FR-S13, FR-S31, FR-S58, FR-S72 |  |
| `goal_progress` | Catalog | #1, #4, #5, #67, #137 | FR-S01, FR-S04, FR-S06, FR-S07, FR-S12, FR-S34, FR-S51, FR-S56, FR-S58, FR-S64 |  |
| `goal_adjustment` | Catalog | #71, #136, #137 | FR-S56, FR-S58 |  |
| `cause_update` | Catalog | #6, #34, #81, #82, #83, #84 | FR-S06, FR-S25, FR-S59 |  |
| `customer` | Identity | #17, #21, #22, #39, #129, #130, #131, #132 | FR-S17, FR-S21, FR-S22, FR-S27, FR-S53, FR-S54, FR-S79 |  |
| `customer_address` | Identity | #23, #24, #25, #26, #27 | FR-S22, FR-S27, FR-S30 |  |
| `otp_challenge` | Identity | #16, #17 | FR-S21 |  |
| `staff_user` | Identity | #47, #48, #49, #52, #123, #124, #125, #126, #127, #128 | FR-S70, FR-S71 |  |
| `refresh_token` | Identity | #17, #18, #19, #20, #49, #50, #51, #131 | FR-S21, FR-S27, FR-S28, FR-S70, FR-S79 |  |
| `cart` | Cart | #10, #11, #12, #13, #14, #15, #17, #41 | FR-S16, FR-S17, FR-S18, FR-S19, FR-S21, FR-S32, FR-S37, FR-S82 |  |
| `cart_line` | Cart | #10, #12, #13, #14, #15, #40, #41 | FR-S10, FR-S11, FR-S12, FR-S13, FR-S14, FR-S15, FR-S16, FR-S17, FR-S18, FR-S19, FR-S20, FR-S31, FR-S37 |  |
| `orders` | Checkout/Ordering | #35, #36, #41, #42, #43, #85, #86, #87, #89, #91 | FR-S06, FR-S10, FR-S26, FR-S27, FR-S30, FR-S31, FR-S32, FR-S34, FR-S35, FR-S36, FR-S37, FR-S38, FR-S42, FR-S64, FR-S73, FR-S79 |  |
| `order_line` | Checkout/Ordering | #7, #33, #34, #36, #41, #86, #90, #91 | FR-S06, FR-S10, FR-S25, FR-S26, FR-S32, FR-S34, FR-S42, FR-S64, FR-S73 |  |
| `dedication` | Checkout/Ordering | #41, #45, #93 | FR-S06, FR-S25, FR-S32, FR-S35, FR-S60 |  |
| `payment` | Payments | #41, #43, #44, #86 | FR-S26, FR-S33, FR-S34, FR-S35, FR-S36, FR-S37, FR-S38, FR-S39, FR-S64, FR-S73 |  |
| `payment_event` | Payments | #44, #86 | FR-S34, FR-S73 |  |
| `refund` | Payments | #91, #92 | FR-S64, FR-S75 |  |
| `refund_line` | Payments | #91 | FR-S64 |  |
| `invoice` | Documents | #37, #86 | FR-S26, FR-S34, FR-S62, FR-S64, FR-S65, FR-S73 | issued in settlement (#44), credit note in refund (#91) |
| `donation_receipt` | Documents | #37, #86 | FR-S26, FR-S34, FR-S63, FR-S64, FR-S73 | issued in settlement (#44), cancellation in refund (#91) |
| `enrolment` | Fulfilment | #28, #29, #30, #112, #130 | FR-S10, FR-S23, FR-S31, FR-S34, FR-S40, FR-S53, FR-S55, FR-S79 |  |
| `download_grant` | Fulfilment | #31, #32, #46, #94 | FR-S24, FR-S34, FR-S41 |  |
| `download_log` | Fulfilment | #46 | FR-S24, FR-S41 |  |
| `shipment` | Fulfilment | #31, #95, #96, #97, #98, #99 | FR-S24, FR-S34, FR-S42, FR-S75 |  |
| `beneficiary` | Giving | #100, #101, #102, #103, #104, #105, #106, #107, #111, #112 | FR-S52, FR-S53, FR-S54 |  |
| `allocation_batch` | Giving | #112, #113 | FR-S53 |  |
| `sponsored_unit` | Giving | #33, #34, #109, #110, #112, #114 | FR-S25, FR-S34, FR-S51, FR-S53, FR-S55, FR-S57 |  |
| `sponsored_unit_event` | Giving | #34 | FR-S25, FR-S53, FR-S55 |  |
| `sponsorship_pool` | Giving | #69, #71 | FR-S50, FR-S51, FR-S56, FR-S64 |  |
| `pool_contribution` | Giving | #33, #34, #71 | FR-S25, FR-S34, FR-S51, FR-S56, FR-S64 |  |
| `unit_funding` | Giving | — | FR-S34, FR-S51 | ⚠ written by pooling inside settlement (#44); not read by any endpoint |
| `distribution` | Giving | #114 | FR-S53, FR-S55 |  |
| `zakat_ledger` | Giving | #115, #116 | FR-S34, FR-S53, FR-S61, FR-S64 |  |
| `outbox_message` | Notifications | #135 | FR-S34, FR-S40, FR-S42, FR-S54, FR-S57, FR-S59, FR-S60, FR-S80, FR-S83 | ⚠ only the outbox-lag check of `/health/ready`; written by handlers, read by `OutboxDispatcher` |
| `message_template` | Notifications | #120, #121, #122 | FR-S77, FR-S80 |  |
| `notification` | Notifications | #86 | FR-S60, FR-S73, FR-S80, FR-S81 |  |
| `idempotency_key` | Api (09 §5.16) | #41, #43, #91 | FR-S32, FR-S36, FR-S64 |  |

Views: `v_goal` → FR-S01, FR-S02, FR-S07, FR-S74; `v_sales_daily` → FR-S74, FR-S75; `v_allocation_backlog` → FR-S57, FR-S74, FR-S75; `v_zakat_balance` → FR-S61, FR-S75. `v_allocation_backlog` is read by #109; `v_zakat_balance` by #115; `v_goal` by #1–#5; `v_sales_daily` by #53 and #117.

## 5. Open questions → blocked FR-S and sessions

FR-S from 01 §7 "Blocks" and from 04 "Traces"; sessions from 12 "Dependencies on open questions" and Phase 0.

| Q | Question (01 §7) | Blocks FR-S | Blocks sessions (12) |
|---|---|---|---|
| Q-01 | Payment provider; one or two merchant accounts | FR-S30–S37 (01) | Phase 0 payment spike; S-16 (real adapter); S-29 if two accounts (IP-2); S-39 live keys |
| Q-02 | VAT registration; gifts outside VAT scope | FR-S62 (tax invoice form), FR-S65 (04 Traces); BR-S13 | S-15 (VAT maths), S-17 (invoice form), S-33 (VAT report) |
| Q-03 | Zakat-eligible products (Sharia sign-off) | FR-S13, FR-S61 (via BR-S06, 01 and 04) | S-38 (data); causes go-live |
| Q-04 | Beneficiary criteria per offer; who approves allocations | FR-S50–FR-S57 (01) | S-24, S-25, S-27 |
| Q-05 | LMS | FR-S40 (01, 04) | S-19 (Phase 0 LMS decision) |
| Q-06 | Book fulfilment, carrier, rates | FR-S42 (01, 04) | S-21; shipping fee setting |
| Q-07 | Refund policy wording | FR-S64 (01, corrected in this audit from FR-S36) | S-29; launch (legal pages) |
| Q-08 | Data residency | — (01 cites ADR-S03) | Phase 0 VPS provisioning → S-03 |
| Q-09 | Main-site `/donate` moves to the store | FR-S82 (04) | S-36; main-site R3 |

## Gaps

Links that the documents do not provide. None is filled in here; each needs an owner decision or a document edit.

**FR-S that cite no BR-S in 04** (49 of 72), which is why section 1 is thin (e.g. RUL-43…RUL-52 reach no BR-S): FR-S03, FR-S07, FR-S08, FR-S09, FR-S10, FR-S11, FR-S12, FR-S15, FR-S16, FR-S17, FR-S20, FR-S22, FR-S23, FR-S24, FR-S26, FR-S27, FR-S28, FR-S31, FR-S32, FR-S33, FR-S34, FR-S35, FR-S36, FR-S37, FR-S38, FR-S39, FR-S40, FR-S41, FR-S42, FR-S43, FR-S50, FR-S51, FR-S52, FR-S53, FR-S54, FR-S55, FR-S56, FR-S57, FR-S58, FR-S59, FR-S64, FR-S70, FR-S71, FR-S76, FR-S77, FR-S79, FR-S80, FR-S81, FR-S83.

**BR-S with no FR-S citing it in 04:** BR-S14 (Could, S2 by design; FR-S39 BNPL is not BR-S14).

**RUL with no FR-S:** none (RUL-44 traced to FR-S51 on 30 Sep).

**RUL with no US-S:** RUL-44.

**FR-S with no RUL in 04 Traces:** FR-S01, FR-S02, FR-S03, FR-S08, FR-S09, FR-S15, FR-S16, FR-S19, FR-S28, FR-S65, FR-S73, FR-S74, FR-S75, FR-S76, FR-S78, FR-S79, FR-S81, FR-S82, FR-S83.

**FR-S → US-S mismatch inside 05:** the 05 coverage table lists US-S01 under FR-S02, US-S31 under FR-S30 and US-S28 under FR-S79, but those stories' own Traces lines do not cite these FR-S (marked ¹ in section 2).

**FR-S with no endpoint in 11 §12:** FR-S08, FR-S20, FR-S37, FR-S39, FR-S40, FR-S51, FR-S57, FR-S62, FR-S63, FR-S65, FR-S80, FR-S81, FR-S82.

By kind, from the FR-S text in 04: server-side or job only (FR-S37, FR-S40, FR-S51, FR-S57, FR-S80, FR-S81); client-only (FR-S08, FR-S82); S2 (FR-S39, FR-S65); FR-S20 limits are enforced by #12/#13 and FR-S62/FR-S63 PDFs are downloaded through #37, but the index does not cite them.

**FR-S with no session in 12:** FR-S39, FR-S65.

**FR-S with no named test in 09 §10 or 10 §17.3** (contract tests aside): FR-S03, FR-S08, FR-S39, FR-S65, FR-S73, FR-S74, FR-S75, FR-S76, FR-S77, FR-S78, FR-S79, FR-S82, FR-S83.

**Endpoints serving no FR-S in 11 §12:** #18, #94.

**Tables no endpoint touches as a resource:** `document_counter`, `unit_funding`, `outbox_message`.


---

## Consistency audit (29 Sep 2026)

Scope: README, 01–12, adr/, database/ and `001_store_schema.sql`, checked against 03, 04, the DDL and `_changes-r1.md`.

### Clean (no finding)

| Check | Result |
|---|---|
| Reconciliation "every 10 min" / "15 min" | None. Every mention says every 5 min for `initiated` > 5 min, `paid` within about 10 min (RUL-38). The 15 min in 06 §alerts is an alert threshold, not the schedule |
| `getPrerenderParams` as the mechanism | Not present. 06, 08, 09, 10, 11, 12 and ADR-S07 all use the `scripts/routes.mjs` → `routes.txt` extension with build failure (r1-16) |
| Table counts other than 49 | None (README, 08, 12, ADR-S02, database/01 all say 49 tables, 4 views; data dictionary has 49 table sections) |
| Staff cookie path | Always `t3s_srt`, `Path=/v1/admin/auth`; staff auth endpoints under `/v1/admin/auth/*` |
| `order` as a table name | Only in the two notes explaining why the table is `orders` (database/01 §2, 09 §5.13) |
| Column and table names | Every backticked `table.column` and snake_case name matches the DDL, the seed or a documented non-DDL name (settings keys, template keys, error codes, DB roles) |
| State names | Every state in prose, pseudo-SQL and the 07 state diagrams is in a DDL CHECK list |
| "admin (all)" | Not present. All policy tables apply RUL-62 segregation of duties |
| Versions | Only .NET 10, C# 14, EF Core 10, Angular 22, PostgreSQL 18 / `postgres:18`. "PostgreSQL 16/17" appears only as the `uuidv7()` shim note (database/01, ADR-S02) |
| MediatR | Only "no MediatR" statements (06, 09, ADR-S08) |
| Money as float | None. DDL `numeric`, C# `decimal`/`Money`, JSON string decimals, TS `Money` string with integer-halala arithmetic. The provider payload in 11 §7.1 uses integer halalas (provider format) |

### Fixed

| File | Fix |
|---|---|
| 01-business-requirements.md §7 | Q-07 (refund policy wording) blocked `FR-S36` (payment retry); corrected to `FR-S64` (refunds), matching 12 (Q-07 → S-29) |
| 11-api-contract.md §9.9 and §12 #89 | `POST /v1/admin/orders/{ref}/payment-review/resolve` allowed `decision: "refund"` under `staff:finance`, which `admin` satisfies. Now also requires `staff:refund` for the refund decision, per RUL-56/RUL-62 and as 10 §16.2 already shows |
| 08-solution-architecture.md §4.2 (host paths) | Public files were "served via `/v1/media/...`", a path that exists nowhere in 11; changed to `/files/public/...`, the URL form used in every 11 DTO example |

### Resolved after coordinator review (29 Sep 2026)

The coordinator's items A-1…A-4 map to this audit's A-1, A-2, A-4 and A-5.

| # | Decision | Applied in |
|---|---|---|
| A-1 | **Resolved.** Product pages use the batch `GET /v1/catalog/live?slugs={slug}` (11 §2.5, #5). `ProductLive` follows 11's item shape (`slug`, `price`, `compareAtPrice`, `formats[]`, `sponsorship`, `goal`). `owned` is no longer part of the live call: when signed in, it comes from `GET /v1/me/enrolments` (#28), and the cart-add rejection stays the final guard | 07 flow (a), 10 §2.1, 10 §5.2 |
| A-2 | **Resolved.** RUL-29 now allows `fulfilled ──► refunded` directly, matching 07 §f and 09 `Order.ApplyRefund` | 03 RUL-29 |
| A-4 | **Resolved.** `staff:resend` (`admin`, `finance`, `store_manager`) added to the 09 §5.14 policy table | 09 §5.14 |
| A-5 | **Resolved.** The Data Protection key ring is at `/srv/store/keys`, mounted at the same path in the container | 08 §2 and §4.2, 09 §5.13 and §7.2 |
| A-6 | **Partly resolved.** 04 Traces now cite BR-S05 (FR-S14, FR-S60), BR-S06 (FR-S13, FR-S61) and BR-S07 (FR-S18, FR-S30, FR-S62, FR-S63). They also cite RUL-01/02/03 (FR-S72), RUL-09/10 (FR-S50, FR-S58), RUL-23 (FR-S06, FR-S30), RUL-27 (FR-S31, FR-S62), RUL-53 (FR-S13) and RUL-55 (FR-S06). BR-S14 stays S2 with no FR-S. RUL-44 has no fitting FR-S and remains a gap | 04 |

### Still open

| # | Finding | Where | Options |
|---|---|---|---|
| A-3 | 05 coverage table and story Traces disagree: US-S01 listed under FR-S02, US-S31 under FR-S30, US-S28 under FR-S79, but the stories do not cite them | 05 "Coverage: FR-S → US-S" | Add the FR-S to the stories' Traces, or drop them from the coverage table |
| A-6 (rest) | RUL-44 has no FR-S and no story. BR-S14 has no FR-S (S2 by design). FR-S03/08/73–79/82/83 have no named test. See [Gaps](#gaps) | 04, 05, 09 §10, 10 §17.3 | Add a trace for RUL-44 if an FR-S is written for it; add tests or accept contract-test coverage |
| A-7 | Resolved — both codes are correct: `cart.duplicate_course` (already in the cart as `buy`) and `cart.course_owned` (already enrolled) are the final guards (11 §1.6, 10 §5.2) | 10 §5.2, 11 §1.6 | Confirmed |

