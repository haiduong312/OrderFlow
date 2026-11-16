import { fromCents, toCents } from '../api/money';
import type { Product } from '../api/types';
import type { CartItem } from '../state/cart';

export function estimatedLine(product: Product, quantity: number): string {
    return fromCents(toCents(product.price) * BigInt(quantity));
}
export function estimatedTotal(items: readonly CartItem[], products: ReadonlyMap<string, Product>): string {
    let cents = 0n;
    for (const item of items) {
        const product = products.get(item.productId);
        if (!product) throw new Error('An item has no confirmed catalog price.');
        cents += toCents(product.price) * BigInt(item.quantity);
    }
    return fromCents(cents); // Explicit numeric(18,2) bound; never round or silently overflow.
}
export function itemIssue(product: Product, quantity: number): string | null {
    if (!product.isActive || !product.category.isActive) return 'This product is no longer available.';
    if (product.available === 0) return 'Currently out of stock. Remove it or check again later.';
    if (quantity > product.available)
        return `Only ${product.available} currently available. Reduce the quantity or check again later.`;
    return null;
}
