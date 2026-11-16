export interface ProblemDetails {
    type?: string;
    title: string;
    status: number;
    code: string;
    traceId: string;
    errors: Record<string, string[]>;
}

export class ApiError extends Error {
    constructor(
        public readonly problem: ProblemDetails,
        public readonly retryAfter: string | null = null,
    ) {
        super(problem.title);
        this.name = 'ApiError';
    }
    get status() {
        return this.problem.status;
    }
    get code() {
        return this.problem.code;
    }
    get traceId() {
        return this.problem.traceId;
    }
    field(name: string) {
        return Object.entries(this.problem.errors)
            .filter(([key]) => key.toLowerCase() === name.toLowerCase())
            .flatMap(([, messages]) => messages);
    }
}

function object(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export async function responseProblem(response: Response): Promise<ApiError> {
    let body: unknown;
    try {
        body = await response.json();
    } catch {
        /* A failed HTTP response remains an error even without JSON. */
    }
    const fields =
        object(body) && object(body.errors)
            ? Object.fromEntries(
                  Object.entries(body.errors).filter(
                      (entry): entry is [string, string[]] =>
                          Array.isArray(entry[1]) && entry[1].every((item) => typeof item === 'string'),
                  ),
              )
            : {};
    return new ApiError(
        {
            status: response.status, // The transport status is authoritative, not a conflicting body.status.
            title:
                object(body) && typeof body.title === 'string'
                    ? body.title
                    : `Request failed (${response.status}).`,
            code: object(body) && typeof body.code === 'string' ? body.code : 'http_error',
            traceId:
                object(body) && typeof body.traceId === 'string'
                    ? body.traceId
                    : (response.headers.get('X-Trace-Id') ?? ''),
            type: object(body) && typeof body.type === 'string' ? body.type : undefined,
            errors: fields,
        },
        response.headers.get('Retry-After'),
    );
}

interface Csrf {
    requestToken: string;
    headerName: string;
}
interface RequestOptions {
    method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
    body?: unknown;
    signal?: AbortSignal;
}

export class ApiClient {
    private csrf: Csrf | null = null;
    private csrfEpoch = 0;
    private csrfPending: Promise<Csrf> | null = null;

    constructor(private readonly transport: typeof fetch = (...args) => fetch(...args)) {}

    invalidateCsrf() {
        this.csrfEpoch++;
        this.csrf = null;
        this.csrfPending = null;
    }

    async refreshCsrf(): Promise<void> {
        this.invalidateCsrf();
        await this.antiforgery();
    }

    private antiforgery(): Promise<Csrf> {
        if (this.csrf) return Promise.resolve(this.csrf);
        if (this.csrfPending) return this.csrfPending;
        const epoch = this.csrfEpoch;
        const request = this.request<Csrf>('/api/auth/antiforgery')
            .then((value) => {
                if (!value || typeof value.requestToken !== 'string' || value.headerName !== 'X-CSRF-TOKEN') {
                    throw new Error('The API returned an invalid antiforgery response.');
                }
                if (epoch !== this.csrfEpoch)
                    throw new DOMException('Identity changed during token retrieval.', 'AbortError');
                this.csrf = value;
                return value;
            })
            .finally(() => {
                if (this.csrfPending === request) this.csrfPending = null;
            });
        this.csrfPending = request;
        return request;
    }

    async request<T>(path: string, options: RequestOptions = {}): Promise<T> {
        const parsed = new URL(path, 'http://orderflow.invalid');
        if (
            !path.startsWith('/api/') ||
            !parsed.pathname.startsWith('/api/') ||
            parsed.origin !== 'http://orderflow.invalid' ||
            path.includes('\\') ||
            path.includes('#')
        ) {
            throw new Error('API requests must use a same-origin /api/ path.');
        }
        const method = options.method ?? 'GET';
        const headers = new Headers({ Accept: 'application/json' });
        if (options.body !== undefined) headers.set('Content-Type', 'application/json');
        if (method !== 'GET') {
            const token = await this.antiforgery();
            headers.set(token.headerName, token.requestToken);
        }
        const requestEpoch = this.csrfEpoch;
        const response = await this.transport(path, {
            method,
            headers,
            signal: options.signal,
            credentials: 'same-origin',
            cache: 'no-store',
            redirect: 'error',
            body: options.body === undefined ? undefined : JSON.stringify(options.body),
        });
        if (!response.ok) {
            const failure = await responseProblem(response);
            if (failure.code === 'antiforgery_failed' && requestEpoch === this.csrfEpoch)
                this.invalidateCsrf();
            throw failure; // No automatic mutation replay, including CSRF errors.
        }
        if (response.status === 204) return undefined as T;
        if (!response.headers.get('Content-Type')?.includes('application/json')) {
            throw new ApiError({
                status: response.status,
                title: 'The API returned an unexpected response.',
                code: 'invalid_response',
                traceId: response.headers.get('X-Trace-Id') ?? '',
                errors: {},
            });
        }
        try {
            return (await response.json()) as T;
        } catch (error) {
            if (error instanceof DOMException && error.name === 'AbortError') throw error;
            throw new ApiError({
                status: response.status,
                title: 'The API returned invalid JSON.',
                code: 'invalid_response',
                traceId: response.headers.get('X-Trace-Id') ?? '',
                errors: {},
            });
        }
    }
}
