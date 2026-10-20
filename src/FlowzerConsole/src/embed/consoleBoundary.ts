/** Dieses Bundle enthält ausdrücklich keinen Console-/BFF-Transport oder Identitätszustand. */
const denied = () => { throw new Error('Die Console-API ist in der Einbettung nicht verfügbar.'); };
export const requestStatusResult = denied;
export const useDirectorySubjectResolutions = denied;
export const useDirectorySubjectSearch = denied;
export const useFolderDirectorySubjectResolutions = denied;
export const useFolderDirectorySubjectSearch = denied;
export const useFormDirectorySubjectResolutions = denied;
export const useFormDirectorySubjectSearch = denied;
export class ApiError extends Error { readonly body: unknown = null; }
