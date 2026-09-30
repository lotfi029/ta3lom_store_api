#!/usr/bin/env python3
"""Close-out 7.2 — full smoke scenario from an empty database (Cloud-build-plan §4, Phase 7).

Seed → guest cart (mixed) → OTP sign-in (cart merge) → checkout → dev payment trigger → documents →
enrolment / download / shipment → sponsorship pooling → allocation → sponsor digest → integrity checks (0 rows).

Prerequisites (Development, dev sinks on):
  createdb ta3lom_store_e2e && PGDATABASE=ta3lom_store_e2e tools/scripts/dev-db-migrate.sh --regenerate --seed-catalogue
  ConnectionStrings__Store="…Database=ta3lom_store_e2e…" dotnet src/Ta3lom.Store.Api/bin/Debug/net10.0/Ta3lom.Store.Api.dll \
      create-admin --email e2e-admin@smoke.test --name "E2E Admin"            # prints the one-time password
  start the API on the same database (ASPNETCORE_ENVIRONMENT=Development), then:
  PGDATABASE=ta3lom_store_e2e tools/smoke/e2e.py --admin-email e2e-admin@smoke.test --admin-password '<one-time password>'

Standard library only; the database checks use psql with the libpq variables (PGHOST, PGUSER, PGPASSWORD, PGDATABASE).
"""
import argparse
import base64
import hashlib
import hmac
import json
import re
import struct
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid

ORIGIN = "http://localhost:4200"


class Client:
    def __init__(self, base):
        self.base = base.rstrip("/")
        self.cookies = {}                    # the store's cookies are Secure; http://localhost is treated as secure, as browsers do
        self.opener = urllib.request.build_opener(NoRedirect())
        self.token = None

    def call(self, method, path, body=None, headers=None, raw=False, content_type="application/json"):
        url = path if path.startswith("http") else self.base + path
        data = None
        h = {"Origin": ORIGIN, "Accept-Language": "ar"}
        if self.token:
            h["Authorization"] = "Bearer " + self.token
        if body is not None:
            data = body if isinstance(body, bytes) else json.dumps(body).encode()
            h["Content-Type"] = content_type
        if self.cookies:
            h["Cookie"] = "; ".join(f"{k}={v}" for k, v in self.cookies.items())
        h.update(headers or {})
        req = urllib.request.Request(url, data=data, method=method, headers=h)
        try:
            with self.opener.open(req, timeout=60) as r:
                payload = r.read()
                status, rh = r.status, r.headers
        except urllib.error.HTTPError as e:
            payload, status, rh = e.read(), e.code, e.headers
        for c in rh.get_all("Set-Cookie") or []:
            name, _, rest = c.partition("=")
            value = rest.split(";", 1)[0]
            if value and "max-age=0" not in c.lower() and "expires=thu, 01 jan 1970" not in c.lower():
                self.cookies[name.strip()] = value
            else:
                self.cookies.pop(name.strip(), None)
        if raw:
            return status, payload, rh
        text = payload.decode("utf-8") if payload else ""
        try:
            return status, json.loads(text) if text else None
        except json.JSONDecodeError:
            return status, text


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


steps = 0


def ok(cond, message, detail=None):
    global steps
    if not cond:
        print(f"✗ {message}" + (f"\n  {detail}" if detail is not None else ""))
        sys.exit(1)
    steps += 1
    print(f"✓ {message}")


def psql(sql):
    return subprocess.run(["psql", "-qAtc", sql], capture_output=True, text=True, check=True).stdout.strip()


