const { spawnSync } = require('child_process');

/**
 * Prozesswächter für lokale/CI-Playwright-Läufe.
 *
 * Ziel:
 * - offensichtlich liegengebliebene Browser-Prozesse aus älteren Läufen vor dem Start erkennen
 * - nach dem Lauf neu entstandene Playwright-Browser zuverlässig wegräumen
 *
 * Die Filter sind bewusst auf bekannte Playwright-/ms-playwright-Kommandos begrenzt, damit
 * normale Chrome-/Chromium-Instanzen der Benutzerumgebung nicht berührt werden.
 */

const DEFAULT_STALE_THRESHOLD_SECONDS = 10 * 60;
const MATCHERS = [
  'playwright_chromiumdev_profile-',
  'ms-playwright/chromium_headless_shell',
  'ms-playwright/chromium-',
  'chrome-headless-shell',
  'chrome-linux/chrome'
];

function listCandidateProcesses()
{
  const result = spawnSync('ps', ['-eo', 'pid=,etime=,command='], {
    encoding: 'utf8'
  });

  if (result.status !== 0)
  {
    throw new Error(result.stderr || 'Unable to inspect running processes.');
  }

  return result.stdout
    .split('\n')
    .map(line => line.trim())
    .filter(Boolean)
    .map(parseProcessLine)
    .filter(Boolean)
    .filter(processInfo => matchesPlaywrightProcess(processInfo.command));
}

function parseProcessLine(line)
{
  const match = line.match(/^(\d+)\s+([0-9:-]+)\s+(.+)$/);
  if (!match)
  {
    return null;
  }

  return {
    pid: Number(match[1]),
    elapsed: match[2],
    elapsedSeconds: parseElapsedSeconds(match[2]),
    command: match[3]
  };
}

function parseElapsedSeconds(value)
{
  const dayAndTime = value.split('-');
  const dayPart = dayAndTime.length === 2 ? Number(dayAndTime[0]) : 0;
  const timePart = dayAndTime.length === 2 ? dayAndTime[1] : dayAndTime[0];
  const timeSegments = timePart.split(':').map(segment => Number(segment));

  if (timeSegments.some(Number.isNaN) || Number.isNaN(dayPart))
  {
    return 0;
  }

  if (timeSegments.length === 3)
  {
    return dayPart * 86_400 + timeSegments[0] * 3_600 + timeSegments[1] * 60 + timeSegments[2];
  }

  if (timeSegments.length === 2)
  {
    return dayPart * 86_400 + timeSegments[0] * 60 + timeSegments[1];
  }

  if (timeSegments.length === 1)
  {
    return dayPart * 86_400 + timeSegments[0];
  }

  return 0;
}

function matchesPlaywrightProcess(command)
{
  return MATCHERS.some(matcher => command.includes(matcher));
}

function killProcesses(processes, signal)
{
  for (const processInfo of processes)
  {
    try
    {
      process.kill(processInfo.pid, signal);
    }
    catch (error)
    {
      if (error && error.code !== 'ESRCH')
      {
        throw error;
      }
    }
  }
}

function sleep(milliseconds)
{
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

async function cleanupProcesses({
  mode,
  knownPids = new Set(),
  staleThresholdSeconds = DEFAULT_STALE_THRESHOLD_SECONDS,
  verbose = true
} = {})
{
  const candidates = listCandidateProcesses();
  const selected = candidates.filter(processInfo =>
  {
    if (mode === 'stale')
    {
      return processInfo.elapsedSeconds >= staleThresholdSeconds;
    }

    if (mode === 'new')
    {
      return !knownPids.has(processInfo.pid);
    }

    return true;
  });

  if (selected.length === 0)
  {
    if (verbose)
    {
      console.log(`[ui-smoke] No Playwright browser processes matched cleanup mode "${mode}".`);
    }

    return [];
  }

  if (verbose)
  {
    console.log(
      `[ui-smoke] Cleaning up ${selected.length} Playwright browser process(es) for mode "${mode}":`,
      selected.map(processInfo => `${processInfo.pid}:${processInfo.elapsed}`).join(', ')
    );
  }

  killProcesses(selected, 'SIGTERM');
  await sleep(1_500);

  const stillRunning = listCandidateProcesses()
    .filter(processInfo => selected.some(candidate => candidate.pid === processInfo.pid));

  if (stillRunning.length > 0)
  {
    if (verbose)
    {
      console.warn(
        `[ui-smoke] Escalating cleanup for ${stillRunning.length} stubborn Playwright browser process(es):`,
        stillRunning.map(processInfo => processInfo.pid).join(', ')
      );
    }

    killProcesses(stillRunning, 'SIGKILL');
  }

  return selected;
}

module.exports = {
  cleanupProcesses,
  listCandidateProcesses,
  DEFAULT_STALE_THRESHOLD_SECONDS
};
