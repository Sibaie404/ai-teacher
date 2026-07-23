# EuphratesWeb — Website Plan & Strategy

A living plan for the EuphratesWeb corporate site and its divisions. Target
buyer: **mid-to-large enterprises, especially banks and other regulated
financial institutions.**

---

## 1. Positioning

**EuphratesWeb is a house of AI divisions that ships enterprise-grade solutions
with the security and rigor regulated businesses require.**

One parent brand, four divisions:

| Division | One-liner | Sells to a bank as… |
|---|---|---|
| **Euphrates Voice** | Human-quality AI voice agents for phone & call centers | 24/7 account servicing line, fraud-alert callbacks, appointment booking, IVR replacement |
| **Euphrates Automation** | Back-office & workflow automation with AI agents | KYC/onboarding, document processing, reconciliation, ticket triage |
| **Euphrates Web** | Enterprise websites & web platforms | Secure customer portals, marketing sites, internal tools |
| **Euphrates Apps** | Custom mobile & web applications | Branded apps, staff tooling, customer-facing products |

The **parent site sells trust and books the consultation.** Divisions are
solution pages/sections beneath it — not four separate websites (yet).

---

## 2. What the market leaders do (research summary)

Studied Sierra (~$15.8B), Decagon (~$4.5B), PolyAI, Parloa, and Cognigy (NiCE).
Consistent playbook to copy:

1. **Outcome-led hero** + a single primary CTA ("Book a demo"). No feature dump.
2. **Proof immediately** — client logos, then *quantified* outcomes
   (% autonomously resolved, CSAT, hours/cost saved).
3. **A serious Security & Trust story** — SOC 2, GDPR/PCI, data residency,
   "we never train public models on your data," deploy in *your* environment.
   This is the single biggest unlock for banks.
4. **Voice-first depth vs omnichannel breadth** — position each division on the
   depth of what it does, in the customer's language, not ours.
5. **Managed / governed / regulated** vocabulary throughout — enterprise buyers
   are buying risk reduction, not novelty.

Sources:
- https://cresta.com/guides/decagon-vs-sierra
- https://www.parloa.com/knowledge-hub/best-voice-ai-companies-customer-support/
- https://www.retellai.com/blog/best-voice-ai-agents-for-banking
- https://www.lorikeetcx.ai/articles/best-voice-ai-financial-services-integration-2026

---

## 3. Site map

```
/  (single-page for v1, split into routes as content grows)
├── Hero + primary CTA (Book a consultation)
├── Who we build for (industries, not fake logos)
├── The four divisions (Voice / Automation / Web / Apps)
├── Outcomes / metrics band          ← replace placeholders with real case data
├── How we work (Discovery → Design → Build → Deploy → Support)
├── Security & Trust                 ← the bank unlock; becomes its own page later
├── About EuphratesWeb
├── Book a consultation ("Tell us about you" intake + calendar)
└── Footer (divisions, contact, legal)
```

Grow into real routes later: `/voice`, `/automation`, `/web`, `/apps`,
`/security`, `/work`, `/about`, `/contact`.

---

## 4. Design direction

- **Dark, premium, substance-forward.** Deep navy/near-black, a teal→cyan
  "Euphrates water" accent, restrained gold micro-accents. Blues read as trust
  to financial buyers.
- Large confident typography, generous whitespace, subtle motion only.
- Real numbers and real words over stock imagery and animation.
- Fully responsive; accessible contrast.

The v1 in `website/index.html` is a **self-contained static page** (no build
step, no external requests) so it runs locally and previews anywhere.

---

## 5. Build phases

- **Phase 0 — Swarm foundation** ✅ (Dockerfile + `deploy/stack.yml` + Traefik).
- **Phase 1 — This marketing site.** Static v1 now → migrate to Next.js when we
  need a CMS, real forms, and per-division routes.
- **Phase 2 — Wire the consultation form** to a real backend (email/CRM +
  calendar embed such as Cal.com / Calendly).
- **Phase 3 — Security page + first case study** (start with the AI Teacher as a
  live Euphrates Voice showcase).
- **Phase 4 — Deploy the site as another service in the Swarm stack.**

---

## 6. Honesty / integrity notes (read before going live)

- The **metrics band uses placeholder numbers** — replace with real, defensible
  case data before publishing. Marked with a comment in the HTML.
- **No fabricated client logos.** We show target *industries* until we have
  real, permissioned references.
- **Security claims must be true.** Say "SOC 2 in progress" only if it is; don't
  claim certifications you don't hold — banks verify.
