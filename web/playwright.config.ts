import { defineConfig } from "@playwright/test";
import path from "node:path";

const releaseBaseURL = process.env.LEX_BROWSER_BASE_URL?.replace(/\/$/, "");
const baseURL = releaseBaseURL ?? "http://127.0.0.1:5138";

export default defineConfig({
  testDir: "browser",
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [["list"], ["html", { open: "never", outputFolder: "playwright-report" }]],
  timeout: 30_000,
  expect: { timeout: 5_000 },
  use: {
    baseURL,
    browserName: "chromium",
    headless: true,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  webServer: releaseBaseURL ? undefined : {
    command: "dotnet run --project src/Lex.Web/Lex.Web.csproj --configuration Release --no-build --no-launch-profile -- --urls http://127.0.0.1:5138",
    cwd: "..",
    env: {
      LEX_INDEX_DIR: path.resolve("browser/empty-indexes"),
      LEX_PUBLIC_BASE_URL: baseURL,
    },
    url: `${baseURL}/healthz`,
    timeout: 120_000,
    reuseExistingServer: !process.env.CI,
    stdout: "ignore",
    stderr: "pipe",
  },
});
