import { useEffect, useState } from 'react';

export function remainingReservation(expiresAt: string, observedAt: string, elapsedMs: number) {
    return Math.max(0, Date.parse(expiresAt) - Date.parse(observedAt) - Math.max(0, elapsedMs));
}
export function ReservationClock({ expiresAt, observedAt }: { expiresAt: string; observedAt: string }) {
    const [sample, setSample] = useState(() => ({ observedAt, at: performance.now(), elapsed: 0 }));
    useEffect(() => {
        const at = performance.now();
        setSample({ observedAt, at, elapsed: 0 });
        const timer = window.setInterval(
            () => setSample({ observedAt, at, elapsed: performance.now() - at }),
            1000,
        );
        return () => window.clearInterval(timer);
    }, [observedAt]);
    const seconds = Math.ceil(
        remainingReservation(expiresAt, observedAt, sample.observedAt === observedAt ? sample.elapsed : 0) /
            1000,
    );
    return (
        <div className="reservation-clock">
            <strong>
                Reservation countdown: {Math.floor(seconds / 60)}:{String(seconds % 60).padStart(2, '0')}
            </strong>
            <p>
                Expires {new Date(expiresAt).toLocaleString()} (your device time zone). Countdown is
                informational and uses server observation time.
            </p>
            {seconds === 0 && (
                <p>Deadline reached on this estimate. Refresh for the server’s current decision.</p>
            )}
        </div>
    );
}
