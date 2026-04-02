import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { MsalService } from '@azure/msal-angular';
import { environment } from '../../environments/environment';
import { RagStreamChunk } from '../shared/models';

/**
 * Connects to GET /api/querystream via fetch + ReadableStream (Server-Sent Events).
 *
 * EventSource is intentionally NOT used here because it does not support custom
 * request headers. Sending the Authorization: Bearer token requires fetch(), which
 * the MSAL interceptor cannot cover for non-HttpClient requests.
 */
@Injectable({ providedIn: 'root' })
export class ChatStreamService {
  private readonly msal = inject(MsalService);

  stream(question: string, options: { topK?: number; minSimilarity?: number; dateFrom?: string; dateTo?: string } = {}): Observable<RagStreamChunk> {
    return new Observable(observer => {
      const params = new URLSearchParams({ question });
      if (options.topK)          params.set('topK',          String(options.topK));
      if (options.minSimilarity) params.set('minSimilarity', String(options.minSimilarity));
      if (options.dateFrom)      params.set('dateFrom',      options.dateFrom);
      if (options.dateTo)        params.set('dateTo',        options.dateTo);

      const url = `/api/querystream?${params}`;
      const abortController = new AbortController();

      this.fetchStream(url, 'GET', undefined, abortController, observer);

      return () => abortController.abort();
    });
  }

  /**
   * 携带临时附件文本的流式问答（Context Augmentation）。
   * 通过 POST /api/querystream 发送，包含 extraContext 字段。
   */
  streamWithContext(
    question: string,
    extraContext: string,
    options: { topK?: number; minSimilarity?: number; dateFrom?: string; dateTo?: string } = {}
  ): Observable<RagStreamChunk> {
    return new Observable(observer => {
      const abortController = new AbortController();

      const body = JSON.stringify({
        question,
        extraContext,
        topK:          options.topK          ?? 5,
        minSimilarity: options.minSimilarity  ?? 0,
        ...(options.dateFrom ? { dateFrom: options.dateFrom } : {}),
        ...(options.dateTo   ? { dateTo:   options.dateTo   } : {}),
      });

      this.fetchStream('/api/querystream', 'POST', body, abortController, observer);

      return () => abortController.abort();
    });
  }

  private fetchStream(
    url: string,
    method: 'GET' | 'POST',
    body: string | undefined,
    abortController: AbortController,
    observer: { next: (v: RagStreamChunk) => void; error: (e: unknown) => void; complete: () => void }
  ): void {
    const account = this.msal.instance.getActiveAccount()
      ?? this.msal.instance.getAllAccounts()[0]
      ?? null;

    const tokenPromise = account
      ? this.msal.instance.acquireTokenSilent({ scopes: [environment.apiScope], account })
          .then(r => r.accessToken)
          .catch(() => null)
      : Promise.resolve(null);

    tokenPromise.then(token => {
      const headers: Record<string, string> = { Accept: 'text/event-stream' };
      if (token) headers['Authorization'] = `Bearer ${token}`;
      if (method === 'POST') headers['Content-Type'] = 'application/json';

      return fetch(url, { method, headers, body, signal: abortController.signal });
    }).then(response => {
      if (!response.ok) {
        observer.error(new Error(`SSE request failed: ${response.status} ${response.statusText}`));
        return;
      }

      const reader = response.body!.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      const pump = (): Promise<void> =>
        reader.read().then(({ done, value }) => {
          if (done) { observer.complete(); return; }

          buffer += decoder.decode(value, { stream: true });
          const lines = buffer.split('\n');
          buffer = lines.pop() ?? '';

          for (const line of lines) {
            if (!line.startsWith('data:')) continue;
            const data = line.slice(5).trim();
            if (!data) continue;
            try {
              const chunk: RagStreamChunk = JSON.parse(data);
              observer.next(chunk);
              if (chunk.type === 'done') { observer.complete(); return; }
            } catch {
              observer.error(new Error('Failed to parse stream chunk'));
              return;
            }
          }
          return pump();
        });

      pump().catch(err => {
        if (err?.name !== 'AbortError') observer.error(new Error('SSE connection error'));
      });
    }).catch(err => {
      if (err?.name !== 'AbortError') observer.error(new Error('SSE connection error'));
    });
  }
}

