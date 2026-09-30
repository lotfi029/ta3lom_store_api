# 05 — Store User Stories

**Revision:** S1 draft r1 · 29 September 2026 · reconciled with the r1 canonical changes (RUL-13, 20, 29, 35, 38, 46, 47, 50, 52, 57, 61, 62, 63)

User stories for the ta3lom store, grouped by epic. Each story names its persona
([02](02-stakeholders-and-personas.md)), priority (**M** Must · **S** Should · **C** Could, taken
from the functional requirement), release, and traces to `FR-S` ([04](04-functional-requirements.md))
and `RUL-` ([03](03-business-rules.md)). Acceptance criteria are Gherkin. Table and column names
are those of [`001_store_schema.sql`](../../database/store/001_store_schema.sql). Figures come from
the approved prototype and [`002_store_seed.sql`](../../database/store/002_store_seed.sql).

**Shared test background.** Unless a scenario says otherwise:

```gherkin
Background:
  Given the seed data from 002_store_seed.sql is loaded
  And the setting "vat_enabled" is false and "shipping_flat_fee" is 25.00
  And the current time is 2026-10-01 10:00 Asia/Riyadh
  And order references are issued from order_ref_seq, next value 4188
```

Mobiles are written in the customer-facing form `05XXXXXXXX` and stored as E.164 `+9665XXXXXXXX`
(RUL-21). Money in API JSON is a string decimal (`"350.00"`).

## Epics

| Epic | Stories |
|---|---|
| E1 Discover | US-S01 – US-S09 |
| E2 Buy | US-S10 – US-S15 |
| E3 Sponsor | US-S16 – US-S19 |
| E4 Donate | US-S20 – US-S24 |
| E5 Dedicate | US-S25 – US-S27 |
| E6 Checkout & Pay | US-S28 – US-S38 |
| E7 Account | US-S39 – US-S44 |
| E8 Beneficiary | US-S45 – US-S46 |
| E9 Admin catalogue | US-S47 – US-S51 |
| E10 Fulfilment | US-S52 – US-S54 |
| E11 Allocation | US-S55 – US-S60 |
| E12 Finance & refunds | US-S61 – US-S66 |
| E13 Reporting & administration | US-S67 – US-S73 |
| E14 Notifications & integration | US-S74 – US-S77 |
| Added in r1 | US-S78 – US-S80 (see the end of E12 and E6) |

---

## E1 — Discover

### US-S01 — Store home
**As a** visitor (P1) **I want** a store home that shows courses, sponsorships and donation opportunities together **so that** I can see at a glance what I can buy or give.
**Priority:** M · S1 · **Traces:** FR-S01, BR-S12, RUL-02, RUL-08

```gherkin
Scenario: Home sections match the prototype
  When I open /ar/store
  Then I see, in order: hero, featured accordion (5 items), 4 donation cards, 6 course cards,
       "كيف تعمل الكفالة؟", 3 sponsorable courses, books, impact numbers and the general-fund band
  And exactly one h1 "تعلّم لنفسك. واكفل من يتعلّم."

Scenario: Draft products never appear
  Given the product "qudrat-prep" has status "draft"
  When I open /ar/store
  Then "الاستعداد لاختبار القدرات العامة" is not shown
```

### US-S02 — Browse the catalogue
**As a** learner (P1) **I want** type tabs, category chips and sorting **so that** I find a relevant item quickly.
**Priority:** M · S1 · **Traces:** FR-S02, RUL-02

```gherkin
Scenario: Courses filtered by category and sorted by price
  When I open /ar/store/courses and choose the chip "مهارات الدراسة" and sort "price"
  Then I see "الاستعداد لاختبار القدرات العامة" (199 SAR) after "مهارات المذاكرة الذكية" (120 SAR)
  And category chips are loaded from the API, not hard-coded

Scenario: Sponsor tab lists only products with an open offer
  When I open /ar/store/sponsor
  Then "qudrat-prep", "formative-guide" and "study-planner" are not listed
  And "digital-teacher" shows "قيمة مقعد: 350 ر.س"

Scenario: Archived products are not listed
  Given the product "study-planner" has status "archived"
  When I open /ar/store/books
  Then "مخطط المذاكرة السنوي 2027" is not listed
```

### US-S03 — Search in Arabic and English
**As a** visitor **I want** search that tolerates Arabic spelling variants **so that** I find a product however I type it.
**Priority:** S · S1 · **Traces:** FR-S03

```gherkin
Scenario Outline: Arabic variants match
  When I search "<query>"
  Then the results include "<slug>"
  Examples:
    | query            | slug              |
    | المعلم الرقمي    | digital-teacher   |
    | حكايه الحروف     | letters-tales     |
    | الطفوله المبكره  | early-childhood   |
    | python           | python-kids       |

Scenario: Input is normalised the same way as the index
  When I search "حكايه الحروف"
  Then the API normalises the query with ar_normalize() before matching product.search_vector and ix_product_trgm

Scenario: No results
  When I search "رياضيات جامعية"
  Then I see "لا توجد نتائج" and a "عرض الكل" link
```

### US-S04 — Course page
**As a** learner (P1) **I want** a course page with figures, curriculum and instructor **so that** I can decide whether to enrol.
**Priority:** M · S1 · **Traces:** FR-S04, RUL-04

```gherkin
Scenario: Course details
  When I open /ar/store/p/digital-teacher
  Then I see 24 lessons, 18 hours, 1,240 learners, level "مبتدئ – متوسط"
  And 5 curriculum rows starting with "أساسيات الفصل الافتراضي"
  And the instructor "أ. ريم السبيعي"
  And the price 350 ر.س with 450 struck through

Scenario: compare_at_price not higher than price is hidden
  Given course_detail.compare_at_price is NULL for "smart-study"
  When I open its page
  Then no struck-through price is shown
```

### US-S05 — Book page
**As a** parent (P2) **I want** a book page with formats and prices **so that** I choose print or digital.
**Priority:** M · S1 · **Traces:** FR-S05, RUL-05

```gherkin
Scenario: Two formats
  When I open /ar/store/p/science-stars-book
  Then I see 132 pages, author "فريق جمعية التعلم الإلكتروني"
  And "نسخة مطبوعة" 65 ر.س (80 struck through) and "نسخة رقمية PDF" 35 ر.س

Scenario: Print-only book
  When I open /ar/store/p/study-planner
  Then only "نسخة مطبوعة" 40 ر.س is offered
```

### US-S06 — Cause page
**As a** donor (P3) **I want** a cause page with the goal, the unit value, updates and recent gifts **so that** I trust where my money goes.
**Priority:** M · S1 · **Traces:** FR-S06, BR-S04, BR-S08, RUL-08, RUL-23, RUL-54, RUL-55

```gherkin
Scenario: Goal display
  Given goal_progress for "water-for-schools" has seed_raised 61250 and seed_donors 318
  When I open /ar/store/p/water-for-schools
  Then I see "61,250 ر.س من 90,000 ر.س", "68%", "318 متبرع"
  And "قيمة البرّاد: 1,500 ر.س", beneficiaries "60 مدرسة", and the days left until 31 December 2026

Scenario: Progress is capped on the bar but not in the figure
  Given "learning-data" has raised 63,000 of 60,000
  Then the bar is drawn at 100% and the label reads "105%" and "اكتمل الهدف"
  And the page states that extra money buys more units of the same cause

Scenario: Anonymous gifts in the feed
  Given a paid order with orders.is_anonymous = true gave 500 SAR to this cause
  Then the latest-gifts feed shows "فاعل خير — 500 ر.س" and never the donor's name
```

### US-S07 — Live figures on prerendered pages
**As a** store manager (P9) **I want** prices, stock and goals fetched live **so that** a static page is never wrong.
**Priority:** M · S1 · **Traces:** FR-S07, RUL-08, RUL-15, ADR-002

```gherkin
Scenario: Price change without rebuild
  Given /ar/store/p/python-kids was prerendered with price 450
  When the store manager changes course_detail.price to 399
  And I reload the page without a rebuild
  Then the buy box shows 399 ر.س
  And after the next rebuild the prerendered HTML also contains 399

Scenario: API unavailable
  Given the store API does not respond
  When I open the prerendered course page
  Then the page content renders and the buy box shows "تعذّر تحميل السعر، حاول لاحقاً" instead of a price
```

### US-S08 — Share a product
**As a** donor (P3) **I want** to share a cause **so that** my friends give too.
**Priority:** S · S1 · **Traces:** FR-S08

```gherkin
Scenario: Share keeps the locale
  Given I am on /en/store/p/water-for-schools
  When I choose "Share" and then "Copy link"
  Then the clipboard contains "https://ta3lom.sa/en/store/p/water-for-schools"
  And WhatsApp, X, Telegram and Email options are offered
```

### US-S09 — Search-engine metadata
**As the** programs team **I want** every catalogue page indexed correctly **so that** people find our causes from search.
**Priority:** M · S1 · **Traces:** FR-S09

```gherkin
Scenario: Metadata per product type
  When I fetch the prerendered HTML of /ar/store/p/digital-teacher
  Then it has one title, a meta description, a canonical URL, og:image = the cover
  And JSON-LD of type "Course"
  And /ar/store/p/water-for-schools carries JSON-LD "DonateAction"
  And /store/card/{token}, /store/cart and /store/checkout carry "noindex"

Scenario: Product slugs come from the store API at build time
  When ta3lom-web is built
  Then scripts/routes.mjs adds the store slugs from GET /v1/catalog/products?fields=slug to routes.txt
  And /ar/store/p/water-for-schools and /en/store/p/water-for-schools are prerendered

Scenario: Store API unreachable at build time
  Given GET /v1/catalog/products?fields=slug fails during the build
  Then the build fails and nothing is deployed
```

---

## E2 — Buy

### US-S10 — Enrol myself in a course
**As a** learner (P1) **I want** to add a course for myself **so that** I can pay and start.
**Priority:** M · S1 · **Traces:** FR-S10, FR-S16, RUL-12, RUL-13

