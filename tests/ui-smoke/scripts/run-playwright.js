#!/usr/bin/env node

const { spawn } = require('child_process');
const { cleanupProcesses, listCandidateProcesses, DEFAULT_STALE_THRESHOLD_SECONDS } = require('./playwright-process-guard');

const args = process.argv.slice(2);
const knownPids = new Set(listCandidateProcesses().map(processInfo => processInfo.pid));
const staleThresholdSeconds = Number.parseInt(
  process.env.PLAYWRIGHT_PROCESS_STALE_THRESHOLD_SECONDS || '',
  10
) || DEFAULT_STALE_THRESHOLD_SECONDS;
const shouldSkipGuard = process.env.PLAYWRIGHT_SKIP_PROCESS_GUARD === '1';

let cleanupStarted = false;

async function performCleanup(mode)
{
  if (cleanupStarted)
  {
    return;
  }

  cleanupStarted = true;

  if (shouldSkipGuard)
  {
    return;
  }

  try
  {
    await cleanupProcesses({ mode, knownPids, staleThresholdSeconds });
  }
  catch (error)
  {
    console.warn(`[ui-smoke] Browser cleanup failed during "${mode}": ${error instanceof Error ? error.message : error}`);
  }
}

async function main()
{
  if (!shouldSkipGuard)
  {
    await cleanupProcesses({ mode: 'stale', staleThresholdSeconds });
  }

  const child = spawn(
    process.platform === 'win32' ? 'npx.cmd' : 'npx',
    ['playwright', 'test', ...args],
    {
      cwd: __dirname + '/..',
      stdio: 'inherit',
      env: process.env
    }
  );

  const terminateChild = signal =>
  {
    if (!child.killed)
    {
      child.kill(signal);
    }
  };

  process.on('SIGINT', () =>
  {
    terminateChild('SIGINT');
  });

  process.on('SIGTERM', () =>
  {
    terminateChild('SIGTERM');
  });

  child.on('exit', async (code, signal) =>
  {
    await performCleanup('new');

    if (signal)
    {
      process.kill(process.pid, signal);
      return;
    }

    process.exit(code ?? 1);
  });

  child.on('error', async error =>
  {
    console.error(`[ui-smoke] Failed to start Playwright: ${error instanceof Error ? error.message : error}`);
    await performCleanup('new');
    process.exit(1);
  });
}

main().catch(async error =>
{
  console.error(error);
  await performCleanup('all');
  process.exit(1);
});