def totp(secret):
    key = base64.b32decode(secret + "=" * (-len(secret) % 8))
    digest = hmac.new(key, struct.pack(">Q", int(time.time()) // 30), hashlib.sha1).digest()
    o = digest[-1] & 15
    return "%06d" % ((struct.unpack(">I", digest[o:o + 4])[0] & 0x7FFFFFFF) % 1000000)


def staff_login(base, email, password):
    c = Client(base)
    s, b = c.call("POST", "/v1/admin/auth/login", {"email": email, "password": password})
    ok(s == 200, f"staff login {email}", b)
    c.token = b["mfaToken"]
    s, e = c.call("POST", "/v1/admin/auth/totp/enrol")
    ok(s == 200, f"TOTP enrolled for {email}", e)
    s, v = c.call("POST", "/v1/admin/auth/totp/verify", {"code": totp(e["secretBase32"])})
    ok(s == 200, f"TOTP verified for {email}", v)
    c.token = v["accessToken"]
    return c


def latest_sms(dev, mobile_e164):
    s, items = dev.call("GET", "/dev/sms?count=20")
    return [m for m in items if m["to"] == mobile_e164]


def sign_in(client, dev, mobile):
    e164 = "+966" + mobile[1:]
    s, _ = client.call("POST", "/v1/auth/otp/request", {"mobile": mobile})
    ok(s in (200, 202, 204), f"OTP requested for {mobile}")
    code = re.search(r"\d{6}", latest_sms(dev, e164)[0]["body"]).group()
    s, b = client.call("POST", "/v1/auth/otp/verify", {"mobile": mobile, "code": code})
    ok(s == 200 and b.get("accessToken"), f"OTP verified for {mobile}", b)
    client.token = b["accessToken"]


def checkout(client, name, email, shipping=True):
    s, q = client.call("POST", "/v1/checkout/quote", {})
    ok(s == 200, "checkout quote", q)
    body = {"contact": {"fullName": name, "email": email}, "isAnonymous": False, "saveAddress": False, "paymentMethod": "mada",
            "acceptTerms": True, "expectedGrandTotal": q["grandTotal"], "locale": "ar"}
    if shipping:
        body["shippingAddress"] = {"recipientName": name, "recipientMobile": "0551234567", "city": "الرياض", "district": "العليا",
                                   "street": "طريق الملك فهد", "buildingNo": "1234", "postalCode": "12211"}
    s, o = client.call("POST", "/v1/checkout", body, headers={"Idempotency-Key": str(uuid.uuid4())})
    ok(s in (200, 201) and o.get("status") == "pending_payment", f"order {o.get('reference')} placed, {o.get('grandTotal')} SAR", o)
    return o


def pay(dev, reference):
    s, b = dev.call("POST", f"/dev/orders/{reference}/confirm-payment", {})
    ok(s == 200, f"{reference} confirmed by the dev trigger", b)
    ok(psql(f"select status from store.orders where reference = '{reference}'") in ("paid", "fulfilled"), f"{reference} is paid")


def drain_outbox(timeout=60):
    end = time.time() + timeout
    while time.time() < end:
        if psql("select count(*) from store.outbox_message where processed_at is null") == "0":
            return True
        time.sleep(1)
    return False


def minimal_pdf():
    objs = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << >> >>"]
    stream = b"BT ET"
    out = b"%PDF-1.4\n"
    offsets = []
    for i, o in enumerate(objs, 1):
        offsets.append(len(out))
        out += f"{i} 0 obj\n{o}\nendobj\n".encode()
    offsets.append(len(out))
    out += b"4 0 obj\n<< /Length %d >>\nstream\n" % len(stream) + stream + b"\nendstream\nendobj\n"
    xref = len(out)
    out += b"xref\n0 5\n0000000000 65535 f \n" + b"".join(b"%010d 00000 n \n" % off for off in offsets)
    out += b"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n%d\n%%%%EOF\n" % xref
    return out


def multipart(fields, file_field, filename, content, content_type):
    boundary = "----e2e" + uuid.uuid4().hex
    parts = []
    for k, v in fields.items():
        parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{k}\"\r\n\r\n{v}\r\n".encode())
    parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{file_field}\"; filename=\"{filename}\"\r\n"
                 f"Content-Type: {content_type}\r\n\r\n".encode() + content + b"\r\n")
    parts.append(f"--{boundary}--\r\n".encode())
    return b"".join(parts), f"multipart/form-data; boundary={boundary}"