```gherkin
Scenario: Add a course
  When I choose "اشترك لنفسك" on digital-teacher and "أضف إلى السلة"
  Then the cart has a line kind "buy", mode "units", quantity 1, amount "350.00"

Scenario: Buy now
  When I choose "اشترِ الآن"
  Then the line is added and I am on /ar/store/checkout

Scenario: Duplicate course in the cart
  Given my cart already has digital-teacher as "buy"
  When I add digital-teacher as "buy" again
  Then I see "الدورة موجودة في السلة بالفعل" and the cart still has 1 line

Scenario: Course already owned
  Given I have an enrolment with status "active" for digital-teacher
  When I add digital-teacher as "buy"
  Then the request is rejected with "لديك هذه الدورة بالفعل"

Scenario: Sponsoring an owned course is allowed
  Given I own digital-teacher
  When I sponsor 1 seat in digital-teacher
  Then the line is added with kind "sponsor"

Scenario: No sponsor tab without an open offer
  When I open qudrat-prep
  Then only "اشترك لنفسك" is shown
```

### US-S11 — Buy a book in print or digital
**As a** parent (P2) **I want** to choose a format and a quantity **so that** I get the copies I need.
**Priority:** M · S1 · **Traces:** FR-S11, RUL-05, RUL-12, RUL-14

```gherkin
Scenario: Two printed copies
  When I choose "نسخة مطبوعة" for science-stars-book, quantity 2, "أضف إلى السلة"
  Then the line is kind "buy", book_format sku "BK-SCIENCE-STAR-P", quantity 2, amount "130.00"

Scenario: Adding the same print line again merges
  When I add 1 more printed copy of science-stars-book
  Then the cart still has one print line, quantity 3, amount "195.00"

Scenario: Digital is always quantity 1
  When I choose "نسخة رقمية PDF" for letters-tales
  Then no quantity stepper is shown and the line has quantity 1, amount "25.00"

Scenario: Print quantity limit
  When I set the print quantity to 21
  Then I see "الحد الأقصى 20 نسخة في السطر" and the quantity stays 20
```

### US-S12 — Keep my cart as a guest and after signing in
**As a** visitor **I want** my cart saved on the server **so that** it survives a reload and follows me after sign-in.
**Priority:** M · S1 · **Traces:** FR-S17, RUL-11, RUL-14, RUL-16, RUL-17

```gherkin
Scenario: Guest cart cookie
  When I add a line as a guest
  Then a cart row is created with customer_id NULL and expires_at = now + 30 days
  And the response sets the HttpOnly cookie "t3s_cart"

Scenario: Merge on sign-in
  Given my guest cart has 1 printed science-stars-book
  And my customer cart has 2 printed science-stars-book
  When I sign in with OTP
  Then my customer cart has one line of quantity 3
  And the guest cart status is "merged"

Scenario: Owned course dropped on merge
  Given my guest cart has digital-teacher as "buy" and I already own it
  When I sign in
  Then the line is removed and I see "أُزيلت دورة تملكها بالفعل"

Scenario: Guest cart expiry
  Given a guest cart last changed 31 days ago
  When the ExpireGuestCarts job runs at 03:00
  Then the cart and its lines are deleted
```

### US-S13 — Review my cart
**As a** customer **I want** purchases and gifts shown separately with a clear total **so that** I know what I pay for and what I give.
**Priority:** M · S1 · **Traces:** FR-S18, RUL-14, RUL-26, RUL-31

```gherkin
Scenario: Mixed cart totals
  Given my cart has:
    | product           | kind    | mode   | qty | amount   |
    | digital-teacher   | buy     | units  | 1   | 350.00   |
    | science-stars-book print | buy | units | 2 | 130.00 |
    | digital-teacher   | sponsor | units  | 5   | 1750.00  |
    | water-for-schools | donate  | amount | 1   | 100.00   |
  When I open /ar/store/cart
  Then "مشترياتك" lists 2 lines and "سلة التبرعات والكفالات" lists 2 lines
  And the summary shows purchases 480.00, shipping 25.00, gifts 1,850.00, total 2,355.00
  And a note says a separate official donation receipt is issued for gifts

Scenario: Gifts alone never add shipping
  Given my cart has only a donate line of 100.00
  Then shipping is not shown and the total is 100.00

Scenario: Amount lines have no stepper
  Then the water-for-schools amount line shows "مبلغ مفتوح" and no quantity stepper
```

### US-S14 — Cart badge everywhere on ta3lom.sa
**As a** visitor **I want** to see my cart count on every page of ta3lom.sa **so that** I can return to it.
**Priority:** M · S1 · **Traces:** FR-S19, FR-S82

```gherkin
Scenario: Badge on the main site
  Given my cart has 3 lines
  When I open https://ta3lom.sa/ar/news
  Then the header cart badge shows 3, read from GET /v1/cart/summary with credentials

Scenario: Store API unavailable
  Given /v1/cart/summary times out
  Then the badge is hidden and no error is shown
```

### US-S15 — Line and cart limits
**As the** association **we want** limits enforced with clear messages **so that** large gifts are handled offline.
**Priority:** M · S1 · **Traces:** FR-S20, RUL-12, RUL-18

```gherkin
Scenario: Cart total ceiling
  Given my cart total is 99,500.00
  When I add 1 water cooler (1,500.00)
  Then the request is rejected with "الحد الأعلى للسلة 100,000 ر.س — تواصل معنا للتبرعات الكبيرة"

Scenario: 51st line
  Given my cart has 50 lines
  When I add another line
  Then it is rejected with the cart-limit message

Scenario: Sponsor quantity ceiling
  When I set 1,000 seats on digital-teacher
  Then the quantity is limited to 999
```

---

## E3 — Sponsor

### US-S16 — Sponsor whole seats
**As a** sponsor (P4) **I want** to pay for N seats **so that** N teachers learn on me.
**Priority:** M · S1 · **Traces:** FR-S12, RUL-06, RUL-12, RUL-43

```gherkin
Scenario: Preset 25 seats
  Given the offer on digital-teacher has seat_price 350, group "معلمات المدارس الأهلية الناشئة في القرى والهجر"
  When I choose "اكفل مقاعد", preset 25, "أضف لسلة التبرعات"
  Then the line is kind "sponsor", mode "units", quantity 25, amount "8750.00", intent "sadaqa"

Scenario: Five seats
  When I choose 5 seats
  Then the buy box shows "5 × 350 ر.س" and total 1,750 ر.س

Scenario: Offer ended
  Given the offer on family-remote ends at 2026-10-31 23:59:59+03
  And the current time is 2026-11-01 09:00 Asia/Riyadh
  When I open family-remote
  Then the sponsor tab shows "انتهت الحملة" and cannot add lines
```

### US-S17 — Contribute any amount to seats (pooled)
**As a** donor **I want** to give less than a seat **so that** my share is combined with others until a seat is funded.
**Priority:** M · S1 · **Traces:** FR-S12, FR-S51, RUL-12, RUL-45, RUL-46

```gherkin
Scenario: Amount shows the seat equivalent
  When I choose "ساهم بمبلغ" on digital-teacher and enter 100
  Then I see "مساهمتك تعادل 0.29 مقعداً"
  And the line is kind "sponsor", mode "amount", quantity 1, amount "100.00"
  And the buy box states that a remainder at campaign end goes to the general education fund

Scenario: Below the minimum
  When I enter 9
  Then I see "أدخل مبلغاً لا يقل عن 10 ر.س" and nothing is added

Scenario: Partial not allowed
  Given sponsorship_offer.allow_partial is false for smart-study
  Then the "ساهم بمبلغ" option is not shown
```

### US-S18 — Sponsor book copies for libraries
**As a** sponsor **I want** to fund book copies **so that** school libraries receive them.
**Priority:** M · S1 · **Traces:** FR-S11, FR-S12, RUL-06

```gherkin
Scenario: Copy presets
  When I choose "اكفل نسخاً" on science-stars-book
  Then presets 10, 25, 50, 100 are offered and the unit is "نسخة" at 40 ر.س
  When I choose 50
  Then the line amount is "2000.00" and the goal reads "612 نسخة من 1,000 نسخة"
```

### US-S19 — Follow my sponsored units
**As a** sponsor (P4) **I want** to see each seat's status **so that** I know my gift reached someone.
**Priority:** M · S1 · **Traces:** FR-S25, BR-S08, RUL-50

```gherkin
Scenario: Seat map
  Given order T3-26-004188 funded 25 units on digital-teacher
  And 5 units are "allocated" and 2 of them "completed"
  When I open /ar/store/account/giving
  Then the seat map shows 2 completed, 3 allocated, 20 awaiting allocation
  And each allocated seat shows the group and region "القصيم", never a name or mobile

Scenario: Pooled share
  Given my 100 SAR pool contribution on digital-teacher funded part of one unit
  Then the line shows "مجمّعة — 0.29 من مقعد" and the unit status once created

Scenario: Swept remainder
  Given my 40 SAR contribution on family-remote was swept at campaign end
  Then the line shows "حُوّل إلى صندوق دعم التعليم عند انتهاء الحملة"
```

---

## E4 — Donate

### US-S20 — Quick give from a donation card
**As a** mobile donor (P3) **I want** to type an amount on the card and add it **so that** I give in seconds.
**Priority:** M · S1 · **Traces:** FR-S15, FR-S16, RUL-12

```gherkin
Scenario: Quick give 50 SAR
  When I type 50 in the water-for-schools card and press "أضف"
  Then a line kind "donate", mode "amount", amount "50.00", intent "sadaqa" is added
  And a toast offers "عرض السلة"

Scenario: Below the cause minimum
  When I type 5
  Then I see "أدخل مبلغاً لا يقل عن 10 ر.س"
```

### US-S21 — Give whole units of a cause
**As a** donor (P6) **I want** to give 2 coolers **so that** my gift is concrete.
**Priority:** M · S1 · **Traces:** FR-S13, RUL-07, RUL-12, RUL-53

