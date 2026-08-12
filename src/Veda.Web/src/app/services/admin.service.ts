import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';

/**
 * Wraps admin operations: delete a single document, clear all data, and clear the semantic cache.
 * Corresponds to the /api/admin/* endpoints and requires the X-Api-Key (Admin Key) request header.
 * The actual KEY is injected by the API Proxy / nginx layer, invisible to the frontend.
 */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/admin';

  deleteDocument(documentId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/documents/${encodeURIComponent(documentId)}`);
  }

  deleteAllData(): Observable<void> {
    const headers = new HttpHeaders({ 'X-Confirm': 'yes' });
    return this.http.delete<void>(`${this.base}/data`, { headers });
  }

  clearCache(): Observable<void> {
    return this.http.delete<void>(`${this.base}/cache`);
  }
}
