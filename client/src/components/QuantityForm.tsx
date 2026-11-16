import { useState } from 'react';
import type { FormEvent } from 'react';
import { MAX_QUANTITY, parseQuantity } from '../state/cart';

export function QuantityForm({
    id,
    name,
    quantity = 1,
    label,
    onSave,
    disabled = false,
}: {
    id: string;
    name: string;
    quantity?: number;
    label: string;
    onSave: (quantity: number) => void;
    disabled?: boolean;
}) {
    const [draft, setDraft] = useState(String(quantity));
    const [error, setError] = useState('');
    function submit(event: FormEvent) {
        event.preventDefault();
        try {
            onSave(parseQuantity(draft));
            setError('');
        } catch (failure) {
            setError(failure instanceof Error ? failure.message : 'Quantity could not be updated.');
        }
    }
    return (
        <form className="quantity-form" onSubmit={submit} noValidate>
            <div className="field">
                <label htmlFor={id}>
                    Quantity <span className="sr-only">for {name}</span>
                </label>
                <input
                    id={id}
                    type="text"
                    inputMode="numeric"
                    autoComplete="off"
                    maxLength={8}
                    value={draft}
                    onChange={(event) => {
                        setDraft(event.target.value);
                        setError('');
                    }}
                    aria-invalid={!!error}
                    aria-describedby={`${id}-hint${error ? ` ${id}-error` : ''}`}
                    disabled={disabled}
                />
                <span id={`${id}-hint`} className="field-hint">
                    1–{MAX_QUANTITY}, whole units
                </span>
            </div>
            <button className="button secondary" type="submit" disabled={disabled}>
                {label}
            </button>
            {error && (
                <p id={`${id}-error`} className="field-error quantity-error" role="alert">
                    {error}
                </p>
            )}
        </form>
    );
}
