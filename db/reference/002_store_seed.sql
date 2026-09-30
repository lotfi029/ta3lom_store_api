-- ============================================================================
--  ta3lom STORE — seed data (S1)
--  Reference catalogue = the approved prototype (design/ta3lom-store-prototype.html).
--  Opening goal figures are stored as seed_* (offline giving before launch),
--  never as fake orders. Run after 001_store_schema.sql.
--  In the app, the same data lives in Ta3lom.Store.Infrastructure/Persistence/Seed.
-- ============================================================================
SET search_path = store, public;
BEGIN;

-- Settings (Docs/store/03-business-rules.md §M)
INSERT INTO setting (key, value) VALUES
 ('vat_enabled','false'),('vat_rate','15'),('seller_vat_no','null'),
 ('shipping_flat_fee','25.00'),('min_gift_amount','10.00'),('cart_max_total','100000.00'),
 ('order_payment_timeout_min','60'),('download_limit','5'),
 ('allocation_alert_days','30'),('allocation_escalate_days','90'),('lapse_days','60'),
 ('refund_window_days','7'),('refund_progress_max_pct','20'),('gift_refund_window_hours','48'),
 ('dedication_free_text_enabled','false');

-- Categories
INSERT INTO category (slug,name_ar,name_en) VALUES ('tech','التقنية في التعليم','EdTech');
INSERT INTO category (slug,name_ar,name_en) VALUES ('teach','مهارات المعلم','Teacher skills');
INSERT INTO category (slug,name_ar,name_en) VALUES ('study','مهارات الدراسة','Study skills');
INSERT INTO category (slug,name_ar,name_en) VALUES ('kids','الطفولة المبكرة','Early childhood');
INSERT INTO category (slug,name_ar,name_en) VALUES ('family','الأسرة','Family');

-- COURSE: digital-teacher
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('course','digital-teacher','published',(SELECT id FROM category WHERE slug='teach'),'المعلّم الرقمي: أدوات التعليم الإلكتروني','The Digital Teacher: e-learning tools','دورة عملية تمكّن المعلم من تصميم درس تفاعلي كامل باستخدام أدوات مجانية، مع تقييم تكويني ومتابعة للطلاب.','A hands-on course that takes a teacher from zero to a complete interactive lesson using free tools.',NULL,NULL,'best',0,now());
INSERT INTO course_detail (product_id,price,compare_at_price,lessons,duration_minutes,level_ar,level_en,instructor_ar,instructor_en,learner_count) VALUES ((SELECT id FROM product WHERE slug='digital-teacher'),350,450,24,1080,'مبتدئ – متوسط','Beginner – intermediate','أ. ريم السبيعي','Reem Al-Subaie',1240);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='digital-teacher'),1,'أساسيات الفصل الافتراضي','Virtual classroom basics',2520);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='digital-teacher'),2,'تصميم درس تفاعلي في 10 دقائق','An interactive lesson in 10 minutes',3480);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='digital-teacher'),3,'التقييم التكويني الرقمي','Digital formative assessment',4200);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='digital-teacher'),4,'إدارة الصف عن بُعد','Remote classroom management',2940);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='digital-teacher'),5,'مشروع التخرج وشهادة الإتمام','Capstone and certificate',2100);
INSERT INTO sponsorship_offer (product_id,seat_price,unit_label_ar,unit_label_en,target_units,beneficiary_group,group_label_ar,group_label_en,ends_at) VALUES ((SELECT id FROM product WHERE slug='digital-teacher'),350,'مقعد','seat',300,'rural_private_school_teachers','معلمات المدارس الأهلية الناشئة في القرى والهجر','Teachers at small rural private schools','2026-12-31 23:59:59+03');
INSERT INTO sponsorship_pool (sponsorship_offer_id) SELECT id FROM sponsorship_offer WHERE product_id=(SELECT id FROM product WHERE slug='digital-teacher') AND is_active;
INSERT INTO goal_progress (product_id,seed_units,seed_donors) VALUES ((SELECT id FROM product WHERE slug='digital-teacher'),184,96);

