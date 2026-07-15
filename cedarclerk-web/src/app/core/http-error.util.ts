import { HttpErrorResponse } from '@angular/common/http';

export function httpErrorMessage(e: unknown, fallback: string): string {
    return e instanceof HttpErrorResponse && e.error?.error ? e.error.error : fallback;
}
