import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface Preferences {
    density: 'comfortable' | 'compact';
    mobileOpen: boolean;
    setDensity: (value: 'comfortable' | 'compact') => void;
    setMobileOpen: (value: boolean) => void;
}
export const usePreferences = create<Preferences>()(
    persist(
        (set) => ({
            density: 'comfortable',
            mobileOpen: false,
            setDensity: (density) => set({ density }),
            setMobileOpen: (mobileOpen) => set({ mobileOpen }),
        }),
        {
            name: 'orderflow-ui-v1',
            version: 1,
            partialize: (state) => ({ density: state.density }),
            merge: (stored, current) => ({
                ...current,
                density:
                    (stored as Partial<Preferences> | null)?.density === 'compact'
                        ? 'compact'
                        : 'comfortable',
            }),
        },
    ),
);
