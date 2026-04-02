import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { VedaApiService } from '../../services/veda-api.service';
import { AdminService } from '../../services/admin.service';
import { ChunkPreview, DocumentSummary, IngestResult } from '../../shared/models';
import { DemoLibraryPanelComponent } from '../../shared/components/demo-library-panel/demo-library-panel.component';

interface IngestEntry {
  name: string;
  type: 'note' | 'document';
  status: 'pending' | 'uploading' | 'done' | 'error';
  result?: IngestResult;
  error?: string;
}

@Component({
  selector: 'app-ingest',
  standalone: true,
  imports: [FormsModule, DemoLibraryPanelComponent],
  templateUrl: './ingest.component.html',
  styleUrl: './ingest.component.scss'
})
export class IngestComponent implements OnInit {
  private readonly api   = inject(VedaApiService);
  private readonly admin = inject(AdminService);

  readonly documentTypes = ['', 'Specification', 'Report', 'BillInvoice', 'Identity', 'Certificate', 'Other'];

  tab = signal<'notes' | 'upload' | 'library'>('notes');

  // ── Notes tab ──────────────────────────────────────────────────────────────
  noteText = signal('');
  noteBusy = signal(false);

  // ── Upload tab ─────────────────────────────────────────────────────────────
  documentName = signal('');
  content      = signal('');
  selectedFile = signal<File | null>(null);
  documentType = signal('');
  uploading    = signal(false);

  private static readonly BINARY_TYPES = new Set([
    'application/pdf',
    'image/jpeg', 'image/jpg', 'image/png', 'image/webp',
    'image/tiff', 'image/bmp',
  ]);

  // ── Ingest history (notes + uploads) ───────────────────────────────────────
  entries = signal<IngestEntry[]>([]);

  // ── Ingested Documents ─────────────────────────────────────────────────────
  documents       = signal<DocumentSummary[]>([]);
  docsLoading     = signal(false);
  selectedDoc     = signal<DocumentSummary | null>(null);
  chunks          = signal<ChunkPreview[]>([]);
  chunksLoading   = signal(false);
  deletingId      = signal<string | null>(null);
  clearingAll     = signal(false);
  confirmClearAll = signal(false);

  ingestedNameSet = computed(() => new Set(this.documents().map(d => d.documentName)));

  ngOnInit(): void {
    this.loadDocuments();
  }

  // ── Notes ──────────────────────────────────────────────────────────────────
  saveNote(): void {
    const text = this.noteText().trim();
    if (!text || this.noteBusy()) return;

    const now = new Date();
    const pad = (n: number) => String(n).padStart(2, '0');
    const dateLabel = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
    const noteName  = `note-${dateLabel}T${pad(now.getHours())}-${pad(now.getMinutes())}`;

    this.noteBusy.set(true);
    const entry: IngestEntry = { name: noteName, type: 'note', status: 'uploading' };
    this.entries.update(e => [entry, ...e]);

    this.api.ingestDocument({
      content: `[${dateLabel}] ${text}`,
      documentName: noteName,
      documentType: 'PersonalNote'
    }).subscribe({
      next: (result: IngestResult) => {
        entry.status = 'done'; entry.result = result;
        this.entries.update(e => [...e]);
        this.noteText.set('');
        this.noteBusy.set(false);
        this.loadDocuments();
      },
      error: (err: Error) => {
        entry.status = 'error'; entry.error = err.message;
        this.entries.update(e => [...e]);
        this.noteBusy.set(false);
      }
    });
  }

  onNoteKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.saveNote();
    }
  }

  // ── Upload ────────────────────────────────────────────────────────────────
  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file  = input.files?.[0];
    if (!file) return;
    this.documentName.set(file.name);
    this.selectedFile.set(file);
    this.content.set('');
    if (!IngestComponent.BINARY_TYPES.has(file.type)) {
      const reader = new FileReader();
      reader.onload = () => this.content.set(reader.result as string);
      reader.readAsText(file);
    }
  }

  ingestDocument(): void {
    const name = this.documentName();
    const file = this.selectedFile();
    if (!name) return;

    if (file && IngestComponent.BINARY_TYPES.has(file.type)) {
      this.uploading.set(true);
      const entry: IngestEntry = { name, type: 'document', status: 'uploading' };
      this.entries.update(e => [entry, ...e]);
      this.api.uploadFile(file, name, this.documentType() || undefined).subscribe({
        next: (result: IngestResult) => {
          entry.status = 'done'; entry.result = result;
          this.entries.update(e => [...e]);
          this.content.set(''); this.documentName.set(''); this.selectedFile.set(null);
          this.uploading.set(false);
          this.loadDocuments();
        },
        error: (err: Error) => {
          entry.status = 'error'; entry.error = err.message;
          this.entries.update(e => [...e]);
          this.uploading.set(false);
        }
      });
      return;
    }

    if (!this.content()) return;
    this.uploading.set(true);
    const entry: IngestEntry = { name, type: 'document', status: 'uploading' };
    this.entries.update(e => [entry, ...e]);
    this.api.ingestDocument({
      content: this.content(),
      documentName: name,
      documentType: this.documentType() || undefined
    }).subscribe({
      next: (result: IngestResult) => {
        entry.status = 'done'; entry.result = result;
        this.entries.update(e => [...e]);
        this.content.set(''); this.documentName.set(''); this.selectedFile.set(null);
        this.uploading.set(false);
        this.loadDocuments();
      },
      error: (err: Error) => {
        entry.status = 'error'; entry.error = err.message;
        this.entries.update(e => [...e]);
        this.uploading.set(false);
      }
    });
  }

  // ── Ingested Documents ───────────────────────────────────────────────────
  loadDocuments(): void {
    this.docsLoading.set(true);
    this.api.listDocuments().subscribe({
      next: docs => { this.documents.set(docs); this.docsLoading.set(false); },
      error: ()   => this.docsLoading.set(false)
    });
  }

  viewChunks(doc: DocumentSummary): void {
    if (this.selectedDoc()?.documentId === doc.documentId) {
      this.selectedDoc.set(null);
      return;
    }
    this.selectedDoc.set(doc);
    this.chunksLoading.set(true);
    this.api.getDocumentChunks(doc.documentName).subscribe({
      next: chunks => { this.chunks.set(chunks); this.chunksLoading.set(false); },
      error: ()    => this.chunksLoading.set(false)
    });
  }

  deleteDocument(doc: DocumentSummary): void {
    this.deletingId.set(doc.documentId);
    this.admin.deleteDocument(doc.documentId).subscribe({
      next: () => {
        if (this.selectedDoc()?.documentId === doc.documentId) this.selectedDoc.set(null);
        this.documents.update(d => d.filter(x => x.documentId !== doc.documentId));
        this.deletingId.set(null);
      },
      error: () => this.deletingId.set(null)
    });
  }

  requestClearAll(): void  { this.confirmClearAll.set(true); }
  cancelClearAll(): void   { this.confirmClearAll.set(false); }

  clearAll(): void {
    this.clearingAll.set(true);
    this.confirmClearAll.set(false);
    this.admin.deleteAllData().subscribe({
      next: () => {
        this.documents.set([]);
        this.selectedDoc.set(null);
        this.clearingAll.set(false);
      },
      error: () => this.clearingAll.set(false)
    });
  }
}
