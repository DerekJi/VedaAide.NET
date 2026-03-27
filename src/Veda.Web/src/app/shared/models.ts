/** API response and request models matching backend DTOs */

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
