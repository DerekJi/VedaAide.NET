import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FeedbackRequest } from '../shared/models';

/**
 * 上报用户行为反馈到 POST /api/feedback。
 * 静默发送（fire-and-forget），失败仅记录警告，不影响 UI。
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
