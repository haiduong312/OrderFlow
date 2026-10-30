You are implementing OrderFlow, a portfolio project for a Middle-level ASP.NET Core / Full-stack developer. Implement the current milestone, not just a plan. Keep the code understandable enough that I can explain it in an interview.

WORKING RULES

- Read repository instructions and inspect existing code, tests, package versions, migrations, and git status before editing. Preserve unrelated work. Adapt existing code instead of recreating it unnecessarily.

- Follow docs/project-spec.md, docs/architecture.md, docs/implementation-status.md, and recorded ADRs once they exist. Report contradictions before implementing dependent behavior. Resolve routine details autonomously and record important decisions.

- Implement only this milestone and its necessary prerequisites. Do not add unrelated packages, patterns, services, or future features. Do not spawn agents unless I explicitly ask.

- Do not commit, push, deploy, rewrite git history, or delete user data. Return a suggested commit message only. Do not put real credentials in code, fixtures, logs, or git.

- Use the supported stable SDK/package versions compatible with the repository. Pin actual versions. Consult official documentation for version-sensitive APIs. Do not use previews or invent API signatures. If execution is unavailable, report the exact blocker and do not claim checks passed.

- Use async I/O and propagate CancellationToken where appropriate. Keep controllers thin and enforce business rules server-side. Never trust frontend totals, roles, ownership, inventory, or payment success.

- Use an EF Core DbContext scoped to a unit of work; never share it across concurrent tasks. Background workers must create appropriate DI scopes.

- Keep correctness-critical coordination in PostgreSQL. In-process locks are insufficient across processes. Transactions, conditional SQL updates, unique constraints, and explicit state transitions must protect invariants.

- Use bounded retries only for classified transient failures and make retried operations safe. Do not catch exceptions broadly and return fake success, empty data, or HTTP 200. Never claim exactly-once external side effects.

- Do not perform network calls while holding business database transactions open. Persist the intention, call outside the transaction, and reconcile uncertain outcomes.

- Store monetary values as decimal/numeric(18,2), currency USD for v1, positive prices, and integer quantities. Public JSON money values use invariant decimal strings. Keep frontend calculations exact using cents/decimal arithmetic. Backend totals are authoritative.

- Use UTC DateTimeOffset and injected TimeProvider for business time. Define expiry boundary behavior consistently and test it without real sleeps.

- For browser authentication, use ASP.NET Core Identity with HttpOnly cookies under one browser origin. Enforce antiforgery on browser state-changing endpoints. Provider webhooks use separate signed authentication and explicit antiforgery exemption. Do not mix in homemade JWT/refresh-token infrastructure.

- Unit-test meaningful domain rules. Integration-test SQL, constraints, authorization, transactions, and HTTP using actual PostgreSQL, not EF InMemory. Add tests for new behavior and relevant regressions; do not endlessly repeat unrelated checks.

- Use deterministic synchronization for race tests. Do not use arbitrary sleeps to claim concurrency correctness. Test every participant actually reached the contested operation.

- Keep logs structured and redact secrets. Record failures instead of hiding them. Keep optional external dependencies out of the critical checkout transaction.

AFTER EACH MILESTONE

1. Summarize behavior implemented and explain the main code flow in Vietnamese.

2. List important files and any new database migration.

3. Report the exact checks run, results, and unverified limitations.

4. Give manual verification commands/steps and known trade-offs.

5. Update docs/implementation-status.md with completed acceptance criteria and remaining work.

6. Give 3 short interview questions about this milestone and explain what I should understand before proceeding.

7. Return the suggested commit title supplied with the milestone, adjusted only if the implemented scope differs.