-- COURSE: smart-study
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('course','smart-study','published',(SELECT id FROM category WHERE slug='study'),'مهارات المذاكرة الذكية','Smart study skills','تقنيات مثبتة علمياً لتنظيم الوقت والتركيز والحفظ طويل المدى، مصمّمة لطلاب المرحلتين المتوسطة والثانوية.','Evidence-based techniques for time, focus and long-term memory, for middle and high school students.',NULL,NULL,NULL,1,now());
INSERT INTO course_detail (product_id,price,compare_at_price,lessons,duration_minutes,level_ar,level_en,instructor_ar,instructor_en,learner_count) VALUES ((SELECT id FROM product WHERE slug='smart-study'),120,NULL,12,360,'جميع المستويات','All levels','د. خالد الزهراني','Dr. Khalid Al-Zahrani',3100);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='smart-study'),1,'كيف يعمل الدماغ عند التعلّم','How the brain learns',1500);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='smart-study'),2,'جدول مذاكرة واقعي','A realistic study plan',1860);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='smart-study'),3,'تقنية الاسترجاع النشط','Active recall',1680);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='smart-study'),4,'قبل الاختبار بأسبوع','One week before the exam',1320);
INSERT INTO sponsorship_offer (product_id,seat_price,unit_label_ar,unit_label_en,target_units,beneficiary_group,group_label_ar,group_label_en,ends_at) VALUES ((SELECT id FROM product WHERE slug='smart-study'),120,'مقعد','seat',500,'social_security_students','طلاب الأسر المستفيدة من الضمان الاجتماعي','Students from social-security families','2026-11-30 23:59:59+03');
INSERT INTO sponsorship_pool (sponsorship_offer_id) SELECT id FROM sponsorship_offer WHERE product_id=(SELECT id FROM product WHERE slug='smart-study') AND is_active;
INSERT INTO goal_progress (product_id,seed_units,seed_donors) VALUES ((SELECT id FROM product WHERE slug='smart-study'),342,210);

-- COURSE: python-kids
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('course','python-kids','published',(SELECT id FROM category WHERE slug='tech'),'البرمجة للناشئين بلغة بايثون','Python for young coders','رحلة ممتعة من أول سطر برمجي إلى لعبة كاملة، للأعمار 10–15 سنة، بمشاريع أسبوعية قصيرة.','From the first line of code to a finished game, ages 10–15, with short weekly projects.',NULL,NULL,'new',2,now());
INSERT INTO course_detail (product_id,price,compare_at_price,lessons,duration_minutes,level_ar,level_en,instructor_ar,instructor_en,learner_count) VALUES ((SELECT id FROM product WHERE slug='python-kids'),450,NULL,30,1440,'مبتدئ','Beginner','م. فيصل القحطاني','Faisal Al-Qahtani',640);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='python-kids'),1,'مرحباً بايثون','Hello Python',1800);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='python-kids'),2,'المتغيرات والقرارات','Variables and decisions',2700);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='python-kids'),3,'الحلقات والرسم','Loops and drawing',3000);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='python-kids'),4,'لعبتي الأولى','My first game',4800);
INSERT INTO sponsorship_offer (product_id,seat_price,unit_label_ar,unit_label_en,target_units,beneficiary_group,group_label_ar,group_label_en,ends_at) VALUES ((SELECT id FROM product WHERE slug='python-kids'),450,'مقعد','seat',150,'orphan_home_students','طلاب دور الأيتام الشريكة','Students at partner orphan-care homes','2027-01-31 23:59:59+03');
INSERT INTO sponsorship_pool (sponsorship_offer_id) SELECT id FROM sponsorship_offer WHERE product_id=(SELECT id FROM product WHERE slug='python-kids') AND is_active;
INSERT INTO goal_progress (product_id,seed_units,seed_donors) VALUES ((SELECT id FROM product WHERE slug='python-kids'),41,23);