```gherkin
Scenario: Units
  When I choose "بعدد البرّاد", preset 2 on water-for-schools
  Then the total is 3,000 ر.س and the line is mode "units", quantity 2, amount "3000.00"

Scenario: Amount presets
  When I choose "ساهم بمبلغ"
  Then presets 50, 100, 250, 500 ر.س are shown and 250 shows "تعادل 0.17 برّاد"
```

### US-S22 — Pay zakat on an eligible cause
**As a** zakat payer (P5) **I want** to mark my gift as zakat **so that** it is ring-fenced.
**Priority:** M · S1 · **Traces:** FR-S13, FR-S61, RUL-19, RUL-20

```gherkin
Scenario: Zakat on tablets
  Given cause_detail.zakat_eligible is true for tablet-for-student
  When I choose 4 tablets and "زكاة"
  Then I see "تُصرف الزكاة لمستحقيها من الطلاب المؤهلين فقط، ولا تُخلط بالتبرعات العامة."
  And the line is intent "zakat", amount "3800.00"

Scenario: Zakat not offered on a non-eligible cause
  When I open water-for-schools
  Then no sadaqa/zakat selector is shown

Scenario: API rejects zakat on a non-eligible cause
  When a client posts a donate line for water-for-schools with intent "zakat"
  Then the API returns 422 with code "zakat_not_eligible" and nothing is added
```

### US-S23 — Give to the general education fund
**As a** donor who is unsure where to give **I want** to give any amount to the general fund **so that** the association uses it where the need is greatest.
**Priority:** M · S1 · **Traces:** FR-S13, RUL-07, FR-S82

```gherkin
Scenario: Amount only
  When I open /ar/store/p/education-fund
  Then there is no units option, only amount presets and input
  And the line is mode "amount"

Scenario: Main-site donate link
  When I follow "تبرع" on https://ta3lom.sa/ar/donate
  Then I land on /ar/store/p/education-fund
```

### US-S24 — Campaign ended between cart and checkout
**As the** association **we want** ended campaigns to stop taking money **so that** we honour the published end date.
**Priority:** M · S1 · **Traces:** FR-S13, FR-S31, RUL-09, RUL-10, RUL-24

```gherkin
Scenario: Cause ended while in the cart
  Given my cart has 120.00 for learning-data, added on 2026-11-15
  And the cause ends at 2026-11-15 23:59:59+03
  When I submit checkout on 2026-11-16 08:00 Asia/Riyadh
  Then the response lists the line with reason "campaign_ended"
  And no order and no payment are created

Scenario: Goal reached does not close by default
  Given water-for-schools has raised 90,000 of 90,000 and close_on_target is false
  When I give 100 SAR
  Then the line is accepted

Scenario: Goal reached closes when configured
  Given close_on_target is true and the target is reached
  Then the buy box shows "اكتمل الهدف" and cannot add lines
```

---

## E5 — Dedicate

### US-S25 — Give on behalf of someone
**As a** donor (P6) **I want** to dedicate my gift **so that** my mother receives a card in my late father's memory.
**Priority:** M · S1 · **Traces:** FR-S14, RUL-21, BR-S05

```gherkin
Scenario: Valid dedication
  When I tick "تبرع عن غيرك — أهدِ الأجر" on 2 coolers
  And I enter recipient "منيرة", mobile "0551234567", sender "فهد", template "صدقة جارية عن روح الفقيد"
  And I add to giving
  Then the cart line carries dedication {toName "منيرة", toMobile "+966551234567", fromName "فهد", templateKey "departed"}
  And the cart shows "إهداء إلى منيرة · بطاقة إلى 0551234567"

Scenario: Invalid mobile
  When I enter mobile "551234567"
  Then I see "أكمل اسم المُهدى إليه ورقم جواله (05XXXXXXXX)" and nothing is added

Scenario: Name too short
  When I enter recipient "م"
  Then the dedication is rejected (2–60 characters)

Scenario: Dedicated lines never merge
  Given the cart has 1 cooler dedicated to "منيرة"
  When I add 1 more cooler dedicated to "منيرة"
  Then the cart has two separate lines

Scenario: Purchases cannot be dedicated
  Then the "Enrol myself" and "Buy a copy" tabs show no dedication toggle
  And the API rejects a buy line carrying a dedication with 422

Scenario: Free text disabled by default
  Given dedication_free_text_enabled is false
  Then only the 4 approved templates are selectable
```

### US-S26 — Receive a dedication card
**As a** dedication recipient (P7) **I want** an SMS with a link to a card **so that** I know someone gave on my behalf.
**Priority:** M · S1 · **Traces:** FR-S60, RUL-22, RUL-23, RUL-63

```gherkin
Scenario: Card sent after payment only
  Given order T3-26-004189 has a dedicated line to +966551234567
  When the order becomes "paid"
  Then one DedicationCard outbox_message is written in the settlement transaction
  And the dispatcher sends the SMS "منيرة، تبرّع فهد عنك في «سقيا الطلاب — برادات مياه للمدارس». بطاقتك: https://ta3lom.sa/ar/store/card/{token}"
  And dedication.sms_status becomes "sent" with sent_at

Scenario: Payment failed
  Given the payment for the order failed
  Then no SMS is sent and dedication.sms_status stays "pending"

Scenario: Card page shows no amount
  When the recipient opens /ar/store/card/{card_token}
  Then the page shows her name, the message, the sender name and the product title
  And no amount is shown anywhere on the page
  And the page has "noindex" and needs no login

Scenario: Anonymous donor without a sender name
  Given the order is anonymous and from_name is empty
  Then the card reads "فاعل خير" as the sender

Scenario: Unknown token
  When someone opens /ar/store/card/AAAAAAAAAAAAAAAAAAAAAA
  Then the response is 404
```

### US-S27 — Resend a dedication card
**As a** store manager (P9) **I want** to resend a card **so that** a recipient who lost the SMS gets it again.
**Priority:** M · S1 · **Traces:** FR-S60, FR-S73

```gherkin
Scenario: Resend
  Given dedication.sms_status is "failed" on order T3-26-004189
  When I choose "إعادة إرسال البطاقة"
  Then a new notification row is queued with template_key "dedication_card"
  And an audit_log row with action "dedication.resend" is written
```

---

## E6 — Checkout & Pay

### US-S28 — Sign in with my mobile
**As a** customer (P1) **I want** to sign in with an SMS code **so that** I never manage a password.
**Priority:** M · S1 · **Traces:** FR-S21, RUL-60, RUL-61, RUL-63, BR-S09

```gherkin
Scenario: Successful OTP
  When I request a code for 0501234567 at checkout
  Then an otp_challenge row stores code_hash (not the code), expires_at = now + 5 minutes
  And the SMS is sent directly within the request, not through outbox_message, so the plain code is stored nowhere (RUL-63)
  And I receive "رمز الدخول لمتجر تعلّم: 482913 — صالح 5 دقائق. لا تشاركه مع أحد."
  When I enter 482913
  Then I receive an access token (15 minutes) and the "t3s_rt" cookie scoped to /v1/auth
  And a customer row exists with mobile "+966501234567" and pdpl_consent_at set

Scenario: Wrong code 5 times
  When I enter a wrong code 5 times
  Then the 6th attempt returns "انتهت محاولات هذا الرمز، اطلب رمزاً جديداً"

Scenario: Code expired
  When I enter the correct code 6 minutes later
  Then it is rejected as expired

Scenario: Per-mobile limit
  Given 3 codes were requested for 0501234567 in the last 15 minutes
  When I request a 4th
  Then the API returns 429 with a Retry-After header

Scenario: Per-IP limit
  Given 10 codes were requested from my IP in the last hour
  When I request an 11th for another number
  Then the API returns 429

Scenario: Blocked customer
  Given customer.status is "blocked" for +966501234567
  When I try to sign in
  Then sign-in is refused and I see a message to contact the association (RUL-61)
```

### US-S29 — Enter checkout details
**As a** customer **I want** a short checkout that asks only what my cart needs **so that** I finish quickly.
**Priority:** M · S1 · **Traces:** FR-S30, RUL-23, RUL-25, RUL-26

```gherkin
Scenario: Gift-only cart
  Given my cart has only a donate line
  Then checkout shows name, verified mobile, email, the "فاعل خير" option, payment method, terms
  And no shipping address section

Scenario: Print line requires an address
  Given my cart has a printed book
  Then a shipping address section is shown with city and short national address "RRRD2929"
  When I submit a short address "RRR2929"
  Then it is rejected (format ^[A-Z]{4}[0-9]{4}$)

Scenario: Terms must be accepted
  When I press "ادفع 2,355 ر.س" without ticking the terms
  Then the form is not submitted and the checkbox is flagged
  And the API rejects a checkout without terms acceptance

Scenario: Acceptance is recorded
  When I tick the terms and pay
  Then orders.terms_accepted_at holds the time of acceptance

Scenario: Email required
  When I leave the email empty
  Then I see that the email is required for receipts
```

### US-S30 — Server validation and repricing before payment
**As the** association **we want** every line re-checked on the server **so that** nothing is charged on stale or invalid data.
**Priority:** M · S1 · **Traces:** FR-S31, RUL-13, RUL-15, RUL-24, RUL-61

```gherkin
Scenario: Price changed since adding to cart
  Given my cart line for python-kids shows unit_price 450.00
  And course_detail.price is now 399.00
  When I submit POST /v1/checkout
  Then the response has priceChanged true and the new total
  And no order is created until I confirm the new total

Scenario: Stock out
  Given book_format "BK-LETTERS-TALE-P" has stock_on_hand 3 and stock_reserved 2
  And my cart has 2 printed letters-tales
  When I submit checkout
  Then the line fails with reason "out_of_stock" and available 1
  And nothing is charged

Scenario: Duplicate course ownership detected at checkout
  Given I bought digital-teacher on another device after adding it here
  When I submit checkout
  Then the line fails with reason "already_enrolled"

Scenario: Course already in one of my unpaid orders
  Given my order T3-26-004191 is "payment_failed" and contains digital-teacher as "buy"
  When I submit a new checkout with digital-teacher as "buy"
  Then the line fails with a reason pointing me to retry T3-26-004191 (RUL-13)

Scenario: Blocked customer cannot check out
  Given customer.status is "blocked"
  Then POST /v1/checkout is refused (RUL-61)

Scenario: Product archived
  Given the product of a cart line is now "archived"
  Then the line fails with reason "not_available"

Scenario: All lines must pass
  Given one of three lines fails
  Then no order row is created and the response lists only the failing line with its reason
```

