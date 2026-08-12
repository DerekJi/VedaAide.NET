import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FeedbackRequest } from '../shared/models';

/**
 * Reports user behavior feedback to POST /api/feedback.
 * Sent silently (fire-and-forget); failures only log a warning and never affect the UI.
 */
@Injectable({ providedIn: 'root' })
export class FeedbackService {
  private readonly http = inject(HttpClient);

  record(req: FeedbackRequest): void {
    this.http.post('/api/feedback', req).subscribe({
      error: (e: unknown) => console.warn('[FeedbackService] report failed', e)
    });
  }
}
