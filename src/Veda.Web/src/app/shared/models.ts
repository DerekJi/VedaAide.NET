/** API response and request models matching backend DTOs */

import type { ChatLang } from './chat-labels';

/** 临时附件（Ephemeral RAG）：前端内存中存储，不持久化。 */
export interface EphemeralAttachment {
  fileName: string;
  extractedText: string;
}

/** POST /api/context/extract 的响应体。 */
export interface ContextExtractResponse {
  text: string;
  fileName: string;
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  text: string;
  streaming?: boolean;
  queued?: boolean;     // true while waiting in queue before being sent
  sources?: SourceReference[];
  confidence?: number;
  isHallucination?: boolean;
  sourcesExpanded?: boolean;
  query?: string;       // the question that produced this assistant answer
  lang?: ChatLang;      // detected language of the answer
}

export interface ChatSession {
  id: string;
  title: string;
  createdAt: number;    // Unix milliseconds
  messages: ChatMessage[];
}

export interface IngestRequest {
  content: string;
  documentName: string;
  documentType?: string;
}

export interface IngestResult {
  documentId: string;
  documentName: string;
  chunksStored: number;
}

export interface QueryRequest {
  question: string;
  documentType?: string;
  topK?: number;
  minSimilarity?: number;
  dateFrom?: string;
  dateTo?: string;
}

export interface SourceReference {
  documentName: string;
  chunkContent: string;
  similarity: number;
  chunkId?: string;
  documentId?: string;
}

export type BehaviorType =
  | 'ResultAccepted'
  | 'ResultRejected'
  | 'SourceClicked'
  | 'QueryRefined';

export interface FeedbackRequest {
  userId: string;
  type: BehaviorType;
  sessionId?: string;
  relatedDocumentId?: string;
  relatedChunkId?: string;
  query?: string;
}

export interface QueryResponse {
  answer: string;
  isHallucination: boolean;
  answerConfidence: number;
  sources: SourceReference[];
}

export interface RagStreamChunk {
  type: 'sources' | 'token' | 'done';
  token?: string;
  sources?: SourceReference[];
  answerConfidence?: number;
  isHallucination?: boolean;
}

export interface PromptTemplate {
  id: number;
  name: string;
  version: string;
  content: string;
  documentType?: number;
  createdAt: string;
}

export interface SavePromptRequest {
  name: string;
  version: string;
  content: string;
  documentType?: number;
}

// ── Document Browser (Stage 5) ───────────────────────────────────────────────

export interface DocumentSummary {
  documentId: string;
  documentName: string;
  documentType: string;
  chunkCount: number;
}

export interface ChunkPreview {
  chunkIndex: number;
  content: string;
  documentType: string;
}

export interface DemoDocument {
  name: string;
  description: string;
  sizeBytes: number;
  extension: string;
}

// ── Evaluation (Phase 6) ────────────────────────────────────────────────────

export interface EvalQuestion {
  id: string;
  question: string;
  expectedAnswer: string;
  tags: string[];
  createdAt: string;
}

export interface SaveEvalQuestionRequest {
  question: string;
  expectedAnswer: string;
  tags?: string[];
}

export interface EvalMetrics {
  faithfulness: number;
  answerRelevancy: number;
  contextRecall: number;
  overall: number;
}

export interface EvalResult {
  questionId: string;
  question: string;
  expectedAnswer: string;
  actualAnswer: string;
  metrics: EvalMetrics;
  sources: SourceReference[];
  isHallucination: boolean;
  modelName: string;
  evaluatedAt: string;
}

export interface EvaluationReport {
  runId: string;
  runAt: string;
  modelName: string;
  results: EvalResult[];
  avgFaithfulness: number;
  avgAnswerRelevancy: number;
  avgContextRecall: number;
  avgOverall: number;
}

export interface RunEvaluationRequest {
  questionIds?: string[];
  chatModelOverride?: string;
}

// ── Chat session backend DTOs (Phase 2) ──────────────────────────────────────

export interface SessionResponse {
  sessionId: string;
  title: string;
  createdAt: string;
  updatedAt: string;
}

export interface MessageSourceDto {
  documentName: string;
  chunkContent: string;
  similarity: number;
  chunkId?: string;
  documentId?: string;
}

export interface MessageResponse {
  messageId: string;
  sessionId: string;
  role: string;
  content: string;
  confidence?: number;
  isHallucination: boolean;
  sources: MessageSourceDto[];
  createdAt: string;
}

export interface AppendMessageRequest {
  role: string;
  content: string;
  confidence?: number;
  isHallucination?: boolean;
  sources?: MessageSourceDto[];
}