-- COURSE: early-childhood
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('course','early-childhood','published',(SELECT id FROM category WHERE slug='kids'),'أساسيات التعليم في الطفولة المبكرة','Early-childhood teaching essentials','للمعلمات والأمهات: كيف نبني المهارات الأساسية الاثنتي عشرة من خلال اللعب والأنشطة اليومية.','For teachers and mothers: building twelve core skills through play and daily routines.',NULL,NULL,NULL,3,now());
INSERT INTO course_detail (product_id,price,compare_at_price,lessons,duration_minutes,level_ar,level_en,instructor_ar,instructor_en,learner_count) VALUES ((SELECT id FROM product WHERE slug='early-childhood'),280,NULL,16,600,'جميع المستويات','All levels','أ. هيا الدوسري','Haya Al-Dosari',870);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='early-childhood'),1,'المهارات الاثنتا عشرة','The twelve core skills',2100);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='early-childhood'),2,'التعلّم باللعب','Learning through play',2400);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='early-childhood'),3,'ركن القراءة في المنزل','A reading corner at home',1560);
INSERT INTO sponsorship_offer (product_id,seat_price,unit_label_ar,unit_label_en,target_units,beneficiary_group,group_label_ar,group_label_en,ends_at) VALUES ((SELECT id FROM product WHERE slug='early-childhood'),280,'مقعد','seat',200,'family_program_mothers','أمهات مستفيدات من برامج الأسرة','Mothers enrolled in family programs','2026-12-31 23:59:59+03');
INSERT INTO sponsorship_pool (sponsorship_offer_id) SELECT id FROM sponsorship_offer WHERE product_id=(SELECT id FROM product WHERE slug='early-childhood') AND is_active;
INSERT INTO goal_progress (product_id,seed_units,seed_donors) VALUES ((SELECT id FROM product WHERE slug='early-childhood'),77,41);

-- COURSE: qudrat-prep
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('course','qudrat-prep','published',(SELECT id FROM category WHERE slug='study'),'الاستعداد لاختبار القدرات العامة','General aptitude test prep','شرح مبسّط للقسمين الكمي واللفظي مع ثلاثة اختبارات تجريبية كاملة وتحليل للأداء.','Quantitative and verbal sections with three full mock tests and performance analysis.',NULL,NULL,NULL,4,now());
INSERT INTO course_detail (product_id,price,compare_at_price,lessons,duration_minutes,level_ar,level_en,instructor_ar,instructor_en,learner_count) VALUES ((SELECT id FROM product WHERE slug='qudrat-prep'),199,249,28,840,'متوسط','Intermediate','أ. عبدالرحمن الشهري','Abdulrahman Al-Shehri',2150);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='qudrat-prep'),1,'القسم الكمي','Quantitative',11400);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='qudrat-prep'),2,'القسم اللفظي','Verbal',9600);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='qudrat-prep'),3,'الاختبارات التجريبية','Mock tests',14400);

-- COURSE: family-remote
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('course','family-remote','published',(SELECT id FROM category WHERE slug='family'),'دليل الأسرة للتعلّم عن بُعد','The family guide to remote learning','كيف تهيّئ الأسرة بيئة تعلّم منزلية صحية، وتتابع أبناءها دون ضغط.','Setting up a healthy home-learning environment and following up without pressure.',NULL,NULL,NULL,5,now());
INSERT INTO course_detail (product_id,price,compare_at_price,lessons,duration_minutes,level_ar,level_en,instructor_ar,instructor_en,learner_count) VALUES ((SELECT id FROM product WHERE slug='family-remote'),90,NULL,8,240,'جميع المستويات','All levels','د. منى العنزي','Dr. Mona Al-Anazi',1520);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='family-remote'),1,'بيئة التعلّم المنزلية','The home learning space',1200);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='family-remote'),2,'الروتين اليومي','Daily routine',1440);
INSERT INTO course_module (product_id,sort,title_ar,title_en,duration_seconds) VALUES ((SELECT id FROM product WHERE slug='family-remote'),3,'المتابعة دون ضغط','Following up without pressure',1800);
INSERT INTO sponsorship_offer (product_id,seat_price,unit_label_ar,unit_label_en,target_units,beneficiary_group,group_label_ar,group_label_en,ends_at) VALUES ((SELECT id FROM product WHERE slug='family-remote'),90,'مقعد','seat',400,'enrolled_student_families','أسر الطلاب المستفيدين','Families of enrolled students','2026-10-31 23:59:59+03');
INSERT INTO sponsorship_pool (sponsorship_offer_id) SELECT id FROM sponsorship_offer WHERE product_id=(SELECT id FROM product WHERE slug='family-remote') AND is_active;
INSERT INTO goal_progress (product_id,seed_units,seed_donors) VALUES ((SELECT id FROM product WHERE slug='family-remote'),388,150);

