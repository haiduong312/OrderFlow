import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

const target = process.env.ORDERFLOW_API_TARGET ?? 'http://127.0.0.1:5080';
const api = new URL(target);
if (!['http:', 'https:'].includes(api.protocol) || api.username || api.password) {
    throw new Error('ORDERFLOW_API_TARGET must be an HTTP(S) URL without embedded credentials.');
}

export default defineConfig({
    plugins: [react()],
    server: {
        host: '127.0.0.1',
        proxy: { '/api': { target, changeOrigin: false, ws: true } },
    },
    test: { include: ['tests/**/*.test.ts'], environment: 'node', clearMocks: true },
});
