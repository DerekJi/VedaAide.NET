import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  ChunkPreview, ContextExtractResponse, DemoDocument, DocumentSummary,
  EvalQuestion, EvaluationReport, IngestRequest, IngestResult,
  PromptTemplate, QueryRequest, QueryResponse,
  RunEvaluationRequest, SaveEvalQuestionRequest, SavePromptRequest,
  SessionResponse, MessageResponse, AppendMessageRequest,
} from '../shared/models';

@Injectable({ providedIn: 'root' })
export class VedaApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  ingestDocument(req: IngestRequest): Observable<IngestResult> {
    return this.http.post<IngestResult>(`${this.base}/documents`, req);
  }

  uploadFile(file: File, documentName?: string, documentType?: string): Observable<IngestResult> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    const qs = new URLSearchParams();
    if (documentName) qs.set('documentName', documentName);
    if (documentType) qs.set('documentType', documentType);
    const params = qs.toString() ? `?${qs.toString()}` : '';
    return this.http.post<IngestResult>(`${this.base}/documents/upload${params}`, formData);
  }

  /**
   * 上传文件，仅提取文本，不写向量数据库（Ephemeral RAG / Context Augmentation）。
   * 对应后端 POST /api/context/extract。
   */
  extractContextFile(file: File): Observable<ContextExtractResponse> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    return this.http.post<ContextExtractResponse>(`${this.base}/context/extract`, formData);
  }

  query(req: QueryRequest): Observable<QueryResponse> {
    return this.http.post<QueryResponse>(`${this.base}/query`, req);
  }

  // ── Documents ──────────────────────────────────────────────────────────────

  listDocuments(): Observable<DocumentSummary[]> {
    return this.http.get<DocumentSummary[]>(`${this.base}/documents`);
  }

  getDocumentChunks(documentName: string): Observable<ChunkPreview[]> {
    return this.http.get<ChunkPreview[]>(`${this.base}/documents/${encodeURIComponent(documentName)}/chunks`);
  }

  // ── Demo Library ───────────────────────────────────────────────────────────

  listDemoDocuments(): Observable<DemoDocument[]> {
    return this.http.get<DemoDocument[]>(`${this.base}/demo/documents`);
  }

  ingestDemoDocument(name: string, documentType?: string): Observable<IngestResult> {
    const params = documentType ? `?documentType=${encodeURIComponent(documentType)}` : '';
    return this.http.post<IngestResult>(`${this.base}/demo/documents/${encodeURIComponent(name)}/ingest${params}`, {});
  }

  // ── Prompts ────────────────────────────────────────────────────────────────

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

  // ── Chat sessions ──────────────────────────────────────────────────────────

  createChatSession(title?: string): Observable<SessionResponse> {
    return this.http.post<SessionResponse>(`${this.base}/chat/sessions`, { title });
  }

  listChatSessions(): Observable<SessionResponse[]> {
    return this.http.get<SessionResponse[]>(`${this.base}/chat/sessions`);
  }

  deleteChatSession(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/chat/sessions/${id}`);
  }

  getChatMessages(sessionId: string): Observable<MessageResponse[]> {
    return this.http.get<MessageResponse[]>(`${this.base}/chat/sessions/${sessionId}/messages`);
  }

  appendChatMessage(sessionId: string, req: AppendMessageRequest): Observable<MessageResponse> {
    return this.http.post<MessageResponse>(`${this.base}/chat/sessions/${sessionId}/messages`, req);
  }
}
