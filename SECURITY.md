# Security Policy

## Supported versions

codekb is in MVP stage and does not yet ship tagged releases. Security fixes land on `main` and are picked up by anyone tracking the head of the branch. Once we cut the first `v0.x` tag, this section will list the supported version range.

| Version | Supported |
|---|---|
| `main` (pre-release) | ✅ |

## Reporting a vulnerability

**Please do not open a public GitHub issue for a security vulnerability.**

Instead, report it privately via one of the following channels:

1. **GitHub Security Advisories** — preferred. Open a draft advisory at
   <https://github.com/Zoff-Tech/codekb/security/advisories/new>. This keeps the
   discussion private until a fix is ready.
2. **Email** — `security@bidgenie.co`. PGP-encrypted mail is welcome; the key
   is published on the same address's keybase profile.

Include in your report:

- A description of the issue and its impact.
- Steps to reproduce, or a proof-of-concept.
- Affected commit SHA(s) on `main`.
- Your suggested remediation, if any.
- Whether you intend to disclose publicly, and on what timeline.

## What to expect

- We will acknowledge receipt within **3 business days**.
- We aim to triage and confirm (or rule out) the report within **10 business
  days**.
- For confirmed vulnerabilities we will agree a coordinated disclosure
  timeline with the reporter. Our default target is a fix within **30 days**
  of confirmation for high-severity issues, longer for issues that require
  significant refactoring.
- We will credit reporters in the release notes (and the GitHub advisory)
  unless you ask us not to.

## Scope

In scope:

- The `CodeKb.*` libraries and CLI in this repository.
- Build, packaging, and CI workflows under `.github/`.
- Documentation that recommends an insecure configuration.

Out of scope:

- Vulnerabilities in upstream dependencies — please report those upstream.
  We will respond to advisories filed against our dependency tree once they
  are public.
- Findings that require the attacker to already have write access to
  `config/codekb.yaml`, the Postgres database, or the host filesystem.
- Findings against deliberately permissive defaults that are documented as
  such (e.g., the dev-only Docker compose recipe in the README).

## Hardening checklist for operators

If you run codekb against private repositories, please:

- **Never** put `gitToken`, `embeddingApiKey`, or `postgresConnectionString`
  credentials in the YAML config. codekb rejects YAML files that contain
  credential keys, but the surrounding deployment system should not store
  them either.
- Provide credentials via environment variables, your platform's secret
  store (AWS Secrets Manager, GCP Secret Manager, Azure Key Vault, HashiCorp
  Vault), or an OS keychain.
- Use a least-privilege Postgres role: `CONNECT`, `SELECT`, `INSERT`,
  `UPDATE`, `DELETE` on the codekb schema only. No `SUPERUSER`, no
  `CREATEDB`.
- Use a read-only Git token if your VCS supports it.
- Monitor the `records_redaction_failed` counter on scan jobs. A non-zero
  value means codekb detected a secret it could not safely transform and
  dropped the record; investigate the offending file.

Thanks for helping keep codekb and its users safe.