-- BOOK: science-stars-book
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('book','science-stars-book','published',(SELECT id FROM category WHERE slug='kids'),'نجوم العلوم — كتاب التجارب المنزلية','Science Stars — home experiments book','48 تجربة علمية آمنة بأدوات المنزل، ملوّنة ومصوّرة خطوة بخطوة للمرحلة الابتدائية.','48 safe experiments with household items, illustrated step by step for primary pupils.',NULL,NULL,'best',6,now());
INSERT INTO book_detail (product_id,author_ar,author_en,pages,published_year) VALUES ((SELECT id FROM product WHERE slug='science-stars-book'),'فريق جمعية التعلم الإلكتروني','E-Learning Association team',132,2026);
INSERT INTO book_format (product_id,kind,sku,price,compare_at_price,stock_on_hand,weight_grams) VALUES ((SELECT id FROM product WHERE slug='science-stars-book'),'print','BK-SCIENCE-STAR-P',65,80,500,450);
INSERT INTO stock_movement (book_format_id,delta,reason,ref_type,note,actor) VALUES ((SELECT id FROM book_format WHERE sku='BK-SCIENCE-STAR-P'),500,'restock','manual','Opening stock','seed');
INSERT INTO media_asset (kind,storage_key,is_public,mime,size_bytes) VALUES ('book_file','private/books/science-stars-book.pdf',false,'application/pdf',1);
INSERT INTO book_format (product_id,kind,sku,price,compare_at_price,file_asset_id) VALUES ((SELECT id FROM product WHERE slug='science-stars-book'),'digital','BK-SCIENCE-STAR-D',35,NULL,(SELECT id FROM media_asset WHERE storage_key='private/books/science-stars-book.pdf'));
INSERT INTO sponsorship_offer (product_id,seat_price,unit_label_ar,unit_label_en,target_units,beneficiary_group,group_label_ar,group_label_en,ends_at) VALUES ((SELECT id FROM product WHERE slug='science-stars-book'),40,'نسخة','copy',1000,'public_primary_libraries','مكتبات المدارس الابتدائية الحكومية','Public primary-school libraries','2026-12-31 23:59:59+03');
INSERT INTO sponsorship_pool (sponsorship_offer_id) SELECT id FROM sponsorship_offer WHERE product_id=(SELECT id FROM product WHERE slug='science-stars-book') AND is_active;
INSERT INTO goal_progress (product_id,seed_units,seed_donors) VALUES ((SELECT id FROM product WHERE slug='science-stars-book'),612,188);

-- BOOK: formative-guide
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('book','formative-guide','published',(SELECT id FROM category WHERE slug='teach'),'دليل المعلم للتقويم التكويني','The teacher''s guide to formative assessment','أكثر من 60 أداة تقويم سريعة تُطبّق داخل الحصة، مع نماذج جاهزة للطباعة.','60+ quick in-class assessment tools with printable templates.',NULL,NULL,NULL,7,now());
INSERT INTO book_detail (product_id,author_ar,author_en,pages,published_year) VALUES ((SELECT id FROM product WHERE slug='formative-guide'),'د. سلمان المطيري','Dr. Salman Al-Mutairi',210,2026);
INSERT INTO book_format (product_id,kind,sku,price,compare_at_price,stock_on_hand,weight_grams) VALUES ((SELECT id FROM product WHERE slug='formative-guide'),'print','BK-FORMATIVE-GU-P',85,NULL,500,450);
INSERT INTO stock_movement (book_format_id,delta,reason,ref_type,note,actor) VALUES ((SELECT id FROM book_format WHERE sku='BK-FORMATIVE-GU-P'),500,'restock','manual','Opening stock','seed');
INSERT INTO media_asset (kind,storage_key,is_public,mime,size_bytes) VALUES ('book_file','private/books/formative-guide.pdf',false,'application/pdf',1);
INSERT INTO book_format (product_id,kind,sku,price,compare_at_price,file_asset_id) VALUES ((SELECT id FROM product WHERE slug='formative-guide'),'digital','BK-FORMATIVE-GU-D',45,NULL,(SELECT id FROM media_asset WHERE storage_key='private/books/formative-guide.pdf'));

