import { ApiError } from '../api/client';
import { isProductId } from '../api/catalog';
export interface AdminCommand {
    resource: string;
    method: 'POST' | 'PUT';
    body: Record<string, unknown>;
}
export function replayable(command: AdminCommand) {
    return (
        /^products\/[0-9a-f-]+\/stock-(receipts|adjustments)$/.test(command.resource) ||
        /^email-deliveries\/[0-9a-f-]+\/retry$/.test(command.resource)
    );
}
export function validateCommand(command: AdminCommand): AdminCommand {
    const parts = command?.resource?.split('/') ?? [];
    const body = command?.body;
    if (!body || typeof body !== 'object' || Array.isArray(body))
        throw new Error('Invalid Admin recovery body.');
    let fields: string[];
    if (parts.length === 1 && ['products', 'categories'].includes(parts[0]!) && command.method === 'POST')
        fields = parts[0] === 'products' ? ['sku', 'name', 'description', 'price', 'categoryId'] : ['name'];
    else if (
        parts.length === 2 &&
        ['products', 'categories'].includes(parts[0]!) &&
        isProductId(parts[1]!) &&
        command.method === 'PUT'
    )
        fields =
            parts[0] === 'products'
                ? ['name', 'description', 'price', 'categoryId', 'isActive', 'expectedVersion']
                : ['name', 'isActive', 'expectedVersion'];
    else if (parts.length === 3 && isProductId(parts[1]!) && command.method === 'POST') {
        if (['products', 'categories'].includes(parts[0]!) && parts[2] === 'archive')
            fields = ['expectedVersion'];
        else if (parts[0] === 'products' && ['stock-receipts', 'stock-adjustments'].includes(parts[2]!))
            fields =
                parts[2] === 'stock-receipts'
                    ? ['operationId', 'quantity', 'reason']
                    : ['operationId', 'delta', 'reason'];
        else if (parts[0] === 'orders' && parts[2] === 'transitions')
            fields = ['targetStatus', 'expectedVersion', 'reason'];
        else if (parts[0] === 'email-deliveries' && parts[2] === 'retry')
            fields = ['operationId', 'reason', 'acknowledgeDuplicateRisk'];
        else throw new Error('Unsupported Admin command.');
    } else throw new Error('Unsupported Admin command.');
    if (
        Object.keys(body).some((field) => !fields.includes(field)) ||
        Object.values(body).some((value) => !['string', 'number', 'boolean'].includes(typeof value))
    )
        throw new Error('Invalid Admin command fields.');
    if (replayable(command) && (typeof body.operationId !== 'string' || !isProductId(body.operationId)))
        throw new Error('Missing durable operation identity.');
    return command;
}
export const adminRecoveryKey = (id: string) => `orderflow-admin-command:v1:${id.toLowerCase()}`;
export class AdminRecovery {
    private command: AdminCommand | null = null;
    private error: string | null = null;
    private running = false;
    private executing = false;
    private listeners = new Set<() => void>();
    private snapshot: { command: AdminCommand | null; error: string | null; running: boolean } = {
        command: null,
        error: null,
        running: false,
    };
    constructor(
        readonly userId: string,
        private readonly storage: Pick<Storage, 'getItem' | 'setItem'> = {
            getItem: (key) => localStorage.getItem(key),
            setItem: (key, value) => localStorage.setItem(key, value),
        },
        private readonly lock: <T>(task: () => Promise<T>) => Promise<T> = (task) => {
            if (!navigator.locks)
                throw new Error('A supported browser with Web Locks is required for Admin mutations.');
            return navigator.locks.request(adminRecoveryKey(userId), task);
        },
    ) {
        this.reload();
    }
    getSnapshot = () => this.snapshot;
    subscribe = (fn: () => void) => {
        this.listeners.add(fn);
        return () => {
            this.listeners.delete(fn);
        };
    };
    private publish() {
        this.snapshot = { command: this.command, error: this.error, running: this.running };
        this.listeners.forEach((fn) => fn());
    }
    reload = () => {
        try {
            const raw = this.storage.getItem(adminRecoveryKey(this.userId));
            if (!raw && this.command) throw new Error('Recovery disappeared.');
            if (raw && (raw.length > 12_288 || new TextEncoder().encode(raw).byteLength > 12_288))
                throw new Error('Oversized recovery.');
            const value = raw
                ? (JSON.parse(raw) as { version: number; command: AdminCommand | null })
                : { version: 1, command: null };
            if (
                value.version !== 1 ||
                !(
                    value.command === null ||
                    (typeof value.command === 'object' && value.command && !Array.isArray(value.command))
                )
            )
                throw new Error('Unsupported recovery format.');
            this.command = value.command ? validateCommand(value.command) : null;
            this.error = null;
        } catch {
            this.error =
                'Admin recovery data is unavailable or invalid. Inspect server state before explicitly closing/resetting local recovery.';
        }
        this.publish();
    };
    private save(command: AdminCommand | null) {
        const serialized = JSON.stringify({ version: 1, command });
        if (new TextEncoder().encode(serialized).byteLength > 12_288)
            throw new Error('Admin recovery exceeds the 12 KiB limit. No request was sent.');
        this.storage.setItem(adminRecoveryKey(this.userId), serialized);
        this.command = command;
        this.error = null;
        this.publish();
    }
    async execute<T>(
        command: AdminCommand,
        send: (command: AdminCommand) => Promise<T>,
        recover = false,
    ): Promise<T> {
        // Fence duplicate submits synchronously, including before React renders disabled controls.
        if (this.executing) throw new Error('An Admin operation is already in progress.');
        this.executing = true;
        try {
            return await this.lock(async () => {
                this.reload();
                if (this.error) throw new Error(this.error);
                const prior = this.command;
                if (prior && (!recover || !replayable(prior)))
                    throw new Error('Resolve the saved Admin operation before submitting another.');
                const actual = prior ?? validateCommand(command);
                this.save(actual); // Durable exact payload BEFORE the request; failure blocks it.
                this.running = true;
                this.publish();
                try {
                    const result = await send(actual);
                    this.save(null);
                    return result;
                } catch (error) {
                    if (
                        !prior &&
                        error instanceof ApiError &&
                        [400, 403, 404, 409, 422].includes(error.status)
                    )
                        this.save(null);
                    throw error;
                } finally {
                    this.running = false;
                    this.publish();
                }
            });
        } finally {
            this.executing = false;
        }
    }
    close() {
        return this.lock(async () => {
            this.save(null);
        });
    }
}
