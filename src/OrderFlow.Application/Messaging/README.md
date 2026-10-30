# Durable messaging boundary

Reserved for the transactional outbox/inbox and lease protocol in ADR-005. No transport, publisher, fake job loop, or in-memory durability substitute is registered now. Minimal outbox persistence begins with orders in milestone 11; dispatch/inbox and worker jobs begin in milestone 14.