-- BOOK: letters-tales
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('book','letters-tales','published',(SELECT id FROM category WHERE slug='kids'),'حكايات الحروف','Letter Tales','28 قصة قصيرة، لكل حرف حكاية، تساعد الطفل على القراءة الأولى بمتعة.','28 short stories, one per letter, for joyful first reading.',NULL,NULL,'new',8,now());
INSERT INTO book_detail (product_id,author_ar,author_en,pages,published_year) VALUES ((SELECT id FROM product WHERE slug='letters-tales'),'أ. نوف الحربي','Nouf Al-Harbi',96,2026);
INSERT INTO book_format (product_id,kind,sku,price,compare_at_price,stock_on_hand,weight_grams) VALUES ((SELECT id FROM product WHERE slug='letters-tales'),'print','BK-LETTERS-TALE-P',55,NULL,500,450);
INSERT INTO stock_movement (book_format_id,delta,reason,ref_type,note,actor) VALUES ((SELECT id FROM book_format WHERE sku='BK-LETTERS-TALE-P'),500,'restock','manual','Opening stock','seed');
INSERT INTO media_asset (kind,storage_key,is_public,mime,size_bytes) VALUES ('book_file','private/books/letters-tales.pdf',false,'application/pdf',1);
INSERT INTO book_format (product_id,kind,sku,price,compare_at_price,file_asset_id) VALUES ((SELECT id FROM product WHERE slug='letters-tales'),'digital','BK-LETTERS-TALE-D',25,NULL,(SELECT id FROM media_asset WHERE storage_key='private/books/letters-tales.pdf'));
INSERT INTO sponsorship_offer (product_id,seat_price,unit_label_ar,unit_label_en,target_units,beneficiary_group,group_label_ar,group_label_en,ends_at) VALUES ((SELECT id FROM product WHERE slug='letters-tales'),30,'نسخة','copy',600,'beneficiary_family_children','أطفال الأسر المستفيدة (3–7 سنوات)','Children of beneficiary families (3–7)','2026-12-31 23:59:59+03');
INSERT INTO sponsorship_pool (sponsorship_offer_id) SELECT id FROM sponsorship_offer WHERE product_id=(SELECT id FROM product WHERE slug='letters-tales') AND is_active;
INSERT INTO goal_progress (product_id,seed_units,seed_donors) VALUES ((SELECT id FROM product WHERE slug='letters-tales'),215,74);

-- BOOK: study-planner
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('book','study-planner','published',(SELECT id FROM category WHERE slug='study'),'مخطط المذاكرة السنوي 2027','2027 annual study planner','مخطط أسبوعي وشهري مبني على تقنيات دورة المذاكرة الذكية، مع ملصقات تحفيزية.','Weekly and monthly planner built on the Smart Study method, with motivational stickers.',NULL,NULL,NULL,9,now());
INSERT INTO book_detail (product_id,author_ar,author_en,pages,published_year) VALUES ((SELECT id FROM product WHERE slug='study-planner'),'فريق الجمعية','Association team',180,2026);
INSERT INTO book_format (product_id,kind,sku,price,compare_at_price,stock_on_hand,weight_grams) VALUES ((SELECT id FROM product WHERE slug='study-planner'),'print','BK-STUDY-PLANNE-P',40,NULL,500,450);
INSERT INTO stock_movement (book_format_id,delta,reason,ref_type,note,actor) VALUES ((SELECT id FROM book_format WHERE sku='BK-STUDY-PLANNE-P'),500,'restock','manual','Opening stock','seed');

