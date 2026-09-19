import type { DownloadedFile } from '@/lib/api/client';

/**
 * Legt eine heruntergeladene Datei im Downloadordner ab.
 *
 * Der Umweg über eine Objekt-URL und einen kurzlebigen Link ist der einzige Weg, der ohne
 * zusätzliche Berechtigung in allen Browsern funktioniert. Die URL wird sofort wieder
 * freigegeben: Sie hält sonst den gesamten Dateiinhalt im Speicher, solange die Seite offen ist.
 */
export function saveFile(file: DownloadedFile): void {
  const url = URL.createObjectURL(file.blob);
  try {
    const link = document.createElement('a');
    link.href = url;
    link.download = file.fileName;
    link.rel = 'noopener';
    document.body.append(link);
    link.click();
    link.remove();
  } finally {
    // Erst im nächsten Zyklus: Ein sofortiges Widerrufen käme dem Download zuvor.
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }
}
