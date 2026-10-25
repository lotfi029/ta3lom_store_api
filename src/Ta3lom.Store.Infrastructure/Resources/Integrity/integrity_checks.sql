-- Nightly integrity checks (IntegrityChecks job, 04:00 Riyadh). Every query must return 0 rows; each failing check raises a
-- staff alert. A heading is "-- <number>. <title>"; each statement ends with ";".

SET search_path = store;

-- 1. Order totals and line counts equal their lines
SELECT o.reference FROM orders o LEFT JOIN order_line l ON l.order_id = o.id
GROUP BY o.id, o.reference, o.total_amount, o.line_count
HAVING o.total_amount <> coalesce(sum(l.amount), 0) OR o.line_count <> count(l.id);

-- 2. Cart lines have exactly one valid target
SELECT id FROM cart_line
WHERE NOT ((target_kind = 'project' AND project_id IS NOT NULL AND category_id IS NULL)
        OR (target_kind = 'category' AND category_id IS NOT NULL AND project_id IS NULL AND option_id IS NULL));

-- 3. Order lines have exactly one valid target
SELECT id FROM order_line
WHERE NOT ((target_kind = 'project' AND project_id IS NOT NULL AND category_id IS NULL)
        OR (target_kind = 'category' AND category_id IS NOT NULL AND project_id IS NULL AND option_id IS NULL));

-- 4. Published projects accept an amount (an active option or an open amount)
SELECT p.slug FROM project p
WHERE p.status = 'published' AND NOT p.allow_open_amount
  AND NOT EXISTS (SELECT 1 FROM project_option o WHERE o.project_id = p.id AND o.is_active);
