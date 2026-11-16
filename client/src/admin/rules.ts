import type { OrderStatus } from '../api/types';
export function allowedFulfillment(status: OrderStatus): OrderStatus | null {
    return status === 'Confirmed'
        ? 'Processing'
        : status === 'Processing'
          ? 'Shipped'
          : status === 'Shipped'
            ? 'Completed'
            : null;
}
export function integerInput(value: string, minimum: number, maximum = 2_147_483_647) {
    if (
        !/^-?(0|[1-9][0-9]*)$/.test(value) ||
        !Number.isSafeInteger(Number(value)) ||
        Number(value) < minimum ||
        Number(value) > maximum
    )
        throw new Error(`Enter a whole number from ${minimum} to ${maximum}.`);
    return Number(value);
}
export function reportMoney(value: string): string {
    if (!/^-?(0|[1-9][0-9]{0,28})\.[0-9]{2}$/.test(value)) throw new Error('Invalid report USD amount.');
    const negative = value.startsWith('-');
    const [whole, fraction] = (negative ? value.slice(1) : value).split('.');
    return `${negative ? '-' : ''}$${whole!.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}.${fraction}`;
}
export function validRange(from: string, to: string) {
    const date = (value: string) =>
        /^\d{4}-\d{2}-\d{2}$/.test(value) &&
        !isNaN(Date.parse(value)) &&
        new Date(value).toISOString().slice(0, 10) === value;
    return date(from) && date(to) && from < to && (Date.parse(to) - Date.parse(from)) / 86_400_000 <= 366;
}
export function defaultRange(now = new Date()) {
    const end = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1));
    return {
        from: new Date(end.getTime() - 30 * 86_400_000).toISOString().slice(0, 10),
        to: end.toISOString().slice(0, 10),
    };
}
