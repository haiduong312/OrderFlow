import { expect, it, vi } from 'vitest';
import type { HubConnection } from '@microsoft/signalr';
import { NotificationRealtime } from '../src/realtime/notifications';
import { SessionController } from '../src/auth/session';
import { ApiClient } from '../src/api/client';
import { createQueryClient } from '../src/api/queries';
import { orderKeys } from '../src/api/orders';
import { remainingReservation } from '../src/components/ReservationClock';
const A = '11111111-1111-4111-8111-111111111111';
const B = '22222222-2222-4222-8222-222222222222';
const id = '33333333-3333-4333-8333-333333333333';
it('treats countdown as elapsed server observation time and never mutates authoritative order state', () => {
    expect(remainingReservation('2035-01-01T00:15:00Z', '2035-01-01T00:00:00Z', 30_000)).toBe(870_000);
    expect(remainingReservation('2035-01-01T00:15:00Z', '2035-01-01T00:15:00Z', 0)).toBe(0);
    expect(remainingReservation('2035-01-01T00:15:00Z', '2035-01-01T00:00:00Z', 999_999)).toBe(0);
});
it('hints only invalidate relevant owner keys, duplicate IDs are harmless, reconnect catches up and logout stops immediately', async () => {
    const queries = createQueryClient();
    const controller = new SessionController(
        queries,
        new ApiClient(
            async () =>
                new Response(JSON.stringify({ id: A, email: 'a@example.invalid', roles: ['Customer'] }), {
                    headers: { 'Content-Type': 'application/json' },
                }),
        ),
    );
    await controller.refresh();
    const handlers = new Map<string, (...args: unknown[]) => void>();
    let reconnected!: () => void;
    let stopped = false;
    const connection = {
        on: (name: string, handler: (...args: unknown[]) => void) => handlers.set(name, handler),
        onreconnecting: () => {},
        onclose: () => {},
        onreconnected: (handler: () => void) => {
            reconnected = handler;
        },
        start: async () => {},
        stop: async () => {
            stopped = true;
        },
    } as unknown as HubConnection;
    const spy = vi.spyOn(queries, 'invalidateQueries');
    const live = new NotificationRealtime(controller, true, () => connection);
    live.start();
    handlers.get('ConnectionReady')!();
    expect(live.getSnapshot()).toBe('connected');
    spy.mockClear();
    const hint = { eventId: id, notificationId: B, orderId: id, status: 'Paid' };
    handlers.get('NotificationAvailable')!(hint);
    expect(spy).toHaveBeenCalledTimes(5);
    expect(
        spy.mock.calls.every(([filter]) => filter?.queryKey?.[0] === 'private' && filter.queryKey[1] === A),
    ).toBe(true);
    handlers.get('NotificationAvailable')!(hint);
    expect(spy).toHaveBeenCalledTimes(5);
    handlers.get('NotificationAvailable')!({ ...hint, eventId: 'invalid' });
    expect(spy).toHaveBeenCalledTimes(5);
    reconnected();
    expect(spy).toHaveBeenCalledTimes(6);
    const refresh = controller.refresh();
    expect(stopped).toBe(true);
    await refresh;
    expect(queries.getQueryData(orderKeys.detail(A, id))).toBeUndefined();
    live.dispose();
    queries.clear();
});
it('all payment/notification pages and owner scopes participate in keys', () => {
    expect(orderKeys.payments(A, id, 1, 20)).not.toEqual(orderKeys.payments(B, id, 1, 20));
    expect(orderKeys.payments(A, id, 1, 20)).not.toEqual(orderKeys.payments(A, id, 2, 20));
    expect(orderKeys.payments(A, id, 1, 20)).not.toEqual(orderKeys.payments(A, id, 1, 100));
    expect(orderKeys.notifications(A, 1, 20, false)).not.toEqual(orderKeys.notifications(A, 1, 20, true));
});
