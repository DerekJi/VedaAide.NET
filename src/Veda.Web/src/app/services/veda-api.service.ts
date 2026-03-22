import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { IngestRequest, IngestResult, PromptTemplate, QueryRequest, QueryResponse, SavePromptRequest } from '../shared/models';

@Injectable({ providedIn: 'root' })
export class VedaApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  ingestDocument(req: IngestRequest): Observable<IngestResult> {
    return this.http.post<IngestResult>(`${this.base}/documents`, req);
  }

  query(req: QueryRequest): Observable<QueryResponse> {
    return this.http.post<QueryResponse>(`${this.base}/query`, req);
  }

  listPrompts(): Observable<PromptTemplate[]> {
    return this.http.get<PromptTemplate[]>(`${this.base}/prompts`);
  }

  savePrompt(req: SavePromptRequest): Observable<void> {
    return this.http.post<void>(`${this.base}/prompts`, req);
  }

  deletePrompt(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/prompts/${id}`);
  }
}
