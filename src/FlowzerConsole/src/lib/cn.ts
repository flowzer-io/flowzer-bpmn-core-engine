import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

/** Kombiniert Klassen und löst Tailwind-Konflikte auf (letzte Angabe gewinnt). */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}

/**
 * Erzeugt eine mit dem Akzent- oder Zustandston eingefärbte Fläche.
 * Entspricht dem `mix()`-Helfer aus dem Design.
 */
export function mix(variable: string, percent: number): string {
  return `color-mix(in oklab, var(${variable}) ${percent}%, transparent)`;
}
