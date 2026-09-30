#!/usr/bin/env python3
"""H-1 local load smoke (plan 14 §12, 09 §10.6) — a scripted stand-in for k6, which is not installable here.

1. Catalogue: N GET requests over C threads on home, product list and product detail → p50 / p95 / p99, errors.
2. 50 concurrent checkouts up to `pending_payment`: 50 customers sign in (OTP via the dev SMS sink), each puts one course in
   the cart, then all 50 `POST /v1/checkout` start together behind a barrier.
3. A burst of the 50 settlements through the dev trigger (`POST /dev/orders/{ref}/confirm-payment`), all at once.

Development only (dev sinks, dev trigger). Each virtual client sends its own X-Forwarded-For (the Development config trusts
loopback proxies), so per-IP limits apply per client as they would in production. Standard library only.
Usage: tools/load/load-smoke.py [--base http://localhost:5080] [--requests 3000] [--threads 30] [--checkouts 50]
"""
import argparse
import json
import re
import statistics
import threading
import time
import urllib.error
import urllib.request
import uuid
from concurrent.futures import ThreadPoolExecutor

ORIGIN = "http://localhost:4200"


def call(base, method, path, body=None, token=None, ip=None, headers=None):
    h = {"Origin": ORIGIN}
    if token:
        h["Authorization"] = "Bearer " + token
    if ip:
        h["X-Forwarded-For"] = ip
    data = None
    if body is not None:
        data = json.dumps(body).encode()
        h["Content-Type"] = "application/json"
    h.update(headers or {})
    start = time.perf_counter()
    try:
        with urllib.request.urlopen(urllib.request.Request(base + path, data=data, method=method, headers=h), timeout=60) as r:
            payload, status = r.read(), r.status
    except urllib.error.HTTPError as e:
        payload, status = e.read(), e.code
    elapsed = (time.perf_counter() - start) * 1000
    try:
        parsed = json.loads(payload) if payload else None
    except json.JSONDecodeError:
        parsed = None
    return status, parsed, elapsed


def pct(values, p):
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(round(p / 100 * (len(ordered) - 1))))]


def report(name, latencies, statuses):
    errors = sum(1 for s in statuses if s >= 400)
    print(f"{name}: n={len(latencies)} p50={pct(latencies, 50):.1f} ms p95={pct(latencies, 95):.1f} ms "
          f"p99={pct(latencies, 99):.1f} ms max={max(latencies):.1f} ms mean={statistics.mean(latencies):.1f} ms errors={errors}")
    return errors


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="http://localhost:5080")
    ap.add_argument("--requests", type=int, default=3000)
    ap.add_argument("--threads", type=int, default=30)
    ap.add_argument("--checkouts", type=int, default=50)
    ap.add_argument("--course", default="smart-study")
    a = ap.parse_args()
    base = a.base.rstrip("/")

    # 1. Catalogue
    s, prod, _ = call(base, "GET", f"/v1/catalog/products/{a.course}")
    paths = ["/v1/catalog/home", "/v1/catalog/products?page=1&pageSize=20", f"/v1/catalog/products/{a.course}"]
    lat, sts = [], []
    lock = threading.Lock()

    def one(i):
        st, _, ms = call(base, "GET", paths[i % len(paths)], ip=f"10.1.{i % 250}.{(i // 250) % 250 + 1}")
        with lock:
            lat.append(ms)
            sts.append(st)

    t0 = time.perf_counter()
    with ThreadPoolExecutor(a.threads) as pool:
        list(pool.map(one, range(a.requests)))
    wall = time.perf_counter() - t0
    errs = report(f"catalogue ({a.threads} threads, {a.requests / wall:.0f} req/s)", lat, sts)

    # 2. Checkouts up to pending_payment
    run = uuid.uuid4().int % 10_000_000
    mobiles = [f"055{(run + i) % 10_000_000:07d}" for i in range(a.checkouts)]

    def prepare(i):
        ip = f"10.2.{i // 250}.{i % 250 + 1}"
        mobile = mobiles[i]
        call(base, "POST", "/v1/auth/otp/request", {"mobile": mobile}, ip=ip)
        _, sms, _ = call(base, "GET", "/dev/sms?count=200")
        e164 = "+966" + mobile[1:]
        code = next(re.search(r"\d{6}", m["body"]).group() for m in sms if m["to"] == e164)
        _, v, _ = call(base, "POST", "/v1/auth/otp/verify", {"mobile": mobile, "code": code}, ip=ip)
        token = v["accessToken"]
        call(base, "POST", "/v1/cart/lines", {"productId": prod["id"], "kind": "buy", "mode": "units", "quantity": 1}, token=token, ip=ip)
        _, q, _ = call(base, "POST", "/v1/checkout/quote", {}, token=token, ip=ip)
        return token, ip, q["grandTotal"]

    with ThreadPoolExecutor(10) as pool:
        clients = list(pool.map(prepare, range(a.checkouts)))
    barrier = threading.Barrier(a.checkouts)

    def place(c):
        token, ip, total = c
        body = {"contact": {"fullName": "حمل تجريبي", "email": "load@example.com"}, "isAnonymous": False, "saveAddress": False,
                "paymentMethod": "mada", "acceptTerms": True, "expectedGrandTotal": total, "locale": "ar"}
        barrier.wait()
        return call(base, "POST", "/v1/checkout", body, token=token, ip=ip, headers={"Idempotency-Key": str(uuid.uuid4())})

    with ThreadPoolExecutor(a.checkouts) as pool:
        results = list(pool.map(place, clients))
    errs += report(f"{a.checkouts} concurrent checkouts", [r[2] for r in results], [r[0] for r in results])
    refs = [r[1]["reference"] for r in results if r[0] in (200, 201) and r[1].get("status") == "pending_payment"]
    print(f"  pending_payment orders: {len(refs)}/{a.checkouts}")

    # 3. Burst of settlements through the dev trigger
    barrier = threading.Barrier(len(refs))

    def settle(ref):
        barrier.wait()
        return call(base, "POST", f"/dev/orders/{ref}/confirm-payment", {})

    t0 = time.perf_counter()
    with ThreadPoolExecutor(len(refs)) as pool:
        settled = list(pool.map(settle, refs))
    wall = time.perf_counter() - t0
    errs += report(f"{len(refs)} settlements in {wall:.2f} s", [r[2] for r in settled], [r[0] for r in settled])
    print("LOAD SMOKE " + ("PASSED" if errs == 0 and len(refs) == a.checkouts else "FAILED"))


if __name__ == "__main__":
    main()
