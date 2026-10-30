# Business modules

ADR-001 deliberately keeps domain and application code in this one assembly. The project is currently empty: there are no business rules, services, or placeholder entities to invoke.

As features are implemented, group them under `Modules/Identity`, `Catalog`, `Inventory`, `Ordering`, `Payments`, `Notifications`, and `Reporting`. Within a module, keep domain entities/rules and application use cases clearly separated by feature folders (for example, `Modules/Ordering/Domain` and `Modules/Ordering/CreateOrder`). Do not generate empty classes or an extra project per module/layer.

Domain code holds business invariants; use cases orchestrate authorization inputs and a unit of work. Infrastructure belongs in the sibling Persistence/Messaging folders or the owning module's provider-adapter folder. Both hosts call the same use cases. Module ownership remains a review convention inside this assembly, rather than a compile-time boundary.
