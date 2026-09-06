import { Outlet } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import { seesFullConsole, useSession } from '@/stores/session';
import { useCompactLayout } from '@/lib/useCompactLayout';

import { AccessDeniedNotice } from './AccessDeniedNotice';
import { CommandPalette } from './CommandPalette';
import { MobileTabBar } from './MobileTabBar';
import { SignInGate } from './SignInGate';
import { Sidebar } from './Sidebar';
import { Topbar } from './Topbar';
import { UserMenu } from './UserMenu';
import { WorkerShell } from './WorkerShell';

/**
 * Rahmen der Anwendung.
 *
 * Wer veröffentlichen oder den Betrieb einsehen darf, sieht die vollständige
 * Konsole. Alle anderen sehen die reduzierte Aufgabenoberfläche — die zwei
 * Personas aus dem Design, jetzt an den Rollen aus dem Token festgemacht statt
 * an einem Schalter im Browser.
 */
export function AppShell() {
  const status = useSession((state) => state.status);
  const user = useSession((state) => state.user);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const compact = useCompactLayout();

  // Wer keinen Zugang hat, bekommt die reduzierte Ansicht: Die vollständige Konsole
  // zeigte dann nur eine Reihe abgelehnter Aufrufe.
  const fullConsole = seesFullConsole(user);

  useEffect(() => {
    if (!fullConsole) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        setPaletteOpen((open) => !open);
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [fullConsole]);

  if (status !== 'signed-in' || !user) {
    return <SignInGate status={status} />;
  }

  if (!fullConsole) {
    // Bewusst ohne Befehlspalette: Sie führt durch den gesamten Katalog und die
    // Instanzen und würde die reduzierte Ansicht wieder öffnen. Der Hinweis auf einen
    // fehlenden Zugang gehört aber auch hierher: Ohne die Zugangsrolle sähe man sonst
    // nur eine leere Aufgabenliste mit einer technischen Fehlermeldung.
    return (
      <WorkerShell onOpenUserMenu={() => setUserMenuOpen(true)}>
        <AccessDeniedNotice />
        <UserMenu open={userMenuOpen} onOpenChange={setUserMenuOpen} />
      </WorkerShell>
    );
  }

  return (
    <div className="flex h-screen">
      {/*
        * Auf dem Telefon traegt die untere Reiterleiste die Navigation; die Seitenleiste
        * haette dort kaum Platz und verdeckte den Inhalt.
        */}
      <div className="flex max-md:hidden">
        <Sidebar onOpenUserMenu={() => setUserMenuOpen(true)} />
      </div>

      <div className="flex h-screen min-w-0 flex-1 flex-col overflow-hidden">
        <Topbar onOpenPalette={() => setPaletteOpen(true)} onOpenUserMenu={() => setUserMenuOpen(true)} />
        {/*
          * Flex-Spalte mit `min-h-0`: Seiten, die den Rest der Hoehe fuellen sollen (Modeler,
          * Instanzansicht), koennen sich hier mit `flex-1` einhaengen. Mit einem blossen
          * `flex-1 overflow-auto` und `h-full` in der Seite loeste Safari die Prozenthoehe
          * gegen einen Flex-Container nicht auf — die Zeichenflaeche wurde 0 Pixel hoch und
          * der Modeler schien gar nicht erst zu starten.
          */}
        {/* Unten Platz fuer die Reiterleiste, damit sie nichts verdeckt. */}
        <main className="flex min-h-0 flex-1 flex-col overflow-auto pb-[calc(58px+env(safe-area-inset-bottom))] md:pb-0">
          <AccessDeniedNotice />
          <Outlet />
        </main>
      </div>

      {/* Nur einhaengen, wenn sie auch gebraucht wird: Per CSS versteckt fragte sie
          auch am Schreibtisch laufend die offenen Aufgaben ab. */}
      {compact && <MobileTabBar />}
      <CommandPalette open={paletteOpen} onOpenChange={setPaletteOpen} />
      <UserMenu open={userMenuOpen} onOpenChange={setUserMenuOpen} />
    </div>
  );
}
