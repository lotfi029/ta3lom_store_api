-- Nightly integrity checks (Docs/store/database/03-database-operations.md §6). Every query must return 0 rows.

SET search_path = store;

-- 1. Order totals equal the sum of lines (+ shipping)
SELECT o.reference FROM orders o JOIN order_line l ON l.order_id = o.id
GROUP BY o.id HAVING o.purchases_total + o.gifts_total <> sum(l.amount);

-- 2. Paid orders have exactly one captured payment for the full amount
SELECT o.reference FROM orders o
WHERE o.status IN ('paid','fulfilled') AND
      (SELECT coalesce(sum(amount),0) FROM payment p WHERE p.order_id = o.id AND p.status IN ('captured','partially_refunded','refunded')) <> o.grand_total;

-- 3. Stock counters match the ledger
SELECT f.sku FROM book_format f
WHERE f.kind = 'print' AND f.stock_on_hand <> (SELECT coalesce(sum(delta),0) FROM stock_movement m WHERE m.book_format_id = f.id);

-- 4. Pool balance equals unconverted contributions
SELECT sp.sponsorship_offer_id FROM sponsorship_pool sp
WHERE sp.balance <> (SELECT coalesce(sum(remaining),0) FROM pool_contribution c
                     WHERE c.sponsorship_offer_id = sp.sponsorship_offer_id AND c.swept_to_fund_at IS NULL);

-- 5. Full-unit lines created exactly quantity units
SELECT l.id FROM order_line l JOIN orders o ON o.id = l.order_id
WHERE l.kind = 'sponsor' AND l.mode = 'units' AND o.paid_at IS NOT NULL
  AND l.quantity <> (SELECT count(*) FROM sponsored_unit u WHERE u.order_line_id = l.id);

-- 6. Goal counters match paid gift lines + adjustments (excluding seed)
SELECT g.product_id FROM goal_progress g
WHERE g.raised_amount <> (SELECT coalesce(sum(l.amount - l.refunded_amount),0) FROM order_line l JOIN orders o ON o.id = l.order_id
                          WHERE l.product_id = g.product_id AND l.kind IN ('sponsor','donate') AND o.paid_at IS NOT NULL)
                       + (SELECT coalesce(sum(a.amount),0) FROM goal_adjustment a WHERE a.product_id = g.product_id);

-- 7. Zakat never disbursed beyond receipts
SELECT product_id FROM v_zakat_balance WHERE balance < 0;

-- 8. Every paid order with gift lines has a receipt; with purchase lines an invoice
SELECT o.reference FROM orders o WHERE o.paid_at IS NOT NULL
  AND o.gifts_total > 0 AND NOT EXISTS (SELECT 1 FROM donation_receipt r WHERE r.order_id = o.id AND r.kind = 'receipt');
SELECT o.reference FROM orders o WHERE o.paid_at IS NOT NULL
  AND o.purchases_total > 0 AND NOT EXISTS (SELECT 1 FROM invoice i WHERE i.order_id = o.id AND i.kind = 'invoice');

-- 9. Document numbers are gapless
SELECT series, yr FROM (
  SELECT 'DR' AS series, extract(year FROM issued_at)::int AS yr, count(*) AS n, max(split_part(number,'-',3)::int) AS mx
  FROM donation_receipt WHERE kind = 'receipt' GROUP BY 2) t WHERE n <> mx;   -- repeat for INV, CN, DRC
