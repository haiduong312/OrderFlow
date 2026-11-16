const maximum = 999999999999999999n;
export function toCents(value: string): bigint {
    if (!/^(0|[1-9][0-9]{0,15})\.[0-9]{2}$/.test(value)) throw new Error('Invalid USD money string.');
    return BigInt(value.replace('.', ''));
}
export function fromCents(value: bigint): string {
    if (value < 0n || value > maximum) throw new Error('USD amount exceeds the supported range.');
    return `${value / 100n}.${String(value % 100n).padStart(2, '0')}`;
}
export function formatUsd(value: string): string {
    const exact = fromCents(toCents(value));
    const [whole, fraction] = exact.split('.');
    return `$${whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}.${fraction}`;
}
