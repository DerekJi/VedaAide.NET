import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface TokenUsageByModel {
  modelName: string;
  operationType: string;
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
}

export interface TokenUsagePeriod {
  totalTokens: number;
  byModel: TokenUsageByModel[];
}

export interface TokenUsageSummary {
  thisMonth: TokenUsagePeriod;
  allTime:   TokenUsagePeriod;
}

@Injectable({ providedIn: 'root' })
export class UsageService {
  private readonly http = inject(HttpClient);

  getSummary(userId?: string): Observable<TokenUsageSummary> {
    const params = userId ? `?userId=${encodeURIComponent(userId)}` : '';
    return this.http.get<TokenUsageSummary>(`/api/usage/summary${params}`);
  }
}
