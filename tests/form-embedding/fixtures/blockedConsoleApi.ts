/** Der Sandbox-Probe enthält ausdrücklich keine Console-/BFF-Transporte. */
const denied = () => { throw new Error('Console API is unavailable in an opaque frame.'); };
export const requestStatusResult = denied;
export const useDirectorySubjectResolutions = denied;
export const useDirectorySubjectSearch = denied;
export const useFolderDirectorySubjectResolutions = denied;
export const useFolderDirectorySubjectSearch = denied;
export const useFormDirectorySubjectResolutions = denied;
export const useFormDirectorySubjectSearch = denied;

export class ApiError extends Error {}
