import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';

/**
 * 封装管理员操作：删除单个文档、清空全部数据、清空语义缓存。
 * 对应 /api/admin/* 端点，需 X-Api-Key (Admin Key) 请求头。
 * 实际 KEY 由 API Proxy / nginx 层注入，前端无感知。
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