### US-S31 — Create the order
**As a** customer **I want** an order reference as soon as I pay **so that** I can quote it.
**Priority:** M · S1 · **Traces:** FR-S32, RUL-28, RUL-29, RUL-30, RUL-31

```gherkin
Scenario: Order snapshot
  Given my cart from US-S13 passes validation
  When I submit POST /v1/checkout with an Idempotency-Key
  Then an orders row is created with reference "T3-26-004188", status "pending_payment"
  And purchases_total 480.00, shipping_total 25.00, gifts_total 1850.00, grand_total 2355.00
  And payment_due_at = placed_at + 60 minutes
  And 4 order_line rows copy title_ar and unit_price
  And book_format "BK-SCIENCE-STAR-P" stock_reserved increases by 2
  And orders.terms_accepted_at is set
  And a payment row with status "initiated" is created
  And the cart status becomes "converted"
  And the response carries a per-order status token (HMAC of the order id)

Scenario: Idempotent submit
  When the same request is sent twice with the same Idempotency-Key
  Then an idempotency_key row (scope "checkout:{customerId}") stores the request hash and response
  And only one order exists and both responses carry "T3-26-004188"

Scenario: Same key, different body
  When the Idempotency-Key is reused with a different cart
  Then the API returns 422 and creates nothing
```

### US-S32 — Pay on the hosted payment page
**As a** customer (P3) **I want** to pay with mada, Apple Pay, card or STC Pay **so that** I use what I have.
**Priority:** M · S1 · **Traces:** FR-S33, RUL-32, RUL-36

```gherkin
Scenario: Redirect to the gateway
  When the order is created
  Then I am sent to the gateway's hosted page or SDK sheet for 2,355.00 SAR
  And no card number or CVV is posted to store-api.ta3lom.sa

Scenario: BNPL hidden when gifts exist
  Given my cart has any sponsor or donate line
  Then Tabby/Tamara are not offered
```

### US-S33 — Settle an order from the gateway webhook
**As the** association **we want** only a verified webhook (or reconciliation) to mark an order paid **so that** nobody fakes a payment.
**Priority:** M · S1 · **Traces:** FR-S34, RUL-13, RUL-33, RUL-34, RUL-39, RUL-43, RUL-59

```gherkin
Scenario: Valid webhook
  Given order T3-26-004188 is "pending_payment" with grand_total 2355.00
  When POST /v1/payments/webhooks/moyasar arrives with a valid signature, event id "evt_9f1", captured 2355.00
  Then in one transaction:
    | effect                                                                  |
    | payment_event row stored with signature_valid true                      |
    | payment.status = captured, orders.status = paid, paid_at set            |
    | enrolment (source purchase) for digital-teacher                         |
    | book_format stock_on_hand −2, stock_reserved −2, stock_movement "sale"  |
    | shipment status "preparing"                                             |
    | 5 sponsored_unit rows "awaiting_allocation" for the sponsor line        |
    | goal_progress for water-for-schools raised +100.00, donor_count +1      |
    | invoice INV-2026-000001 and donation receipt DR-2026-000001             |
    | outbox OrderPaid and RenderDocuments                                     |
  And the endpoint returns 200

Scenario: Replayed webhook
  When the same event "evt_9f1" is delivered again
  Then the unique (provider, provider_event_id) stops a second insert
  And the endpoint returns 200 and nothing else changes (5 units, not 10)

Scenario: Invalid signature
  When a webhook arrives with a wrong signature
  Then the endpoint returns 401, the attempt is logged, and the order stays "pending_payment"

Scenario: A duplicate course is paid in a race
  Given I already hold an active enrolment in digital-teacher from order T3-26-004187
  And order T3-26-004188 with digital-teacher as "buy" is captured
  When the settlement runs
  Then no second enrolment is created
  And the line is marked for refund and a finance alert is written (RUL-13)

Scenario: The browser return URL is not proof
  When the browser returns to /ar/store/orders/T3-26-004188/thanks before any webhook
  Then the order stays "pending_payment"
```

### US-S34 — Amount mismatch goes to payment review
**As a** finance officer (P10) **I want** mismatched captures held **so that** nothing is fulfilled on a wrong amount.
**Priority:** M · S1 · **Traces:** FR-S38, RUL-29, RUL-35

```gherkin
Scenario: Captured less than the total
  Given order T3-26-004190 has grand_total 1500.00
  When a valid webhook reports captured 150.00
  Then orders.status becomes "payment_review"
  And no enrolment, unit, goal update, document or dedication SMS is created
  And a FinanceAlert outbox message is written

Scenario: Capture arrives after expiry
  Given order T3-26-004193 became "expired" at 11:05
  When a valid webhook reports a capture of its full grand_total at 11:20
  Then the order becomes "payment_review", not "paid" (RUL-35)

Scenario: Capture on a cancelled order
  Given order T3-26-004194 is "cancelled" by staff
  When a capture is reported
  Then the order becomes "payment_review"
```

### US-S35 — See the result after paying
**As a** customer **I want** a thank-you page that tells me the truth **so that** I know if I paid.
**Priority:** M · S1 · **Traces:** FR-S35, RUL-33, RUL-37

```gherkin
Scenario: Paid
  Given I return to /ar/store/orders/T3-26-004188/thanks
  When GET /v1/orders/T3-26-004188/status turns "paid"
  Then I see "تقبّل الله منك، ماجد", the receipt summary, the dedication card preview and "اذهب إلى حسابي"

Scenario: Still pending
  Given the webhook has not arrived
  Then the page polls and shows "جارٍ تأكيد الدفع" without claiming success

Scenario: Failed
  Given the payment failed
  Then I see the failure and a "إعادة المحاولة" button

Scenario: Status needs the customer's token
  When GET /v1/orders/T3-26-004188/status is called with neither my bearer token nor the order's status token
  Then the API refuses it; the reference alone never reveals an order
  And the thanks page uses my bearer token, since I signed in at checkout
```

### US-S36 — Retry a failed payment
**As a** customer **I want** to retry on the same order **so that** I do not rebuild my cart.
**Priority:** M · S1 · **Traces:** FR-S36, RUL-37

```gherkin
Scenario: Retry within 60 minutes
  Given order T3-26-004191 is "payment_failed", placed 20 minutes ago
  When I choose "إعادة المحاولة"
  Then a second payment row "initiated" is created for the same order
  And the reference stays T3-26-004191

Scenario: Retry after the timeout
  Given the order was placed 61 minutes ago
  Then retry is refused and the order is "expired"
```

### US-S37 — Recover missed webhooks and expire unpaid orders
**As the** association **we want** background jobs to settle or expire orders **so that** no payment is lost and no stock stays reserved.
**Priority:** M · S1 · **Traces:** FR-S37, RUL-29, RUL-30, RUL-38

```gherkin
Scenario: Browser closed, every webhook lost
  Given I paid order T3-26-004192 at 10:00 and no webhook arrives
  And the gateway reports the payment captured
  When ReconcilePayments runs every 5 minutes for payments "initiated" more than 5 minutes ago
  Then the settlement transaction runs and the order is "paid" by about 10:10 (RUL-38)

Scenario: Reconciliation window
  Given a payment still "initiated" 25 hours after payment_due_at
  Then ReconcilePayments no longer queries it

Scenario: Unpaid order expires
  Given order T3-26-004193 is "pending_payment" and payment_due_at has passed
  When ExpireUnpaidOrders runs
  Then the order becomes "expired" and stock_reserved is released
  And its lines are restored to my active cart, except products already in it (RUL-29)
```

### US-S38 — Pay in instalments for purchases *(S2)*
**As a** learner **I want** Tabby or Tamara for a purchase-only cart **so that** I spread the cost.
**Priority:** C · S2 · **Traces:** FR-S39, RUL-36

```gherkin
Scenario: Purchase-only cart
  Given my cart has only python-kids (450.00)
  Then BNPL is offered
Scenario: Mixed cart
  Given my cart also has a 50.00 donation
  Then BNPL is not offered and a posted method "bnpl" is rejected with 422
```

---

## E7 — Account

### US-S39 — Manage my profile and addresses
**As a** customer (P2) **I want** saved details and addresses **so that** checkout is faster.
**Priority:** M · S1 · **Traces:** FR-S22, RUL-61

```gherkin
Scenario: Default address
  Given I have two addresses
  When I mark the Dammam address as default
  Then only that customer_address has is_default true

Scenario: Default anonymity
  Given anonymous_default is true
  Then the checkout "فاعل خير" option is pre-ticked

Scenario: Marketing consent
  Then marketing_opt_in defaults to false and is changed only by me
```

### US-S40 — My courses
**As a** learner (P1) **I want** my courses with progress **so that** I resume where I stopped.
**Priority:** M · S1 · **Traces:** FR-S23, RUL-42

```gherkin
Scenario: Enrolled course
  Given LmsProgressSync (hourly) set progress_pct 40 on my active enrolment in smart-study
  Then My courses shows "40% مكتمل" and "تابع"

Scenario: Pending LMS sync
  Given my enrolment is "pending_sync"
  Then the course shows "جارٍ تفعيل الوصول" instead of a start button

Scenario: Certificate
  Given the enrolment is "completed" with certificate_no
  Then a certificate link is shown
```

### US-S41 — My books and downloads
**As a** parent (P2) **I want** to download my digital book and track printed ones **so that** I get what I paid for.
**Priority:** M · S1 · **Traces:** FR-S24, FR-S41, RUL-41

