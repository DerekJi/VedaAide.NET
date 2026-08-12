import { Component, effect, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { VedaApiService } from '../../../services/veda-api.service';
import { DemoDocument, IngestResult } from '../../models';

interface DemoEntry extends DemoDocument {
  selected: boolean;
  status?: 'ingesting' | 'done' | 'error';
  result?: IngestResult;
}

/**
 * Demo document library panel: lists preset demo documents and supports one-click
 * loading of selected documents into the knowledge base.
 * Recruiters can experience RAG Q&A immediately without uploading any documents.
 */
@Component({
  selector: 'app-demo-library-panel',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './demo-library-panel.component.html',
  styleUrl: './demo-library-panel.component.scss'
})
export class DemoLibraryPanelComponent implements OnInit {
  private readonly api = inject(VedaApiService);

  entries   = signal<DemoEntry[]>([]);
  loading   = signal(false);
  ingesting = signal(false);

  readonly documentTypes = ['', 'Specification', 'Report', 'BillInvoice', 'Identity', 'Certificate', 'Other'];
  selectedDocType = signal('');

  /** Names of documents already present in the knowledge base — these rows are disabled. */
  readonly ingestedNames = input<ReadonlySet<string>>(new Set());

  /** Emits when all selected documents have been ingested (success or error). */
  readonly ingestionComplete = output<void>();

  constructor() {
    // When a document is deleted externally, clear its local 'done' status so the row becomes re-selectable.
    effect(() => {
      const ingested = this.ingestedNames();
      this.entries.update(entries =>
        entries.map(e =>
          e.status === 'done' && !ingested.has(e.name)
            ? { ...e, status: undefined, result: undefined }
            : e
        )
      );
    });
  }

  readonly recommendedQuestions = [
    'What AI technologies does VedaAide use?',
    'How is the project architecture designed?',
    'What document formats does the system support?',
  ];

  ngOnInit(): void {
    this.loading.set(true);
    this.api.listDemoDocuments().subscribe({
      next: docs => {
        this.entries.set(docs.map(d => ({ ...d, selected: false })));
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  isIngested(name: string): boolean {
    return this.ingestedNames().has(name);
  }

  get hasSelection(): boolean {
    return this.entries().some(e => e.selected && !this.isIngested(e.name));
  }

  toggleAll(checked: boolean): void {
    this.entries.update(entries => entries.map(e => ({ ...e, selected: checked })));
  }

  ingestSelected(): void {
    const toIngest = this.entries().filter(e => e.selected && e.status !== 'done' && !this.isIngested(e.name));
    if (!toIngest.length) return;

    this.ingesting.set(true);
    let remaining = toIngest.length;

    toIngest.forEach(entry => {
      entry.status = 'ingesting';
      this.entries.update(e => [...e]);

      this.api.ingestDemoDocument(entry.name, this.selectedDocType() || undefined).subscribe({
        next: result => {
          entry.status = 'done';
          entry.result = result;
          remaining--;
          this.entries.update(e => [...e]);
          if (remaining === 0) {
            this.ingesting.set(false);
            this.ingestionComplete.emit();
          }
        },
        error: () => {
          entry.status = 'error';
          remaining--;
          this.entries.update(e => [...e]);
          if (remaining === 0) {
            this.ingesting.set(false);
            this.ingestionComplete.emit();
          }
        }
      });
    });
  }
}
