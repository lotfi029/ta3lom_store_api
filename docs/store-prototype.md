# ta3lom Store — Prototype R0

**Date:** 29 September 2026 · **Status:** clickable prototype, for client review
**File:** `design/ta3lom-store-prototype.html` (open in a browser; no server needed)
**Visual system:** copied verbatim from `design/ta3lom-prototype.html`: same CSS tokens, header and footer, logo, cards, hero, band and motion. Only store-specific components were added, and they are built from the same tokens.

---

## 1. What the store does

The store sells **courses** and **books**. Any product can also be **bought for others**, meaning donated to beneficiaries the association selects. There are also **donation products**: concrete items with a goal, such as water, devices and data bundles.

| Product type | For yourself | For others |
|---|---|---|
| **Course** | Enrol (one seat, lifetime access) | **Sponsor seats**: N seats, or a partial amount pooled toward a seat |
| **Book** | Print (qty, shipped) or digital PDF | **Sponsor copies** for school libraries / children |
| **Donation product** | — | Donate **N units** (e.g. 2 water coolers) or **any amount** (partial share) · Sadaqah / Zakat |

Every "for others" line can be **dedicated** (تبرع عن غيرك). The dedicated person gets a card by SMS. This is the dedication pattern from Kafeef, not a transfer of the course to that person.

---

## 2. Reference: store.kafeef.sa (scanned 28 Sep 2026)

Kafeef runs on **Salla** with its donation extension. Logic found in the page source:

| Kafeef feature | Source evidence | Adopted in prototype |
|---|---|---|
| Donation product with **target / collected / end date** | `donation:{target, collected_amount, target_end_date, can_donate}` | ✅ progress bar, % and end date on every card |
| **Open amount** on the card itself | `custom_amount_enabled:true`, input "أدخل مبلغ التبرع" | ✅ amount field + "أضف" on each donation card |
| **Unit amount** (fixed price per unit) | `unit_amount` | ✅ `unit` per cause (cooler 1,500 SAR, tablet 950 …) |
| Separate **"سلة التبرعات"** | button "أضف لسلة التبرعات" | ✅ cart split into *Purchases* and *Giving* |
| **"تبرع عن غيرك"** gifting widget | `<salla-gifting widget-title="تبرع عن غيرك">` | ✅ dedication block with name, mobile and card text |
| Share on each product | `salla-social-share` | ✅ share modal |
| "صدقتك وزكاتك" | homepage copy | ✅ Sadaqah / Zakat selector on zakat-eligible causes |
| Donor count per campaign | card counters | ✅ donors count + latest-gifts feed |

**Not in Kafeef, added for ta3lom:** seat sponsorship on courses and books, **allocation tracking** (seat map in *My account*: awaiting → allocated → completed), purchases and donations in one checkout, and delivery updates on causes.

---

## 3. Prototype map

| Route | Page |
|---|---|
| `#/ar/` | Store home: hero, featured accordion, donation opportunities, courses, how sponsorship works, books, impact stats |
| `#/ar/shop/{course\|book\|sponsor\|cause}` | Catalogue with type tabs, category filter and search (`?q=`) |
| `#/ar/p/{slug}` | Product page + buy box (`?m=sponsor` opens sponsor mode) |
| `#/ar/cart` | Cart split into purchases and giving |
| `#/ar/checkout` | Details, shipping (only if printed book), payment |
| `#/ar/done/{orderId}` | Confirmation, receipt, dedication card preview |
| `#/ar/account/{courses\|books\|giving\|orders}` | Library, giving with seat allocation map, receipts |

Every route has an `/en/` equivalent. The "Reset demo" link in the top bar clears the cart and orders stored in the browser.

---

## 4. Business rules shown in the prototype