```gherkin
Scenario: Download with watermark
  Given a download_grant for letters-tales with download_count 0, max_downloads 5
  When I press "تحميل"
  Then I receive a signed URL valid for 10 minutes
  And the PDF carries my name, masked mobile "05•••••567" and "T3-26-004188"
  And download_count becomes 1 and a download_log row is written

Scenario: Limit reached
  Given download_count is 5
  Then the button is disabled with "استنفدت مرات التحميل — تواصل معنا"

Scenario: Link used after 10 minutes
  When I open the signed URL 11 minutes later
  Then the response is 403

Scenario: Print status
  Given my shipment is "shipped" with tracking "SMSA123456"
  Then My books shows "في الطريق" and the tracking number
```

### US-S42 — Orders and receipts
**As a** customer **I want** all my orders and documents **so that** I keep records.
**Priority:** M · S1 · **Traces:** FR-S26, RUL-59

```gherkin
Scenario: Mixed order documents
  Given order T3-26-004188 is paid with purchase and gift lines
  Then My orders shows reference, date, total 2,355 ر.س, status
  And two downloads: the invoice INV-2026-000001 and the donation receipt DR-2026-000001

Scenario: Another customer's order
  When I request the documents of an order I do not own
  Then the API returns 404
```

### US-S43 — Export or delete my data
**As a** customer **I want** to export or delete my data **so that** my PDPL rights are respected.
**Priority:** M · S1 · **Traces:** FR-S27, RUL-61

```gherkin
Scenario: Export
  When I call GET /v1/me/export
  Then I receive JSON with profile, addresses, orders, giving and enrolments

Scenario: Delete
  When I confirm "حذف حسابي"
  Then customer.status becomes "anonymised", name, email and mobile are replaced
  And my addresses are deleted and all refresh tokens revoked
  And my orders keep amounts with contact_* set to "محذوف / deleted"
  And the donation receipts and invoices remain
```

### US-S44 — Sign out everywhere
**As a** customer **I want** to sign out on all devices **so that** a lost phone cannot use my account.
**Priority:** S · S1 · **Traces:** FR-S28

```gherkin
Scenario: Revoke the family
  When I choose "تسجيل الخروج من جميع الأجهزة"
  Then every refresh_token of mine has revoked_at set
  And the next refresh from another device returns 401
```

---

## E8 — Beneficiary

### US-S45 — Accept a sponsored seat
**As a** rural teacher (P8) **I want** an SMS invitation and a simple first login **so that** I start the course without paying.
**Priority:** M · S1 · **Traces:** FR-S54, RUL-47, RUL-52, RUL-60

```gherkin
Scenario: Account prepared at allocation
  Given beneficiary B-000123 (teacher, group rural_private_school_teachers, mobile +966561112233) is allocated a digital-teacher seat
  Then in the allocation transaction the system finds or creates the customer row for +966561112233
  And creates the enrolment (source "sponsored") owned by that customer
  And sets beneficiary.customer_id (RUL-47, RUL-52)
  And she receives "مرحباً، خُصّص لك مقعد في دورة «المعلّم الرقمي: أدوات التعليم الإلكتروني» من جمعية التعلم الإلكتروني. ابدأ: {link}"

Scenario: First login simply signs in
  When she signs in with OTP on 0561112233 for the first time
  Then no new customer row is created
  And My courses shows digital-teacher with no price or payment step

Scenario: Minor with a guardian's mobile
  Given beneficiary B-000140 is a student whose record holds only a guardian's mobile
  Then the customer row and the invitation use the guardian's mobile
```

### US-S46 — Complete, lapse and re-allocate
**As the** programs team **we want** seat status kept up to date **so that** sponsors see real outcomes and unused seats are not wasted.
**Priority:** M · S1 · **Traces:** FR-S55, RUL-50

```gherkin
Scenario: Completion from the LMS
  When LmsProgressSync (hourly) reads the enrolment as completed
  Then enrolment.status is "completed" and the sponsored_unit becomes "completed"
  And a sponsored_unit_event "allocated → completed" is written

Scenario: Lapse after 60 days
  Given a unit was allocated 61 days ago and the enrolment has no started_at
  When LapseUnits runs at 02:00
  Then the unit goes "allocated → lapsed → awaiting_allocation" with a sponsored_unit_event for each step
  And the beneficiary's enrolment becomes "revoked" and she receives a short SMS (RUL-50)
  And the sponsor sees the seat as awaiting again

Scenario: Lapsed beneficiary allocated again later
  Given B-000123's earlier enrolment in digital-teacher is "revoked"
  When an officer allocates her a new seat in digital-teacher
  Then the allocation is accepted (ux_enrolment_live ignores revoked enrolments)

Scenario: Book copy delivered
  Given a science-stars-book copy unit is allocated to a library
  When the officer records the distribution with delivered_on and a proof photo
  Then the unit becomes "completed"
```

---

## E9 — Admin catalogue

### US-S47 — Manage products
**As a** store manager (P9) **I want** to create, preview and publish products **so that** I run the store without a developer.
**Priority:** M · S1 · **Traces:** FR-S72, RUL-01, RUL-02, RUL-03, BR-S10

```gherkin
Scenario: Publish a course
  Given a draft course with title_ar, summary_ar and price 220
  When I publish it
  Then status is "published", published_at is set and audit_log has "product.publish"

Scenario: Image without Arabic alt
  When I upload a cover without alt_ar
  Then it is rejected (ck_media_alt)

Scenario: Type is immutable after the first order
  Given digital-teacher has order lines
  When I try to change its type to "book"
  Then the change is rejected

Scenario: English falls back to Arabic
  Given title_en is empty
  Then /en/store/p/{slug} shows the Arabic title
```

### US-S48 — Manage sponsorship offers
**As a** store manager **I want** to open an offer on a course or book **so that** donors can sponsor it.
**Priority:** M · S1 · **Traces:** FR-S50, RUL-06, RUL-09, RUL-10

```gherkin
Scenario: Create an offer
  When I create an offer on qudrat-prep: seat_price 150, target_units 200, group "social_security_students", ends 2027-03-31
  Then a sponsorship_offer and an empty sponsorship_pool are created

Scenario: Only one active offer
  Given digital-teacher has an active offer
  When I activate a second offer on it
  Then the change is rejected (ux_offer_one_active)

Scenario: Extend an ended offer
  When I set ends_at of family-remote to 2026-12-31
  Then it accepts new lines again and the change is audited
```

### US-S49 — Manage causes
**As a** store manager **I want** to set up causes with goals **so that** donors see concrete targets.
**Priority:** M · S1 · **Traces:** FR-S58, RUL-07

```gherkin
Scenario: New Ramadan cause
  When I create a cause: unit_amount 1500, unit_label_ar "برّاد", target_amount 45000, ends 2027-03-20, zakat_eligible false
  Then it can be published

Scenario: Unit amount without label
  When unit_amount is 1500 and unit_label_ar is empty
  Then it is rejected (ck_cause_label)

Scenario: Second general fund
  When I tick is_general_fund on a second cause
  Then it is rejected (ux_one_general_fund)

Scenario: Opening figures
  When I set seed_raised 20000 and seed_donors 80
  Then v_goal includes them and audit_log records the change
```

### US-S50 — Edit settings
**As an** admin **I want** to change the store's numbers **so that** policy changes need no deployment.
**Priority:** M · S1 · **Traces:** FR-S76, RUL §M

```gherkin
Scenario: Change shipping fee
  When I set shipping_flat_fee to 30.00
  Then new checkouts charge 30.00 and existing orders keep 25.00
  And setting.updated_by is my email and audit_log has the diff

Scenario: Only admin
  Given I am store_manager
  Then PUT /v1/admin/settings returns 403
```

### US-S51 — Edit message templates
**As a** store manager **I want** to edit SMS and email texts with a test send **so that** messages read well.
**Priority:** S · S1 · **Traces:** FR-S77, RUL-63

```gherkin
Scenario: Test send
  When I edit message_template ("order_paid","sms","ar") and send a test to 0509998877
  Then the SMS is rendered with sample values and a notification row is recorded

Scenario: Unknown variable
  When the body contains "{{ totl }}"
  Then saving is rejected with the list of allowed variables
```

---

## E10 — Fulfilment

### US-S52 — Grant course access through the LMS
**As a** learner **I want** access granted automatically **so that** I start right after paying.
**Priority:** M · S1 · **Traces:** FR-S40, RUL-42

```gherkin
Scenario: LMS succeeds
  When order T3-26-004188 is paid
  Then the enrolment is created in the settlement transaction
  And the LMS sync from the outbox stores lms_enrolment_ref and order_line.fulfilment_status "enrolled"

Scenario: LMS fails
  Given the LMS returns 503
  Then the enrolment is "pending_sync" and the outbox retries with backoff up to 24 hours
  And the customer is not charged again
```

### US-S53 — Ship printed books
**As a** fulfilment officer **I want** a shipping queue with packing slips and tracking **so that** books arrive.
**Priority:** M · S1 · **Traces:** FR-S42, RUL-40, RUL-63

```gherkin
Scenario: Mark shipped
  Given shipment of T3-26-004188 is "preparing"
  When I print the packing slip and enter carrier "SMSA", tracking "SMSA123456"
  Then shipment.status is "shipped", shipped_at set
  And the customer receives "طلبك T3-26-004188 في الطريق. رقم الشحنة SMSA123456"

Scenario: Delivered completes the order
  Given every other line of the order is terminal
  When I mark the shipment "delivered"
  Then the print line is "delivered" and the order becomes "fulfilled"
```

### US-S54 — Manage stock
**As a** fulfilment officer **I want** on-hand, reserved and available stock **so that** we never oversell.
**Priority:** M · S1 · **Traces:** FR-S43, RUL-30

