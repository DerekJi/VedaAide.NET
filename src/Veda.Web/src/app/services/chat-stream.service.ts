import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { RagStreamChunk } from '../shared/models';

/**
 * Connects to GET /api/querystream via EventSource (Server-Sent Events).
 * Returns an Observable that emits RagStreamChunk objects until the stream ends.
 */
@Injectable({ providedIn: 'root' })
export class ChatStreamService {
  stream(question: string, options: { topK?: number; minSimilarity?: number; dateFrom?: string; dateTo?: string } = {}): Observable<RagStreamChunk> {
    return new Observable(observer => {
      const params = new URLSearchParams({ question });
      if (options.topK) params.set('topK', String(options.topK));
      if (options.minSimilarity) params.set('minSimilarity', String(options.minSimilarity));
      if (options.dateFrom) params.set('dateFrom', options.dateFrom);
      if (options.dateTo) params.set('dateTo', options.dateTo);

      const url = `/api/querystream?${params}`;
      const es = new EventSource(url);

      es.onmessage = event => {
        try {
          const chunk: RagStreamChunk = JSON.parse(event.data);
          observer.next(chunk);
          if (chunk.type === 'done') {
            observer.complete();
            es.close();
          }
        } catch {
          observer.error(new Error('Failed to parse stream chunk'));
        }
      };

      es.onerror = () => {
        observer.error(new Error('SSE connection error'));
        es.close();
      };

      return () => es.close();
    });
  }
}