-- CAUSE: water-for-schools
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('cause','water-for-schools','published',NULL,'سقيا الطلاب — برادات مياه للمدارس','Water for pupils — school water coolers','برّاد مياه مُفلتر لمدرسة في منطقة نائية يخدم قرابة 300 طالب لسنوات.','A filtered water cooler for a remote school, serving around 300 pupils for years.','تفتقر مدارس كثيرة في القرى والهجر إلى مياه شرب نظيفة وباردة، خصوصاً في أشهر الصيف. يُركّب البرّاد ويُصان لمدة عامين، ويُرسل لك تقرير بالصور عند التركيب.','Many village schools lack clean, cool drinking water, especially in summer. Each cooler is installed and maintained for two years, and you receive a photo report on installation.','urgent',10,now());
INSERT INTO cause_detail (product_id,unit_amount,unit_label_ar,unit_label_en,target_amount,allow_custom_amount,zakat_eligible,beneficiaries_ar,beneficiaries_en,is_general_fund,ends_at) VALUES ((SELECT id FROM product WHERE slug='water-for-schools'),1500,'برّاد','cooler',90000,true,false,'60 مدرسة','60 schools',false,'2026-12-31 23:59:59+03');
INSERT INTO goal_progress (product_id,seed_raised,seed_donors) VALUES ((SELECT id FROM product WHERE slug='water-for-schools'),61250,318);

-- CAUSE: tablet-for-student
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('cause','tablet-for-student','published',NULL,'جهاز لوحي لطالب محتاج','A tablet for a student in need','جهاز لوحي محمّل بالمحتوى التعليمي للجمعية، يُسلّم للطالب مع باقة بيانات لفصل دراسي.','A tablet preloaded with the association''s content, handed over with one term of data.','يُختار الطلاب وفق معايير الأهلية المنشورة في صفحة المستفيدين. يُسجّل كل جهاز باسم المستفيد ويُتابَع استخدامه التعليمي.','Students are selected against the published eligibility criteria. Each device is registered to the beneficiary and its learning use is followed up.',NULL,11,now());
INSERT INTO cause_detail (product_id,unit_amount,unit_label_ar,unit_label_en,target_amount,allow_custom_amount,zakat_eligible,beneficiaries_ar,beneficiaries_en,is_general_fund,ends_at) VALUES ((SELECT id FROM product WHERE slug='tablet-for-student'),950,'جهاز','tablet',190000,true,true,'200 طالب','200 students',false,'2027-02-28 23:59:59+03');
INSERT INTO goal_progress (product_id,seed_raised,seed_donors) VALUES ((SELECT id FROM product WHERE slug='tablet-for-student'),72400,402);

-- CAUSE: learning-data
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('cause','learning-data','published',NULL,'باقة إنترنت تعليمية لفصل دراسي','One term of learning data','باقة بيانات مخصّصة للمنصات التعليمية لطالب من أسرة محدودة الدخل.','A data bundle, restricted to learning platforms, for a student from a low-income family.','تُفعَّل الباقة على شريحة باسم ولي الأمر وتقتصر على المنصات التعليمية المعتمدة.','Activated on a SIM in the guardian''s name and restricted to approved learning platforms.',NULL,12,now());
INSERT INTO cause_detail (product_id,unit_amount,unit_label_ar,unit_label_en,target_amount,allow_custom_amount,zakat_eligible,beneficiaries_ar,beneficiaries_en,is_general_fund,ends_at) VALUES ((SELECT id FROM product WHERE slug='learning-data'),120,'باقة','bundle',60000,true,true,'500 طالب','500 students',false,'2026-11-15 23:59:59+03');
INSERT INTO goal_progress (product_id,seed_raised,seed_donors) VALUES ((SELECT id FROM product WHERE slug='learning-data'),51800,733);

-- CAUSE: school-bag
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('cause','school-bag','published',NULL,'الحقيبة المدرسية المتكاملة','The complete school bag','حقيبة بكامل أدواتها وقرطاسيتها مع قصة من إصدارات الجمعية.','A fully stocked school bag with stationery and one of our story books.','تُوزّع الحقائب قبل بداية كل فصل دراسي عبر المدارس الشريكة.','Distributed before each term through partner schools.',NULL,13,now());
INSERT INTO cause_detail (product_id,unit_amount,unit_label_ar,unit_label_en,target_amount,allow_custom_amount,zakat_eligible,beneficiaries_ar,beneficiaries_en,is_general_fund,ends_at) VALUES ((SELECT id FROM product WHERE slug='school-bag'),180,'حقيبة','bag',54000,true,true,'300 طالب','300 students',false,'2027-01-15 23:59:59+03');
INSERT INTO goal_progress (product_id,seed_raised,seed_donors) VALUES ((SELECT id FROM product WHERE slug='school-bag'),17820,129);