```gherkin
Scenario: Restock
  When I restock BK-LETTERS-TALE-P by 200 with reason "restock"
  Then stock_on_hand increases by 200 and a stock_movement row records my email

Scenario: Adjustment needs a reason
  When I adjust by −3 without a note
  Then it is rejected

Scenario: Last copy under concurrency
  Given BK-LETTERS-TALE-P has 1 available
  When two customers check out 1 copy at the same moment
  Then exactly one order reserves it and the other gets "out_of_stock"

Scenario: Low-stock alert with the default threshold
  Given book_format.low_stock_threshold is NULL for BK-LETTERS-TALE-P and low_stock_default_threshold is 20
  And available stock (stock_on_hand − stock_reserved) is 19
  When LowStockAlert runs at 08:00 Asia/Riyadh
  Then a staff alert lists BK-LETTERS-TALE-P

Scenario: Per-format threshold
  Given BK-SCIENCE-STAR-P has low_stock_threshold 50 and 60 available
  Then it is not listed
```

---

## E11 — Allocation

### US-S55 — Maintain the beneficiary register
**As an** allocation officer (P11) **I want** to import, verify and approve beneficiaries **so that** seats go to eligible people.
**Priority:** M · S1 · **Traces:** FR-S52, RUL-51, PDPL

```gherkin
Scenario: CSV import
  When I import 40 rows from a partner school
  Then 40 beneficiary rows are created with codes B-000124… , eligibility_status "pending"
  And full_name_enc and mobile_enc are encrypted, mobile_hash set
  And a duplicate mobile is rejected (ux_beneficiary_mobile) and reported per row

Scenario: Masked lists
  Then the list shows code, kind, group, region, status and a masked mobile only

Scenario: Reveal PII
  When I (allocation_officer) reveal B-000123
  Then the name is shown and audit_log records "beneficiary.reveal_pii"
  And a finance user sees no reveal action

Scenario: Approve
  When I approve B-000123 and set zakat_eligible true
  Then verified_by and verified_at are set and the change is audited
```

### US-S56 — Allocate seats in FIFO batches
**As an** allocation officer **I want** to allocate the oldest waiting seats to approved matching people **so that** no donor waits longer than needed.
**Priority:** M · S1 · **Traces:** FR-S53, RUL-20, RUL-47, RUL-48, RUL-62

```gherkin
Scenario: FIFO batch
  Given digital-teacher has 20 units "awaiting_allocation", the oldest funded 2026-08-14
  When I select 5 approved beneficiaries in group "rural_private_school_teachers" and allocate
  Then the 5 oldest units (by funded_at) become "allocated" in one allocation_batch
  And each gets an enrolment (source "sponsored"), a sponsored_unit_event and a BeneficiaryInvite

Scenario: Course seat needs a mobile
  Given B-000135 is approved in the right group but has no mobile of her own and no guardian's mobile
  Then allocating her a course seat is rejected with "mobile_required" (RUL-47e)

Scenario: Admin without the allocation role
  Given I have the roles {admin} only
  Then POST allocation batch returns 403 (RUL-62 segregation of duties)

Scenario: Wrong group
  When I pick a beneficiary in group "social_security_students"
  Then that allocation is rejected with "group_mismatch"

Scenario: Already enrolled
  Given B-000123 already has an active enrolment in digital-teacher
  Then allocating her another seat is rejected with "already_enrolled"

Scenario: Not approved
  Given B-000130 is "pending"
  Then she is not selectable

Scenario: Zakat-funded unit to a non-zakat beneficiary (guard for S2; no zakat reaches pools or units in S1, RUL-19)
  Given a unit with has_zakat true (offer with zakat_eligible true)
  And B-000131 is approved with zakat_eligible false
  When I allocate that unit to B-000131
  Then it is rejected with "zakat_beneficiary_required"
  And no zakat_ledger "out" row is written

Scenario: Zakat-funded unit to a zakat-eligible beneficiary
  When I allocate it to B-000123 (zakat_eligible true)
  Then a zakat_ledger row direction "out" is written for the zakat share of the unit

Scenario: Two officers at once
  When two officers allocate from the same backlog simultaneously
  Then no unit is allocated twice (FOR UPDATE SKIP LOCKED)
```

### US-S57 — Turn pooled money into seats
**As a** donor **I want** my partial contribution combined with others **so that** it becomes a real seat.
**Priority:** M · S1 · **Traces:** FR-S51, RUL-45

```gherkin
Scenario: Pool completes a seat
  Given the digital-teacher pool balance is 300.00 from earlier contributions
  When a paid order adds a 100.00 contribution
  Then one sponsored_unit (source "pooled", unit_price 350.00) is created
  And unit_funding rows record which contributions paid it, oldest first, summing 350.00
  And sponsorship_pool.balance is 50.00 and equals the sum of pool_contribution.remaining

Scenario: Large contribution creates several seats
  Given the pool balance is 0.00
  When a 1,000.00 contribution is paid
  Then 2 pooled units are created and the balance is 300.00
```

### US-S58 — Sweep the pool at campaign end
**As the** association **we want** remainders moved to the general fund **so that** no money sits idle after a campaign.
**Priority:** M · S1 · **Traces:** FR-S56, RUL-46, RUL-09

```gherkin
Scenario: Remainder swept
  Given the family-remote offer ends 2026-10-31 23:59:59+03 with pool balance 40.00 (seat 90.00)
  When CloseCampaigns runs at 00:05 Asia/Riyadh on 2026-11-01
  Then the remaining pool_contribution rows get swept_to_fund_at and remaining 0
  And sponsorship_pool.balance is 0.00
  And two goal_adjustment rows with the same pair_id are written:
    | product_id      | kind           | amount | sponsorship_offer_id |
    | family-remote   | pool_sweep_out | -40.00 | the ended offer      |
    | education-fund  | pool_sweep_in  | 40.00  | the ended offer      |
  And goal_progress.raised_amount of education-fund increases by 40.00
  And nightly integrity check 6 (which includes goal_adjustment) returns zero rows
  And each contributor's My giving shows the note from US-S19

Scenario: Zakat is never swept
  Given a pool contribution with is_zakat true (possible only from S2)
  Then it is not swept to education-fund; finance moves it to a zakat-eligible cause (RUL-46)

Scenario: No remainder
  Given the pool balance is 0.00 at the end date
  Then nothing is moved
```

### US-S59 — Alerts for waiting seats
**As a** programs manager **I want** alerts for seats waiting too long **so that** sponsors are not left waiting.
**Priority:** S · S1 · **Traces:** FR-S57, RUL-49

```gherkin
Scenario: 30 and 90 days
  Given 12 units of smart-study were funded 31 days ago and are still awaiting
  When AllocationAlerts runs at 07:00
  Then allocation officers get an alert with 12 overdue units (v_allocation_backlog.overdue_30)
  And units waiting 91 days are escalated to the store manager and admin
```

### US-S60 — Post delivery updates for a cause
**As a** store manager (P9) **I want** to post "cooler installed" with a photo **so that** donors see the result.
**Priority:** M · S1 · **Traces:** FR-S59, RUL-54, RUL-63

```gherkin
Scenario: Public update with notification
  When I post on water-for-schools: title "تم تركيب البرّاد في مدرسة الهجرة الابتدائية — القصيم", units_delivered 1, occurred_on 2026-09-02, public, notify
  Then the update appears on the product page
  And every donor to water-for-schools gets an SMS or email once, and notified_at is set

Scenario: Private update
  When I post with is_public false
  Then it is visible only in donors' My giving, not on the product page
```

---

## E12 — Finance & refunds

### US-S61 — Issue invoices and donation receipts
**As a** finance officer (P10) **I want** two separate documents per order **so that** sales and gifts are never mixed.
**Priority:** M · S1 · **Traces:** FR-S62, FR-S63, RUL-27, RUL-59, BR-S07, BR-S13

```gherkin
Scenario: Mixed order
  When order T3-26-004188 is paid
  Then invoice INV-2026-000001 covers the two buy lines and shipping: gross 505.00
  And donation receipt DR-2026-000001 covers the sponsor and donate lines: total 1850.00, zakat_amount 0
  And the receipt shows the association licence number and the donor's real name even if anonymous

Scenario: Zakat shown separately
  Given an order with 3,800.00 zakat on tablet-for-student and 100.00 sadaqa
  Then the donation receipt has total_amount 3900.00 and zakat_amount 3800.00

Scenario: VAT-registered
  Given vat_enabled is true, vat_rate 15, seller_vat_no set
  When a 350.00 course is paid
  Then the line vat_amount is 45.65, net 304.35, the invoice is_tax_invoice true with a QR

Scenario: Gapless numbering
  When 3 orders are paid concurrently
  Then their invoice numbers are consecutive without gaps (document_counter locked FOR UPDATE)
```

### US-S62 — Refund within policy
**As a** finance officer **I want** the system to check refund eligibility **so that** I never refund outside policy.
**Priority:** M · S1 · **Traces:** FR-S64, RUL-56, RUL-57, RUL-58, RUL-62

