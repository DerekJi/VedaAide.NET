import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  EvalQuestion, EvaluationReport, IngestRequest, IngestResult,
  PromptTemplate, QueryRequest, QueryResponse,
  RunEvaluationRequest, SaveEvalQuestionRequest, SavePromptRequest,
} from '../shared/models';

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

  // ── Evaluation ─────────────────────────────────────────────────────────────

  listEvalQuestions(): Observable<EvalQuestion[]> {
    return this.http.get<EvalQuestion[]>(`${this.base}/evaluation/questions`);
  }

  saveEvalQuestion(req: SaveEvalQuestionRequest): Observable<EvalQuestion> {
    return this.http.post<EvalQuestion>(`${this.base}/evaluation/questions`, req);
  }

  deleteEvalQuestion(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/evaluation/questions/${id}`);
  }

  runEvaluation(req: RunEvaluationRequest): Observable<EvaluationReport> {
    return this.http.post<EvaluationReport>(`${this.base}/evaluation/run`, req);
  }

  listEvalReports(limit = 20): Observable<EvaluationReport[]> {
    return this.http.get<EvaluationReport[]>(`${this.base}/evaluation/reports?limit=${limit}`);
  }

  getEvalReport(runId: string): Observable<EvaluationReport> {
    return this.http.get<EvaluationReport>(`${this.base}/evaluation/reports/${runId}`);
  }

  deleteEvalReport(runId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/evaluation/reports/${runId}`);
  }
}
