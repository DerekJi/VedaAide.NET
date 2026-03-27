import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { VedaApiService } from '../../services/veda-api.service';
import { AdminService } from '../../services/admin.service';
import { ChunkPreview, DocumentSummary } from '../../shared/models';
import { DemoLibraryPanelComponent } from '../../shared/components/demo-library-panel/demo-library-panel.component';

@Component({
  selector: 'app-documents',
  standalone: true,
  imports: [FormsModule, DemoLibraryPanelComponent],
  templateUrl: './documents.component.html',
  styleUrl: './documents.component.scss'
})
export class DocumentsComponent implements OnInit {
  private readonly api   = inject(VedaApiService);
  private readonly admin = inject(AdminService);

  documents       = signal<DocumentSummary[]>([]);
  loading         = signal(false);
  selectedDoc     = signal<DocumentSummary | null>(null);
  chunks          = signal<ChunkPreview[]>([]);
  chunksLoading   = signal(false);
  deletingId      = signal<string | null>(null);
  clearingAll     = signal(false);
  confirmClearAll = signal(false);

  ngOnInit(): void {
    this.loadDocuments();
  }

  loadDocuments(): void {
    this.loading.set(true);
    this.api.listDocuments().subscribe({
      next: docs => { this.documents.set(docs); this.loading.set(false); },
      error: ()  => this.loading.set(false)
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

  requestClearAll(): void {
    this.confirmClearAll.set(true);
  }

  cancelClearAll(): void {
    this.confirmClearAll.set(false);
  }

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