```gherkin
Scenario: Course within 7 days and under 20%
  Given digital-teacher was bought 3 days ago and progress_pct is 12
  When I refund the line
  Then the gateway refund is requested, refund status "succeeded" on confirmation
  And the enrolment is "revoked", the order "partially_refunded"
  And credit note CN-2026-000001 references the invoice
  And the customer receives "تم استرداد 350 ر.س من الطلب T3-26-004188."

Scenario: Print book before shipment
  Given order T3-26-004188 has 2 printed science-stars-book (130.00) and shipping 25.00, shipment "preparing"
  When I cancel every print line
  Then the refund is 155.00 including shipping and the reserved stock is released (RUL-57)

Scenario: Print book after delivery
  Given the shipment was delivered 5 days ago and the returned copies are unused
  Then 130.00 is refundable and the 25.00 shipping fee is not, unless marked faulty

Scenario: Admin without the finance role
  Given I have the roles {admin} only
  Then POST /v1/admin/orders/T3-26-004188/refunds returns 403 (RUL-62)

Scenario: Course over 20%
  Given progress_pct is 25
  Then the line shows "غير قابل للاسترداد: تجاوز التقدم 20٪" and cannot be selected

Scenario: Digital book after download
  Given the download_grant for letters-tales has download_count 1
  Then the line shows "غير قابل للاسترداد: تم التحميل" and cannot be selected

Scenario: Sponsorship units, only awaiting ones, within 48 hours
  Given 5 units were funded 20 hours ago and 1 is already allocated
  Then at most 4 units are refundable (1,400.00)
  When I refund 4
  Then 4 units become "cancelled", goal_progress funded_units −4, raised −1400.00
  And a cancellation receipt DRC-2026-000001 references DR-2026-000001

Scenario: Donation after 48 hours
  Given a 100.00 donation paid 49 hours ago
  Then it is not refundable

Scenario: Pool contribution partly converted
  Given a 100.00 contribution with remaining 30.00, paid 10 hours ago
  Then at most 30.00 is refundable

Scenario: Only finance
  Given I am store_manager
  Then POST /v1/admin/orders/T3-26-004188/refunds returns 403

Scenario: Idempotent refund
  When the same refund request is sent twice with the same Idempotency-Key
  Then one refund row exists and the gateway is called once
```

### US-S63 — Resolve payment review
**As a** finance officer **I want** to decide on held orders **so that** the donor is either served or refunded.
**Priority:** M · S1 · **Traces:** FR-S38, RUL-35

```gherkin
Scenario: Refund the mismatch
  Given T3-26-004190 is "payment_review" (captured 150.00, total 1500.00)
  When I choose "refund" with reason "amount mismatch"
  Then the captured 150.00 is refunded and the order becomes "refunded"

Scenario: Accept a late capture
  Given T3-26-004193 is "payment_review" because the capture arrived after expiry
  When I accept it
  Then the order becomes "paid" and the full settlement of RUL-39 runs

Scenario: Accept as paid
  When I choose "mark as paid" after the gateway confirms the full 1500.00
  Then the settlement effects of RUL-39 run and the order becomes "paid"
  And audit_log records my decision
```

### US-S64 — Zakat ledger
**As a** finance officer **I want** zakat tracked separately per product **so that** we can prove it was spent correctly.
**Priority:** M · S1 · **Traces:** FR-S61, RUL-20, RUL-58 · voucher UI detail also in US-S78

```gherkin
Scenario: Inflow
  When a 3,800.00 zakat donation to tablet-for-student is paid
  Then zakat_ledger has direction "in", amount 3800.00, the order line id
  And v_zakat_balance for tablet-for-student increases by 3800.00

Scenario: Refund reverses
  When that gift is refunded within 48 hours
  Then a zakat_ledger row direction "refund" 3800.00 is written

Scenario: Cause disbursement voucher
  Given tablet-for-student has a zakat balance of 3800.00
  When finance records voucher "ZV-2026-0007" for 1900.00 (2 tablets delivered)
  Then zakat_ledger has direction "out", amount 1900.00, reference "ZV-2026-0007" (RUL-20)
  And the balance is 1900.00

Scenario: Voucher above the balance
  When finance records a voucher of 5000.00 against a balance of 3800.00
  Then it is rejected and the balance stays 3800.00

Scenario: Never negative
  Then the nightly check "SELECT product_id FROM v_zakat_balance WHERE balance < 0" returns zero rows
```

### US-S65 — Anonymous donors still get a named receipt
**As a** donor (P3) **I want** to hide my name publicly but keep a proper receipt **so that** my gift is private yet documented.
**Priority:** M · S1 · **Traces:** FR-S30, FR-S63, RUL-23

```gherkin
Scenario: Anonymous
  Given I ticked "أخفِ اسمي في قائمة المساهمين (فاعل خير)"
  Then orders.is_anonymous is true, the feed shows "فاعل خير"
  And donation_receipt.donor_name is my real name
```

### US-S66 — ZATCA Phase 2 reporting *(S2)*
**As a** finance officer **I want** invoices reported to ZATCA **so that** we comply once VAT-registered.
**Priority:** S · S2 · **Traces:** FR-S65, BR-S13

```gherkin
Scenario: Simplified invoice reported
  Given vat_enabled is true and the e-invoicing adapter is configured
  When INV-2026-000120 is issued
  Then zatca_uuid, zatca_icv, zatca_pih, zatca_hash and zatca_qr are stored
  And zatca_status becomes "reported" within 24 hours of issue
```

---

## E13 — Reporting & administration

### US-S67 — Staff sign-in with 2FA
**As a** staff member **I want** email, password and TOTP **so that** a stolen password alone is useless.
**Priority:** M · S1 · **Traces:** FR-S70, RUL-62

```gherkin
Scenario: First login forces TOTP enrolment
  Given staff_user.totp_secret_enc is NULL
  When I sign in with the one-time password
  Then I must enrol TOTP before any admin page loads

Scenario: Staff session cookie
  When I sign in through /v1/admin/auth/*
  Then my refresh token is set in the HttpOnly cookie "t3s_srt" with Path=/v1/admin/auth, lifetime 8 hours
  And my access token lasts 10 minutes and has audience "store-staff"

Scenario: Lockout
  When I enter a wrong password 5 times in 15 minutes
  Then locked_until is set 1 hour ahead and audit_log records the lockout
```

### US-S68 — Role-based access
**As an** admin **I want** each role to see only its area **so that** mistakes and misuse are limited.
**Priority:** M · S1 · **Traces:** FR-S71, RUL-62 (segregation of duties)

```gherkin
Scenario Outline: Permissions are enforced by the API
  Given I have the role "<role>"
  When I call "<endpoint>"
  Then the status is <status>
  Examples:
    | role               | endpoint                          | status |
    | fulfilment         | PATCH shipment status             | 200    |
    | fulfilment         | POST refund                       | 403    |
    | allocation_officer | POST allocation batch             | 200    |
    | store_manager      | POST allocation batch             | 403    |
    | finance            | GET zakat report                  | 200    |
    | viewer             | PUT product                       | 403    |
    | viewer             | GET orders                        | 200    |
    | admin              | POST refund                       | 403    |
    | admin              | POST allocation batch             | 403    |
    | admin              | reveal beneficiary PII            | 403    |
    | admin, finance     | POST refund                       | 200    |
    | admin              | PUT settings                      | 200    |
```

Scenario: Segregation of duties
  Given the admin role does not include refunds, allocation or PII reveal (RUL-62)
  Then an admin must also hold "finance" or "allocation_officer" in staff_user.roles to perform them

### US-S69 — Manage orders
**As a** finance officer **I want** to find and inspect any order **so that** I answer customer queries.
**Priority:** M · S1 · **Traces:** FR-S73

```gherkin
Scenario: Search by mobile
  When I search "0501234567"
  Then I see T3-26-004188 with lines, payments, payment events, documents, notifications
  And actions "resend receipt" and, for pending orders only, "cancel"

Scenario: Cancel pending
  Given T3-26-004193 is "pending_payment"
  When I cancel it
  Then the order is "cancelled", cancelled_at set and reserved stock released

Scenario: Cannot cancel paid
  Given T3-26-004188 is "paid"
  Then cancel is not offered and the API returns 409
```

### US-S70 — Dashboard
**As a** store manager **I want** today's figures and alerts **so that** I see how the store is doing.
**Priority:** S · S1 · **Traces:** FR-S74

```gherkin
Scenario: Dashboard tiles
  When I open /admin/store
  Then I see sales and gifts for today, 7 and 30 days, top products, campaign progress
  And the allocation backlog from v_allocation_backlog and open alerts
```

### US-S71 — Reports
**As a** finance officer **I want** CSV reports **so that** I close the month and prepare the annual report.
**Priority:** M · S1 · **Traces:** FR-S75, BR-S11

```gherkin
Scenario Outline: Report export
  When I export "<report>" for September 2026
  Then the CSV totals equal the on-screen totals
  Examples:
    | report                          |
    | Sales & VAT by day and product  |
    | Gifts by product                |
    | Zakat ledger                    |
    | Allocation backlog & completion |
    | Refunds                         |
    | Shipments                       |
```

### US-S72 — Audit log
**As an** admin **I want** to see who changed what **so that** every administrative change is accountable.
**Priority:** M · S1 · **Traces:** FR-S78, BR-S11

```gherkin
Scenario: Filter
  When I filter the audit log by actor "rawan@ta3lom.sa" and entity "product"
  Then I see each change with time, action and diff

Scenario: Append-only
  Then no endpoint updates or deletes audit_log rows and store_app has only INSERT, SELECT on it
```

### US-S73 — Look up a customer
**As a** store manager **I want** to find a customer by mobile **so that** I can help them or block abuse.
**Priority:** S · S1 · **Traces:** FR-S79, RUL-61

```gherkin
Scenario: Lookup
  When I search +966501234567
  Then I see the customer's orders, giving and enrolments

Scenario: Block
  When I block the customer
  Then customer.status is "blocked" and the change is audited
  And the customer can no longer sign in or check out
  And document links already emailed to them keep working (RUL-61)
```

---

## E14 — Notifications & integration

### US-S74 — Reliable notifications
**As the** association **we want** messages sent from an outbox **so that** a failed SMS never undoes a payment.
**Priority:** M · S1 · **Traces:** FR-S80, FR-S81, RUL-63

```gherkin
Scenario: SMS provider down
  Given the SMS provider returns errors
  When order T3-26-004188 is paid
  Then the order stays "paid"
  And the outbox_message is retried with exponential backoff, attempts and last_error updated
  And each attempt's result is in notification

Scenario: A retried message never sends twice
  Given the dispatcher sent the order_paid SMS but crashed before marking outbox_message processed
  When it retries the message
  Then ux_notification_once (outbox_message_id, channel, recipient) prevents a second SMS

Scenario: Email receipt
  Then an email with the receipt link is sent to contact_email via SMTP
```

### US-S75 — Monthly sponsor digest
**As a** sponsor (P4) **I want** a monthly email on my units **so that** I follow progress without logging in.
**Priority:** M · S1 · **Traces:** FR-S25, FR-S80, RUL-63

