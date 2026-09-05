import { Outlet } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import { seesFullConsole, useSession } from '@/stores/session';

import { AccessDeniedNotice } from './AccessDeniedNotice';
import { CommandPalette } from './CommandPalette';
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
      <Sidebar onOpenUserMenu={() => setUserMenuOpen(true)} />

      <div className="flex h-screen min-w-0 flex-1 flex-col overflow-hidden">
        <Topbar onOpenPalette={() => setPaletteOpen(true)} onOpenUserMenu={() => setUserMenuOpen(true)} />
        <main className="flex-1 overflow-auto">
          <AccessDeniedNotice />
          <Outlet />
        </main>
      </div>

      <CommandPalette open={paletteOpen} onOpenChange={setPaletteOpen} />
      <UserMenu open={userMenuOpen} onOpenChange={setUserMenuOpen} />
    </div>
  );
}
