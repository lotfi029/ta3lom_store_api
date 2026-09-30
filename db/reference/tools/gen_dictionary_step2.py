"""Step 2: render the data dictionary.
Usage: python3 gen_dictionary_step2.py <dict.json> <out 02-data-dictionary.md> <generated-on date>"""
import json,sys
json_path, out_path, generated_on = sys.argv[1], sys.argv[2], sys.argv[3]
T=json.load(open(json_path,encoding='utf-8'))['tables']
MOD=[
("1. Settings, audit, numbering",[
 ('setting','Admin-editable configuration (Docs/store/03-business-rules.md §M). One JSON value per key.'),
 ('audit_log','Append-only record of every staff mutation and sensitive system action (BR-S11). Never updated or deleted.'),
 ('document_counter','Gapless counters per document series and year (RUL-59). Row is locked `FOR UPDATE` inside the payment transaction.')]),
("2. Media",[('media_asset','Every stored file: product images, PDFs, private book files. Alt text is mandatory for images (main-site FR-35).')]),
("3. Catalogue",[
 ('category','Course and book categories (filter chips).'),
 ('product','Supertype for every sellable or givable item (RUL-01). Type-specific data lives in `course_detail`, `book_detail`/`book_format`, `cause_detail`.'),
 ('course_detail','Course price and metadata (1:1 with product).'),
 ('course_module','Curriculum rows shown on the product page.'),
 ('book_detail','Book metadata (1:1 with product).'),
 ('book_format','Print or digital edition with its own price; print carries stock (RUL-05, RUL-30).'),
 ('stock_movement','Ledger of every stock change. `book_format.stock_on_hand` must equal the sum of movements (nightly check).'),
 ('sponsorship_offer','Makes a course or book sponsorable (RUL-06). At most one active offer per product.'),
 ('cause_detail','Donation product settings (RUL-07). Exactly one row may be the general fund.'),
 ('goal_progress','Denormalised counters for progress bars (RUL-08). Updated in the payment transaction; `seed_*` holds pre-launch offline figures.'),
 ('cause_update','Delivery updates posted by staff (RUL-54).'),
 ('goal_adjustment','Goal movements that are not order lines: pool sweep out/in pairs (RUL-46) and audited finance corrections. Included in integrity check 6.')]),
("4. Identity",[
 ('customer','Store customer, keyed by mobile (RUL-60/61). Separate from main-site editors.'),
 ('customer_address','Saved shipping addresses (Saudi National Address format).'),
 ('otp_challenge','One row per OTP sent. Code stored as HMAC; rate limits are computed from this table (RUL-60).'),
 ('staff_user','Admin users with roles and mandatory TOTP (RUL-62).'),
 ('refresh_token','Rotating refresh tokens for customers and staff, with family-based reuse detection (ADR-006 pattern).')]),
("5. Cart",[
 ('cart','Server-side cart; guests via HttpOnly cookie id (RUL-11).'),
 ('cart_line','Indicative lines — repriced at checkout (RUL-15). Dedication kept as JSON until the order is created.')]),
("6. Ordering",[
 ('orders','One checkout. Named in the plural because ORDER is a reserved word. Totals are snapshots (RUL-31). The state machine is in RUL-29.'),
 ('order_line','Immutable snapshot of what was bought or given, with VAT split and fulfilment status (RUL-40).'),
 ('dedication','إهداء attached to a gift line; `card_token` drives the public card page (RUL-21/22).')]),
("7. Payments",[
 ('payment','One attempt at the gateway. Several per order are possible (retries, RUL-37).'),
 ('payment_event','Webhook inbox: every callback stored before processing; unique per provider event (RUL-34).'),
 ('refund','Refund issued through the gateway by finance (RUL-56).'),
 ('refund_line','Which lines, amounts and units a refund covers.'),
 ('idempotency_key','Stored responses for `Idempotency-Key` requests (checkout, refunds); pruned after 24 h.')]),
("8. Documents",[
 ('invoice','Sales invoice or credit note for purchases. ZATCA columns are filled in S2 (BR-S13).'),
 ('donation_receipt','Donation receipt or cancellation for gift lines (RUL-59). Zakat is shown separately.')]),
("9. Fulfilment",[
 ('enrolment','Access to a course, from a purchase or a sponsored allocation. One live enrolment per customer per course (RUL-13, RUL-47c).'),
 ('download_grant','Right to download a digital book (RUL-41).'),
 ('download_log','Each download, for limits and abuse review.'),
 ('shipment','One shipment per order containing print lines.')]),
("10. Giving",[
 ('beneficiary','Register of people and organisations who receive sponsored units. Personal data encrypted at the application layer (PDPL).'),
 ('allocation_batch','Groups allocations made together, so they can be notified and audited as one action.'),
 ('sponsored_unit','One seat or copy funded by donors (RUL-43/45). Carries the allocation lifecycle (RUL-50).'),
 ('sponsored_unit_event','Status history shown to sponsors as a timeline.'),
 ('sponsorship_pool','Running balance of partial contributions per offer (RUL-45).'),
 ('pool_contribution','A partial contribution; `remaining` shrinks as units are funded, FIFO.'),
 ('unit_funding','Which contributions funded which pooled unit, and how much each gave.'),
 ('distribution','Hand-over record for sponsored book copies.'),
 ('zakat_ledger','Every zakat movement in, out or refunded (RUL-20).')]),
("11. Notifications",[
 ('outbox_message','Transactional outbox written in the same transaction as the business change. Worker delivers with retry and backoff (RUL-63).'),
 ('message_template','SMS/email templates per key, channel and locale.'),
 ('notification','Delivery record of each SMS or email sent.')]),
]
seen=set()
o=["# Store Data Dictionary","",f"**Generated from** the EF Core migrations (identical to [`db/reference/001_store_schema.sql`](../../db/reference/001_store_schema.sql), checked by `SchemaParityTests`) on PostgreSQL · {generated_on}","",
"Every column is listed. **Key**: PK primary key, FK→ foreign key target, UQ unique. Types are PostgreSQL types. The EF Core CLR type mapping is in [01-database-design.md §6](01-database-design.md#6-ef-core-mapping).","",
"Regenerate after each migration with `tools/scripts/regenerate-data-dictionary.sh` so that this file never drifts from the schema.",""]
o.append("## Contents\n")
for m,ts in MOD:
    o.append(f"- **{m}** — "+", ".join(f"[`{t}`](#{t})" for t,_ in ts))
o.append("")
for m,ts in MOD:
    o.append(f"---\n\n## {m}\n")
    for t,desc in ts:
        seen.add(t)
        o.append(f"### {t}\n\n{desc}\n")
        o.append("| Column | Type | Null | Default | Key | Notes |\n|---|---|---|---|---|---|")
        for c,ty,n,d,pk,fk,uq,note in T[t]:
            d=d.replace('|','\\|')
            if len(d)>40: d=d[:37]+'…'
            key=" ".join(x for x in (pk,fk,uq) if x)
            o.append(f"| `{c}` | {ty} | {'✓' if n=='YES' else ''} | {('`'+d+'`') if d else ''} | {key} | {note.replace('|','/')} |")
        o.append("")
missing=set(T)-seen
assert not missing, missing
open(out_path,'w',encoding='utf-8').write("\n".join(o)+"\n")
print('ok')
