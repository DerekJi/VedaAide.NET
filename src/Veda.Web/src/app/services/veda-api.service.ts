import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { IngestRequest, IngestResult, QueryRequest, QueryResponse } from '../shared/models';

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
}