1. **Course purchase** is one per account and cannot be added twice.
2. **Seat sponsorship**: price per seat = `sponsor.seat`. Presets are 1 / 5 / 10 / 25 plus a stepper.
3. **Partial share**: minimum 10 SAR. The UI shows how many units the share equals (e.g. 100 ÷ 350 = 0.29 seat). Shares are pooled until a unit is fully funded, then allocated.
4. **Donation product**: donate by units (`unit` × qty) or an open amount. The general fund has amount only.
5. **Zakat** can only be chosen on causes flagged `zakat:true`. It is recorded on the line so it can be ring-fenced.
6. **Dedication** requires a recipient name and a valid mobile (`05XXXXXXXX`). The SMS card is sent after payment.
7. **Shipping** is a flat 25 SAR, only when a printed book is in the cart.
8. **Instalments (Tabby)** are blocked if the cart contains any gift.
9. **Receipts**: one order receipt, plus a separate official donation receipt for gift lines.
10. **Anonymous** option ("فاعل خير") hides the donor name in public feeds.
11. Paid gifts immediately raise the product's progress bar and donor count.

---

## 5. Data model proposal (for the build, not in the prototype)

To be added to `database/schema.prisma` when the store is approved:

```prisma
model Product {
  id          String   @id @default(cuid())
  type        ProductType            // COURSE | BOOK | CAUSE
  slug        String   @unique
  titleAr     String
  titleEn     String?
  price       Decimal? @db.Decimal(10,2)  // course; books use formats
  sponsorable Boolean  @default(false)
  seatPrice   Decimal? @db.Decimal(10,2)  // price of one sponsored seat/copy
  unitAmount  Decimal? @db.Decimal(10,2)  // CAUSE: cooler, tablet…
  target      Decimal? @db.Decimal(12,2)  // money for CAUSE, units for sponsorship
  collected   Decimal  @default(0) @db.Decimal(12,2)
  customAmount Boolean @default(true)
  zakatEligible Boolean @default(false)
  endsAt      DateTime?
  beneficiaryGroupAr String?
  beneficiaryGroupEn String?
  published   Boolean  @default(false)
}

model OrderLine {
  id        String   @id @default(cuid())
  orderId   String
  productId String
  kind      LineKind            // BUY | SPONSOR | DONATE
  mode      LineMode            // UNITS | AMOUNT
  format    BookFormat?         // PRINT | DIGITAL
  qty       Int      @default(1)
  unitPrice Decimal  @db.Decimal(10,2)
  amount    Decimal  @db.Decimal(10,2)
  intent    GiftIntent?         // SADAQA | ZAKAT
  dedication Json?              // {to, phone, from, message}
}

model SeatAllocation {             // one row per sponsored seat/copy
  id            String @id @default(cuid())
  orderLineId   String
  beneficiaryId String?            // null = awaiting allocation
  status        AllocationStatus   // AWAITING | ALLOCATED | COMPLETED
  allocatedAt   DateTime?
  completedAt   DateTime?
}
```

Partial shares need a **pool** per product (`SponsorPool { productId, balance }`). When the balance reaches `seatPrice`, a `SeatAllocation` is created and the balance is reduced.

---

## 6. Open questions for the association

1. **Payment gateway.** The same blocker as BR-03 / FR-40–44. Must the store and donations use one merchant account or two?
2. **VAT.** Should course and book prices be shown VAT-inclusive? Confirm with the accountant that gift lines are outside VAT scope. The prototype assumes this.
3. **Zakat eligibility.** Which causes qualify? Get a Sharia-board sign-off.
4. **Beneficiary selection.** Who allocates seats, what are the criteria, and is allocation handled in the admin panel?
5. **Course delivery.** Is there an LMS in-house or an external platform (enrolment by API or by code)?
6. **Book fulfilment.** Who ships printed books, where is stock held, and is 25 SAR the right flat rate?
7. **Refunds.** Is the 7-day / 20%-progress rule acceptable? Donations are non-refundable.
8. **Build choice.** Custom build inside ta3lom-web/api, or Salla like Kafeef? Salla is faster to launch; custom is needed for seat allocation tracking.

---

## 7. Next steps

1. Client walkthrough of the prototype (AR first, then EN)
2. Answer §6 and freeze the scope
3. Add the store requirements (FR-7x) and user stories to `Docs/requirements/`
4. Add the admin screens to the prototype: products, campaigns, allocation queue and orders
