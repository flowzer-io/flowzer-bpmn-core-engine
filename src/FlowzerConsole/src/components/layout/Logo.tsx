/**
 * Die Wortmarke aus dem Design: Startknoten, Fluss, Endknoten — als reine
 * CSS-Formen, damit sie die Akzentfarbe des Themes übernimmt.
 */
export function LogoMark() {
  return (
    <div className="flex shrink-0 items-center gap-[3px]" aria-hidden>
      <span className="border-accent block h-[9px] w-[9px] rounded-full border-2" />
      <span className="from-accent to-accent-2 block h-0.5 w-[13px] rounded-sm bg-gradient-to-r" />
      <span
        className="bg-accent block h-2 w-2 rounded-full"
        style={{ boxShadow: '0 0 8px var(--accent-2)' }}
      />
    </div>
  );
}

export function LogoWordmark({ className }: { className?: string }) {
  return (
    <span className={`font-display text-[21px] font-bold tracking-[-0.02em] ${className ?? ''}`}>
      flowzer
    </span>
  );
}
