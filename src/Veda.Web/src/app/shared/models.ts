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