```gherkin
Scenario: Digest
  Given 3 of my units were allocated and 1 completed during October 2026
  When the SponsorDigest job runs on 1 November 2026 at 09:00 Asia/Riyadh
  Then I receive one email listing those changes by group and region (RUL-63)
```

### US-S76 — Store in the main-site navigation
**As a** visitor of ta3lom.sa **I want** the store in the header and footer **so that** I find it from any page.
**Priority:** M · S1 · **Traces:** FR-S82, BR-S12

```gherkin
Scenario: Navigation
  When I open https://ta3lom.sa/ar
  Then the header has "المتجر" linking to /ar/store and the cart badge (US-S14)
  And the footer has store links
```

### US-S77 — Health endpoints
**As the** operator (Lotfi) **I want** liveness and readiness endpoints **so that** monitoring and deploys know when the API is healthy.
**Priority:** M · S1 · **Traces:** FR-S83

```gherkin
Scenario: Ready
  When I call GET /health/ready
  Then it returns 200 when PostgreSQL answers and the oldest unprocessed outbox_message is under 30 minutes old

Scenario: Database down
  Given PostgreSQL is stopped
  Then /health/live returns 200 and /health/ready returns 503
```

---

## Added in r1

### US-S78 — Record a zakat disbursement voucher *(E12)*
**As a** finance officer (P10) **I want** to record zakat spent on a cause as a voucher **so that** the zakat ledger shows where it went.
**Priority:** M · S1 · **Traces:** FR-S61, RUL-20

```gherkin
Scenario: Voucher for delivered units
  Given school-bag has zakat received 5400.00 and no outflow
  And 20 bags funded by zakat were delivered on 2026-10-20 to zakat-eligible students
  When I record voucher "ZV-2026-0011", amount 3600.00, date 2026-10-20, purpose "20 حقيبة مدرسية"
  Then zakat_ledger has direction "out", reference "ZV-2026-0011", actor = my email
  And v_zakat_balance for school-bag shows balance 1800.00
  And audit_log records the voucher

Scenario: Only finance
  Given I have the roles {admin, store_manager}
  Then recording a voucher returns 403
```

### US-S79 — Get my cart back after an unpaid order *(E6)*
**As a** customer **I want** my lines back in the cart when an order expires **so that** I do not rebuild it.
**Priority:** M · S1 · **Traces:** FR-S37, RUL-29

```gherkin
Scenario: Expired order restored
  Given order T3-26-004193 with 2 coolers and python-kids expired
  And my active cart already has python-kids
  Then the 2 coolers are restored to my active cart and python-kids is not duplicated

Scenario: Final payment failure
  Given order T3-26-004191 is "payment_failed" and its 60 minutes have passed
  Then its lines are restored to my active cart in the same way
```

### US-S80 — Refund a duplicate course paid in a race *(E12)*
**As a** finance officer **I want** to be alerted when a duplicate course was paid **so that** the customer is refunded quickly.
**Priority:** M · S1 · **Traces:** FR-S64, RUL-13

```gherkin
Scenario: Alert and refund
  Given settlement of T3-26-004188 marked its digital-teacher line for refund (US-S33)
  Then the finance dashboard lists T3-26-004188 with reason "duplicate_enrolment"
  When I refund the line (350.00)
  Then the refund is allowed regardless of the 7-day / 20% rule, because no access was granted
```

---

## Coverage: FR-S → US-S

| FR-S | Stories | | FR-S | Stories |
|---|---|---|---|---|
| FR-S01 | US-S01 | | FR-S40 | US-S52 |
| FR-S02 | US-S01, US-S02 | | FR-S41 | US-S41 |
| FR-S03 | US-S03 | | FR-S42 | US-S53 |
| FR-S04 | US-S04 | | FR-S43 | US-S54 |
| FR-S05 | US-S05 | | FR-S50 | US-S48 |
| FR-S06 | US-S06 | | FR-S51 | US-S17, US-S57 |
| FR-S07 | US-S07 | | FR-S52 | US-S55 |
| FR-S08 | US-S08 | | FR-S53 | US-S56 |
| FR-S09 | US-S09 | | FR-S54 | US-S45 |
| FR-S10 | US-S10 | | FR-S55 | US-S46 |
| FR-S11 | US-S11, US-S18 | | FR-S56 | US-S58 |
| FR-S12 | US-S16, US-S17, US-S18 | | FR-S57 | US-S59 |
| FR-S13 | US-S21, US-S22, US-S23, US-S24 | | FR-S58 | US-S49 |
| FR-S14 | US-S25 | | FR-S59 | US-S60 |
| FR-S15 | US-S20 | | FR-S60 | US-S26, US-S27 |
| FR-S16 | US-S10, US-S20 | | FR-S61 | US-S22, US-S64, US-S78 |
| FR-S17 | US-S12 | | FR-S62 | US-S61 |
| FR-S18 | US-S13 | | FR-S63 | US-S61, US-S65 |
| FR-S19 | US-S14 | | FR-S64 | US-S62, US-S80 |
| FR-S20 | US-S15 | | FR-S65 | US-S66 |
| FR-S21 | US-S28 | | FR-S70 | US-S67 |
| FR-S22 | US-S39 | | FR-S71 | US-S68 |
| FR-S23 | US-S40 | | FR-S72 | US-S47 |
| FR-S24 | US-S41 | | FR-S73 | US-S27, US-S69 |
| FR-S25 | US-S19, US-S75 | | FR-S74 | US-S70 |
| FR-S26 | US-S42 | | FR-S75 | US-S71 |
| FR-S27 | US-S43 | | FR-S76 | US-S50 |
| FR-S28 | US-S44 | | FR-S77 | US-S51 |
| FR-S30 | US-S29, US-S31, US-S65 | | FR-S78 | US-S72 |
| FR-S31 | US-S24, US-S30 | | FR-S79 | US-S28, US-S73 |
| FR-S32 | US-S31 | | FR-S80 | US-S74, US-S75 |
| FR-S33 | US-S32 | | FR-S81 | US-S74 |
| FR-S34 | US-S33 | | FR-S82 | US-S14, US-S23, US-S76 |
| FR-S35 | US-S35 | | FR-S83 | US-S77 |
| FR-S36 | US-S36 | | | |
| FR-S37 | US-S37, US-S79 | | | |
| FR-S38 | US-S34, US-S63 | | | |
| FR-S39 | US-S38 | | | |

All 72 functional requirements in [04](04-functional-requirements.md) are covered by at least one story.

---

## Open points

Found while writing the acceptance criteria. Items settled by the r1 canonical changes are kept for
traceability and marked resolved.

| # | Point | Status | Affects |
|---|---|---|---|
| OP-05-1 | Pool sweep vs. integrity check 6 (sweep had no `order_line`). | **Resolved — RUL-46** (`goal_adjustment` pair; check 6 includes adjustments) | US-S58 |
| OP-05-2 | Zakat in a swept pool would break RUL-20. | **Resolved — RUL-46** (zakat never swept; no zakat in pools in S1, RUL-19) | US-S58 |
| OP-05-3 | The RUL-47d zakat guard cannot be triggered in S1 (no `has_zakat` units until offers become zakat-eligible in S2). US-S56 tests it with an offer flagged `zakat_eligible = true`. | Open: confirm the guard and its test are built in S1 | US-S56 |
| OP-05-4 | No definition of a zakat disbursement voucher for causes. | **Resolved — RUL-20** (`zakat_ledger` `out`, `reference` = voucher no.; US-S64, US-S78). The voucher numbering format (`ZV-2026-0007` here) is not defined canonically: example only | US-S64, US-S78 |
| OP-05-5 | Reconciliation worst case about 25 min vs. FR-S34's 10 min. | **Resolved — RUL-38** (every 5 min, `initiated` > 5 min; about 10 min) | US-S37 |
| OP-05-6 | Meaning of a blocked customer. | **Resolved — RUL-61** (cannot sign in or check out; emailed links still work) | US-S28, US-S30, US-S73 |
| OP-05-7 | No low-stock threshold. | **Resolved — RUL §M** (`low_stock_default_threshold` 20, per-format `book_format.low_stock_threshold`; job `LowStockAlert`) | US-S54 |
| OP-05-8 | RUL-12 limits print quantity to 1–20, while `cart_line.quantity` allows 1–999. The limit exists only in the application. | Open | US-S11 |
| OP-05-9 | Customer-facing error codes (`campaign_ended`, `out_of_stock`, `already_enrolled`, `zakat_not_eligible`, `group_mismatch`, `zakat_beneficiary_required`, `mobile_required`, `duplicate_enrolment`) are examples; the canonical list belongs in 11-api-contract. | Open | 11 |
| OP-05-10 | A unit allocated within 48 hours of funding becomes non-refundable (RUL-57 vs. RUL-48). Confirm that this is intended. | Open | US-S62 |
| OP-05-11 | No job produced the monthly sponsor digest. | **Resolved — RUL-63** (job `SponsorDigest`, 1st of month 09:00) | US-S75 |
| OP-05-12 | Enrolment of a lapsed beneficiary. | **Resolved — RUL-50** (enrolment `revoked` + short SMS; may be allocated again) | US-S46 |
| OP-05-13 | **FR-S71 vs. RUL-62.** Resolved — FR-S71 reworded to follow RUL-62 segregation of duties. | Resolved | US-S56, US-S62, US-S68 |
| OP-05-14 | **Refund of a duplicate course line (RUL-13).** The rule says the line is "marked for refund" but not whether finance must still approve it, or whether the refund is automatic. US-S80 assumes finance refunds it outside the 7-day / 20% test. | Open | US-S33, US-S80 |
| OP-05-15 | **Beneficiaries with no mobile at all** (e.g. a student with no guardian's mobile on file) cannot receive a course seat under RUL-47(e). Confirm that the programs team collects a mobile at nomination. | Open (programs team, Q-04) | US-S56 |
