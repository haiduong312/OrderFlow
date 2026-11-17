import { defineConfig, devices } from '@playwright/test';

const port = Number(process.env.ORDERFLOW_BROWSER_PORT ?? '4173');
export default defineConfig({
    testDir: './e2e',
    fullyParallel: false,
    workers: 1,
    retries: 0,
    timeout: 30_000,
    expect: { timeout: 10_000 },
    outputDir: process.env.ORDERFLOW_BROWSER_OUTPUT ?? './test-results',
    reporter: [['line']],
    use: {
        baseURL: `http://127.0.0.1:${port}`,
        viewport: { width: 1440, height: 1000 },
        trace: 'off',
        video: 'off',
        screenshot: process.env.ORDERFLOW_SAFE_FAILURE_ARTIFACTS === '1' ? 'off' : 'only-on-failure',
        reducedMotion: 'reduce',
    },
    projects: [
        { name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 1000 } } },
    ],
    webServer: {
        command: `npm run dev -- --host 127.0.0.1 --port ${port} --strictPort`,
        url: `http://127.0.0.1:${port}`,
        reuseExistingServer: false,
        timeout: 30_000,
    },
});
