-- Smoke scenario: one mixed order (course purchase + 5 sponsored seats + 100 SAR pooled share + dedication),
-- settlement effects, one allocation, and two negative tests. Run on a seeded DB, then 003_integrity_checks.sql.
-- On PostgreSQL < 18 first run: CREATE FUNCTION uuidv7() RETURNS uuid LANGUAGE sql AS $$ SELECT gen_random_uuid() $$;
SET search_path=store;
BEGIN;
INSERT INTO customer(mobile,full_name,email) VALUES ('+966551234567','محمد','m@x.co');
INSERT INTO orders(reference,customer_id,purchases_total,shipping_total,gifts_total,grand_total,contact_name,contact_mobile,contact_email,payment_due_at,terms_accepted_at)
 SELECT 'T3-26-001000',id,350,0,1850,2200,'محمد','+966551234567','m@x.co',now()+interval '1 hour',now() FROM customer;
INSERT INTO order_line(order_id,line_no,product_id,product_type,kind,mode,quantity,unit_price,amount,vat_rate,vat_amount,title_ar)
 SELECT o.id,1,p.id,'course','buy','units',1,350,350,15,45.65,p.title_ar FROM orders o, product p WHERE p.slug='digital-teacher';
INSERT INTO order_line(order_id,line_no,product_id,product_type,sponsorship_offer_id,kind,mode,quantity,unit_price,amount,intent,title_ar)
 SELECT o.id,2,p.id,'course',so.id,'sponsor','units',5,350,1750,'sadaqa',p.title_ar FROM orders o, product p JOIN sponsorship_offer so ON so.product_id=p.id WHERE p.slug='digital-teacher';
INSERT INTO order_line(order_id,line_no,product_id,product_type,sponsorship_offer_id,kind,mode,quantity,unit_price,amount,intent,title_ar)
 SELECT o.id,3,p.id,'course',so.id,'sponsor','amount',1,350,100,'sadaqa',p.title_ar FROM orders o, product p JOIN sponsorship_offer so ON so.product_id=p.id WHERE p.slug='digital-teacher';
INSERT INTO dedication(order_line_id,to_name,to_mobile,template_key,card_token) SELECT id,'والدتي','+966551112222','hasanat','abcDEF123' FROM order_line WHERE line_no=2;
INSERT INTO payment(order_id,provider,provider_payment_id,method,amount,status) SELECT id,'moyasar','pay_1','mada',2200,'captured' FROM orders;
UPDATE orders SET status='paid',paid_at=now();
INSERT INTO enrolment(customer_id,product_id,source,order_line_id) SELECT o.customer_id,l.product_id,'purchase',l.id FROM order_line l JOIN orders o ON o.id=l.order_id WHERE l.line_no=1;
INSERT INTO sponsored_unit(product_id,sponsorship_offer_id,source,order_line_id,unit_price) SELECT l.product_id,l.sponsorship_offer_id,'full',l.id,350 FROM order_line l, generate_series(1,5) WHERE l.line_no=2;
INSERT INTO pool_contribution(sponsorship_offer_id,order_line_id,amount,remaining) SELECT sponsorship_offer_id,id,100,100 FROM order_line WHERE line_no=3;
UPDATE sponsorship_pool SET balance=100 WHERE sponsorship_offer_id=(SELECT sponsorship_offer_id FROM order_line WHERE line_no=3);
UPDATE goal_progress SET raised_amount=1850,funded_units=5,donor_count=1 WHERE product_id=(SELECT id FROM product WHERE slug='digital-teacher');
INSERT INTO donation_receipt(order_id,number,donor_name,total_amount) SELECT id,'DR-2026-000001','محمد',1850 FROM orders;
INSERT INTO invoice(order_id,kind,number,is_tax_invoice,net_total,vat_total,gross_total) SELECT id,'invoice','INV-2026-000001',true,304.35,45.65,350 FROM orders;
-- beneficiary + allocation
INSERT INTO beneficiary(code,kind,beneficiary_group,region,full_name_enc,eligibility_status,created_by) VALUES ('B-000001','teacher','rural_private_school_teachers','القصيم','\x00','approved','t');
INSERT INTO customer(mobile) VALUES ('+966559999999');
UPDATE sponsored_unit SET status='allocated',beneficiary_id=(SELECT id FROM beneficiary),allocated_at=now() WHERE id=(SELECT id FROM sponsored_unit ORDER BY funded_at LIMIT 1);
INSERT INTO enrolment(customer_id,product_id,source,sponsored_unit_id) SELECT (SELECT id FROM customer WHERE mobile='+966559999999'),product_id,'sponsored',id FROM sponsored_unit WHERE status='allocated';
-- duplicate purchase enrolment must fail
SAVEPOINT s; 
DO $$ BEGIN
 BEGIN INSERT INTO store.enrolment(customer_id,product_id,source) SELECT customer_id,product_id,'manual' FROM store.enrolment WHERE source='purchase'; RAISE EXCEPTION 'dup allowed!'; EXCEPTION WHEN unique_violation THEN RAISE NOTICE 'dup blocked OK'; END;
 BEGIN INSERT INTO store.order_line(order_id,line_no,product_id,product_type,kind,mode,quantity,unit_price,amount,vat_amount,title_ar) SELECT order_id,9,product_id,'cause','donate','amount',1,10,10,1.3,'x' FROM store.order_line LIMIT 1; RAISE EXCEPTION 'vat on gift allowed!'; EXCEPTION WHEN check_violation THEN RAISE NOTICE 'gift vat blocked OK'; END;
END $$;
ROLLBACK;  -- leave the database clean
SELECT * FROM v_allocation_backlog;
