# Seven-minute portfolio demo

Preparation is outside the presentation time. This is a synthetic local simulator demo, not a real payment integration. Run the [README quick start](../README.md) first; keep the generated private directory and all volumes. Required for the running demo: Docker/Compose, Python3.12+, OpenSSL, browser with an explicit exception/trust for this generated localhost certificate, free loopback18448/5093/8028 and subnet172.29.246/24. Initial build/registry downloads can take minutes. No Node/.NET SDK is needed for the containerized manual demo; automated browser rehearsal additionally needs Node24.19.0/npm11.17.0, client dependencies and matching Playwright Chromium.

Use two browser profiles/private contexts: Customer and Admin. Open `.local/<demo>/demo.json` and `bootstrap.json` in a **private editor** to enter their generated credentials. Do not display passwords in a presentation, shell history, recording or committed file. Only .invalid fixture accounts are seeded; public registration can create Customers, never Admins. Seed has three USD12.50 products and8 units each; opening stock uses real audited API receipts. `up` refuses a populated DB and never resets existing credentials/data. Pick a fresh directory/project to repeat the entire presentation.

```bash
demo_dir="$PWD/.local/portfolio-demo"  # Use the SAME fresh directory chosen in README.
python3 infra/demo/demo.py check --directory "$demo_dir"
```

| Time | Action and expected visible evidence | Explain |
| --- | --- | --- |
|0:00–0:50|Customer signs in at `https://localhost:18448/login`, reloads Account, browses Catalog and opens **Demo Browse**. Add quantity1, follow **View your cart**; estimate is$12.50.|Cookie session restores; catalog/category/search/page keys belong to Query; cart persists only IDs/quantities and reserves nothing.|
|0:50–1:40|**Continue to checkout → Create order & reserve stock → View order & finish checkout**. Copy the order UUID from URL. It is PendingPayment with server expiry/countdown. Click **Start payment**.|One intention retains exact body/key before POST. PostgreSQL snapshots price and reserves stock atomically;202 payment is acceptance, not captured revenue.|
|1:40–2:35|In terminal run the `pay` command below with that UUID. Keep Customer order page open until Confirmed and **Applied to this order** appear.|Operator CLI talks to the Development simulator, submits a signed callback; workers/reconciliation determine state. No simulator key or fake success enters React. Countdown/redirect are informational.|
|2:35–3:45|In separate Admin profile sign in; open `/admin/orders/<UUID>`. Enter reason and click **Advance to Processing**, then **Shipped**, then **Completed**.|Expected versions/state graph are enforced by API/SQL; Admin cannot skip edges and Customer direct Admin requests return403. This is fulfillment status only, no carrier integration.|
|3:45–4:35|Return to the still-open Customer page; it refreshes to Completed. Open Notifications. In Admin, open financial/history audit or Reports, select today's **UTC** date through tomorrow as exclusive `to`.|Live connection hints invalidate Query; polling/focus/reconnect also catch up durable HTTP state. One allocated capture contributes paid sales; all captures, succeeded refunds and net are distinct. Audit has no edit control.|
|4:35–5:20|Run `duplicate` below. It sends two same-user/key/body requests through replicas; outputs one order and one reserved unit.|Original checkout identity and stored response survive retries; this is a demonstration, while deterministic SQL gates in tests prove specific races. Repeating this command keeps the original intention.|
|5:20–6:35|Run `outage` below. It stops only this demo's RabbitMQ, creates one durable checkout, observes a pending outbox row/API readiness, restarts the broker and waits for publication/one durable notification.|Checkout does not require broker connectivity. Original EventId/inbox prevent duplicate local effects; accepted external sends can still duplicate. No retry counter reset or replacement payment/refund identity.|
|6:35–7:00|Show the acceptance audit/remaining-work list. Mention explicit shared-key initialization, cache30s bound, single-machine measurements and no production deployment.|Explain limits without claiming production scale, exactly-once delivery or real customers.|

```bash
python3 infra/demo/demo.py pay --directory "$demo_dir" --order <UUID-from-Customer-order-URL>
python3 infra/demo/demo.py duplicate --directory "$demo_dir"
python3 infra/demo/demo.py outage --directory "$demo_dir"
# Stop only this demo; preserve every named volume and private file.
python3 infra/demo/demo.py down --directory "$demo_dir"
```

Use a real UUID instead of the angle-bracket placeholder. `pay` requires an unexpired PendingPayment order owned by the generated demo Customer; do not wait15minutes between checkout and payment. It reuses an active attempt, persists its own exact initiation key before sending, resolves the existing provider payment and relays freshly signed exact bytes. It does not mark an order paid itself. Normal browser returns never prove payment. Pending/NeedsReview states are intentionally visible; there is no “force paid” or general refund retry endpoint.

Duplicate/recovery requests use separate fixture SKUs, so the stock assertions do not depend on the interactive cart. The scripts persist original keys/payloads before transport and do not replace an uncertain operation. A failed seed requires a new lab; a failed financial request requires inspection/replay of the original intention, not deleting the journal. `outage` restores the broker in finally and allows60s including restart for observed recovery; if it fails, keep evidence and investigate. Broker readiness can recover before consumption; the script also requires a durable notification. A prolonged outage may exhaust retries or suspend API hint dispatch; repair and explicitly restart affected hosts, retaining identities/counters. It does not silently manufacture success.

Optional Mailpit is `http://localhost:8028`, the simulator machine endpoint is loopback5093. Do not paste ControlKey/ApiKey into a browser page. This repository has **operator-controlled payment simulation**, not a hosted customer payment page. Confirmed email can appear in Mailpit; its acceptance is not a promise of recipient delivery/exactly-once SMTP.

## Automated rehearsal

Use a separate freshly seeded demo, before manually creating orders. It consumes the Browse/Replay/Recovery fixtures, so do not run it midway through the human script.

```bash
npm ci --prefix client
(cd client && npx --no-install playwright install --with-deps chromium)
ORDERFLOW_DEMO_DIRECTORY="$demo_dir" node infra/demo/rehearse.mjs
```

The rehearsal launches the real built UI with only this fixture's TLS public-key pin, separate Customer/Admin contexts, session restore, cart/checkout, simulator payment, fulfillment, connected live UI/durable refresh, direct Customer Admin denial, duplicate request and broker recovery. No API response mocking, test-control routes, screenshots, raw traces or persistent browser auth state. It writes only structural pass/timing evidence under private `results/rehearsal.json`; existing SignalR transport tests independently prove cross-node hints, so this script does not pretend polling fallback proves an individual hint's receipt. Failures stop visibly; use a new demo for a complete rerun. See [audit results](acceptance-audit.md) for what was actually executed.