def find(obj, pred):
    if isinstance(obj, dict):
        if pred(obj):
            yield obj
        for v in obj.values():
            yield from find(v, pred)
    elif isinstance(obj, list):
        for v in obj:
            yield from find(v, pred)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="http://localhost:5080")
    ap.add_argument("--admin-email", required=True)
    ap.add_argument("--admin-password", required=True)
    a = ap.parse_args()
    dev = Client(a.base)

    # 1. Seeded catalogue
    s, ready = dev.call("GET", "/health/ready")
    ok(s == 200, "health/ready")
    ok(psql("select count(*) from store.product where status = 'published'") != "0", "reference catalogue seeded")
    ok(psql("select count(*) from store.orders") == "0", "empty database (no orders)")
    products = {}
    for slug in ("smart-study", "science-stars-book", "digital-teacher", "school-bag"):
        s, p = dev.call("GET", f"/v1/catalog/products/{slug}")
        ok(s == 200, f"catalogue product {slug}", p)
        products[slug] = p
    formats = {f["kind"]: f for f in products["science-stars-book"]["book"]["formats"]}

    # 2. Staff: admin (create-admin) and an allocation officer (RUL-62 separation of duties)
    admin = staff_login(a.base, a.admin_email, a.admin_password)
    s, st = admin.call("POST", "/v1/admin/staff", {"email": "e2e-officer@smoke.test", "name": "E2E Officer", "roles": ["allocation_officer"]})
    ok(s == 201, "allocation officer created", st)
    officer = staff_login(a.base, "e2e-officer@smoke.test", st["oneTimePassword"])

    # The digital edition gets a private book file (the seeded asset has no file on this machine).
    body, ctype = multipart({"kind": "book_file"}, "file", "science-stars.pdf", minimal_pdf(), "application/pdf")
    s, asset = admin.call("POST", "/v1/admin/media", body, content_type=ctype)
    ok(s in (200, 201), "book file uploaded (private)", asset)
    s, ap_ = admin.call("GET", f"/v1/admin/products/{products['science-stars-book']['id']}")
    fmt = next(find(ap_, lambda d: d.get("id") == formats["digital"]["id"]))
    s, upd = admin.call("PUT", f"/v1/admin/formats/{fmt['id']}", {
        "kind": fmt["kind"], "sku": fmt["sku"], "price": fmt["price"], "compareAtPrice": fmt.get("compareAtPrice"), "isActive": True,
        "weightGrams": fmt.get("weightGrams"), "lowStockThreshold": fmt.get("lowStockThreshold"), "fileAssetId": asset["id"]})
    ok(s == 200, "digital format linked to the file", upd)

    # 3. Guest cart with every line kind (the prototype's mixed cart)
    buyer = Client(a.base)
    lines = [
        {"productId": products["smart-study"]["id"], "kind": "buy", "mode": "units", "quantity": 1},
        {"productId": products["science-stars-book"]["id"], "bookFormatId": formats["print"]["id"], "kind": "buy", "mode": "units", "quantity": 1},
        {"productId": products["science-stars-book"]["id"], "bookFormatId": formats["digital"]["id"], "kind": "buy", "mode": "units", "quantity": 1},
        {"productId": products["digital-teacher"]["id"], "kind": "sponsor", "mode": "units", "quantity": 2},
        {"productId": products["digital-teacher"]["id"], "kind": "sponsor", "mode": "amount", "quantity": 1, "amount": "100.00", "intent": "sadaqa"},
        {"productId": products["school-bag"]["id"], "kind": "donate", "mode": "units", "quantity": 1, "intent": "sadaqa"},
    ]
    for line in lines:
        s, b = buyer.call("POST", "/v1/cart/lines", line)
        ok(s in (200, 201), f"guest cart + {line['kind']}/{line['mode']} {line.get('quantity')}", b)
    s, cart = buyer.call("GET", "/v1/cart")
    ok(cart["isGuest"] and len(cart["purchases"]) + len(cart["gifts"]) == 6, "guest cart holds 6 lines", cart)

    # 4. OTP sign-in merges the guest cart (RUL-17)
    sign_in(buyer, dev, "0551000001")
    s, cart = buyer.call("GET", "/v1/cart")
    ok(not cart["isGuest"] and len(cart["purchases"]) + len(cart["gifts"]) == 6, "guest cart merged into the account cart", cart)

    # 5. Checkout + dev payment trigger
    order = checkout(buyer, "مشتري تجربة", "buyer@example.com")
    ref = order["reference"]
    pay(dev, ref)
    ok(drain_outbox(), "outbox drained (documents, notifications, LMS)")

    # 6. Documents and notifications
    s, detail = buyer.call("GET", f"/v1/me/orders/{ref}")
    kinds = {d["kind"]: d for d in detail["documents"]}
    ok(s == 200 and {"invoice", "receipt"} <= set(kinds) and all(d["ready"] for d in detail["documents"]),
       f"documents ready: {', '.join(d['number'] for d in detail['documents'])}", detail["documents"])
    for d in detail["documents"]:
        s, pdf, h = buyer.call("GET", f"/v1/me/documents/{d['number']}", raw=True)
        ok(s == 200 and pdf[:4] == b"%PDF", f"{d['number']} downloads as PDF ({len(pdf)} bytes)")
    ok(any("T3-" in m["body"] for m in latest_sms(dev, "+966551000001")), "order-paid SMS sent")
    s, mails = dev.call("GET", "/dev/mail?count=20")
    ok(any(m.get("to") == "buyer@example.com" and ref in (m.get("subject") or "") for m in mails), "order-paid e-mail with the documents sent")

    # 7. Enrolment, download, shipment
    s, enr = buyer.call("GET", "/v1/me/enrolments")
    course = next((e for e in enr if e["product"]["slug"] == "smart-study"), None)
    ok(course and course["status"] in ("active", "pending_sync"), f"enrolment {course and course['status']}", enr)
    s, launch = buyer.call("POST", f"/v1/me/enrolments/{course['id']}/launch")
    ok(s == 200 and launch["url"], "course launch URL issued")
    s, books = buyer.call("GET", "/v1/me/books")
    grant = books["downloads"][0]
    s, link = buyer.call("POST", f"/v1/me/downloads/{grant['grantId']}/link")
    ok(s == 200 and link["remaining"] == 5, "download link issued (5 remaining)", link)
    s, pdf, h = buyer.call("GET", link["url"], raw=True)
    ok(s == 200 and pdf[:4] == b"%PDF", f"watermarked download ({len(pdf)} bytes)")
    s, ships = admin.call("GET", "/v1/admin/shipments?status=preparing")
    ship = next(x for x in ships["items"] if x["orderRef"] == ref)
    s, b = admin.call("POST", f"/v1/admin/shipments/{ship['id']}/ship", {"carrier": "SMSA", "trackingNo": "E2E0001"})
    ok(s == 200 and b["status"] == "shipped", "shipment shipped", b)
    s, b = admin.call("POST", f"/v1/admin/shipments/{ship['id']}/deliver", {"deliveredOn": time.strftime("%Y-%m-%d")})
    ok(s == 200 and b["status"] == "delivered", "shipment delivered", b)
    ok(drain_outbox(), "outbox drained (shipment SMS)")
    ok(psql(f"select status from store.orders where reference = '{ref}'") == "fulfilled", f"{ref} fulfilled (RUL-40)")

    # 8. Pooling (RUL-45): 100 + 300 on a 350 seat → one pooled unit, 50 left in the pool
    donor = Client(a.base)
    sign_in(donor, dev, "0551000002")
    s, b = donor.call("POST", "/v1/cart/lines", {"productId": products["digital-teacher"]["id"], "kind": "sponsor", "mode": "amount",
                                                "quantity": 1, "amount": "300.00", "intent": "sadaqa"})
    ok(s in (200, 201), "second sponsor adds a 300.00 share", b)
    order2 = checkout(donor, "راعٍ ثانٍ", "sponsor2@example.com", shipping=False)
    pay(dev, order2["reference"])
    ok(drain_outbox(), "outbox drained")
    pid = products["digital-teacher"]["id"]
    units = psql(f"select source || ':' || count(*) from store.sponsored_unit where product_id = '{pid}' group by source order by source")
    ok(units.split() == ["full:2", "pooled:1"], f"units: {units.replace(chr(10), ', ')}")
    balance = psql(f"select p.balance from store.sponsorship_pool p join store.sponsorship_offer o on o.id = p.sponsorship_offer_id where o.product_id = '{pid}'")
    ok(balance == "50.00", f"pool balance {balance}")

    # 9. Allocation (RUL-47…52): 3 approved teachers → the officer allocates the 3 units FIFO
    ids = []
    for i in range(3):
        s, ben = admin.call("POST", "/v1/admin/beneficiaries", {"kind": "teacher", "beneficiaryGroup": "rural_private_school_teachers",
                                                               "region": "القصيم", "fullName": f"معلمة تجربة {i + 1}", "mobile": f"055199830{i + 1}",
                                                               "organisationName": "مدرسة تجربة"})
        ok(s == 201, f"beneficiary {ben.get('code')} registered", ben)
        s, b = admin.call("POST", f"/v1/admin/beneficiaries/{ben['id']}/eligibility", {"status": "approved", "zakatEligible": False, "notes": "E2E"})
        ok(s == 200, f"{ben['code']} approved", b)
        ids.append(ben["id"])
    s, b = admin.call("POST", f"/v1/admin/allocation/products/{pid}/allocate", {"beneficiaryIds": ids, "note": "E2E"})
    ok(s == 403, "admin cannot allocate (separation of duties)")
    s, b = officer.call("POST", f"/v1/admin/allocation/products/{pid}/allocate", {"beneficiaryIds": ids, "note": "E2E"})
    ok(s == 200 and b["allocated"] == 3, "officer allocated 3 units", b)
    ok(drain_outbox(), "outbox drained (invitations, LMS)")
    invited = sum(1 for i in range(3) if latest_sms(dev, f"+96655199830{i + 1}"))
    ok(invited == 3, "3 beneficiary invitations sent")

    # 10. Sponsor digest (RUL-63): both sponsors get one; a second run sends nothing
    s, _ = dev.call("POST", "/dev/jobs/SponsorDigestJob/run")
    time.sleep(3)
    ok(drain_outbox(), "outbox drained (digests)")
    digests = psql("select count(*) from store.notification where template_key = 'sponsor_digest' and status = 'sent'")
    ok(digests == "2", f"{digests} sponsor digests sent")
    s, _ = dev.call("POST", "/dev/jobs/SponsorDigestJob/run")
    time.sleep(3)
    ok(psql("select count(*) from store.outbox_message where type = 'SponsorDigest'") == "2", "second digest run in the month queues nothing")

    # 11. Integrity checks (db/reference/003): every query returns 0 rows
    out = subprocess.run(["psql", "-qAt", "-f", "db/reference/003_integrity_checks.sql"], capture_output=True, text=True)
    rows = [l for l in out.stdout.splitlines() if l.strip() and l.strip() != "SET"]
    ok(out.returncode == 0 and not rows, "integrity checks: 0 rows", rows[:10])
    print(f"E2E passed: {steps} checks")


if __name__ == "__main__":
    main()