-- CAUSE: education-fund
INSERT INTO product (type,slug,status,category_id,title_ar,title_en,summary_ar,summary_en,body_ar,body_en,badge,sort,published_at) VALUES ('cause','education-fund','published',NULL,'صندوق دعم التعليم (تبرع عام)','Education support fund (general)','تبرع بأي مبلغ، وتوجّهه الجمعية إلى البرنامج الأكثر احتياجاً.','Give any amount; the association directs it to the most urgent program.','يُصرف الصندوق وفق خطة الجمعية المعتمدة من مجلس الإدارة، ويُفصح عنه في التقرير المالي السنوي.','Spent under the board-approved plan and disclosed in the annual financial report.',NULL,14,now());
INSERT INTO cause_detail (product_id,unit_amount,unit_label_ar,unit_label_en,target_amount,allow_custom_amount,zakat_eligible,beneficiaries_ar,beneficiaries_en,is_general_fund,ends_at) VALUES ((SELECT id FROM product WHERE slug='education-fund'),NULL,NULL,NULL,500000,true,false,'جميع البرامج','All programs',true,NULL);
INSERT INTO goal_progress (product_id,seed_raised,seed_donors) VALUES ((SELECT id FROM product WHERE slug='education-fund'),143900,1120);

-- Message templates (RUL-63) — Arabic shown; English rows follow the same keys
INSERT INTO message_template (key,channel,locale,subject,body) VALUES
 ('otp','sms','ar',NULL,'رمز الدخول لمتجر تعلّم: {{ code }} — صالح 5 دقائق. لا تشاركه مع أحد.'),
 ('otp','sms','en',NULL,'Your ta3lom store code: {{ code }} — valid 5 minutes. Do not share it.'),
 ('order_paid','sms','ar',NULL,'تم استلام طلبك {{ reference }} بمبلغ {{ total }} ر.س. الإيصال: {{ link }}'),
 ('order_paid','sms','en',NULL,'Order {{ reference }} received, {{ total }} SAR. Receipt: {{ link }}'),
 ('dedication_card','sms','ar',NULL,'{{ to_name }}، تبرّع {{ from_name }} عنك في «{{ product }}». بطاقتك: {{ link }}'),
 ('dedication_card','sms','en',NULL,'{{ to_name }}, {{ from_name }} gave on your behalf to "{{ product }}". Your card: {{ link }}'),
 ('beneficiary_invite','sms','ar',NULL,'مرحباً، خُصّص لك مقعد في دورة «{{ product }}» من جمعية التعلم الإلكتروني. ابدأ: {{ link }}'),
 ('shipment_shipped','sms','ar',NULL,'طلبك {{ reference }} في الطريق. رقم الشحنة {{ tracking }}'),
 ('refund_done','sms','ar',NULL,'تم استرداد {{ amount }} ر.س من الطلب {{ reference }}.'),
 ('cause_update','sms','ar',NULL,'تحديث من «{{ product }}»: {{ title }} {{ link }}');

-- Dedication message templates are stored as settings so staff can edit them
INSERT INTO setting (key, value) VALUES ('dedication_templates', '[
 {"key":"hasanat","ar":"اللهم اجعله في ميزان حسناتك","en":"May it weigh in your good deeds"},
 {"key":"departed","ar":"صدقة جارية عن روح الفقيد","en":"A lasting charity for the departed"},
 {"key":"success","ar":"هدية بمناسبة نجاحك","en":"A gift for your success"},
 {"key":"eid","ar":"عيدكم مبارك","en":"Eid Mubarak"}]');

-- Document counters for the current year
INSERT INTO document_counter (series, year) VALUES ('INV',2026),('CN',2026),('DR',2026),('DRC',2026);

COMMIT;
