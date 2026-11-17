import { expect, test as base } from '@playwright/test';

// CI publishes only these explicitly masked images. Network traces/storage/error-context stay private.
const test = base.extend<{ failureArtifacts: void }>({
    failureArtifacts: [
        async ({ page }, use, info) => {
            // Depending on page keeps it alive until this automatic fixture finishes teardown.
            await use();
            if (process.env.ORDERFLOW_SAFE_FAILURE_ARTIFACTS !== '1' || info.status === info.expectedStatus)
                return;
            for (const [index, current] of page.context().pages().slice(0, 3).entries()) {
                try {
                    await current.screenshot({
                        path: info.outputPath(`safe-failure-${index}.png`),
                        mask: [current.locator('input, textarea, [data-sensitive]')],
                        maskColor: '#000000',
                        animations: 'disabled',
                        timeout: 5_000,
                    });
                } catch {
                    // Preserve the original failure even if the browser already closed.
                    console.log('Masked failure screenshot unavailable.');
                }
            }
        },
        { auto: true },
    ],
});

export { expect, test };
