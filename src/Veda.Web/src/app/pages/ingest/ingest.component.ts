import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { VedaApiService } from '../../services/veda-api.service';
import { IngestResult } from '../../shared/models';

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
  imports: [FormsModule],
  templateUrl: './ingest.component.html',
  styleUrl: './ingest.component.scss'
})
export class IngestComponent {
  private api = inject(VedaApiService);

  tab = signal<'notes' | 'documents'>('notes');

  // Notes tab
  noteText = signal('');
  noteBusy = signal(false);

  // Documents tab
  documentName = signal('');
  content = signal('');
  documentType = signal('');
  uploading = signal(false);

  // Shared history (notes + documents)
  entries = signal<IngestEntry[]>([]);

  readonly documentTypes = ['', 'Specification', 'Report', 'BillInvoice', 'Identity', 'Other'];

  saveNote(): void {
    const text = this.noteText().trim();
    if (!text || this.noteBusy()) return;

    const now = new Date();
    const pad = (n: number) => String(n).padStart(2, '0');
    const dateLabel = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
    const noteName = `note-${dateLabel}T${pad(now.getHours())}-${pad(now.getMinutes())}`;
    const contentWithDate = `[${dateLabel}] ${text}`;

    this.noteBusy.set(true);
    const entry: IngestEntry = { name: noteName, type: 'note', status: 'uploading' };
    this.entries.update(e => [entry, ...e]);

    this.api.ingestDocument({
      content: contentWithDate,
      documentName: noteName,
      documentType: 'PersonalNote'
    }).subscribe({
      next: (result: IngestResult) => {
        entry.status = 'done';
        entry.result = result;
        this.entries.update(e => [...e]);
        this.noteText.set('');
        this.noteBusy.set(false);
      },
      error: (err: Error) => {
        entry.status = 'error';
        entry.error = err.message;
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

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.documentName.set(file.name);
    const reader = new FileReader();
    reader.onload = () => this.content.set(reader.result as string);
    reader.readAsText(file);
  }

  ingestDocument(): void {
    if (!this.content() || !this.documentName()) return;

    this.uploading.set(true);
    const entry: IngestEntry = { name: this.documentName(), type: 'document', status: 'uploading' };
    this.entries.update(e => [entry, ...e]);

    this.api.ingestDocument({
      content: this.content(),
      documentName: this.documentName(),
      documentType: this.documentType() || undefined
    }).subscribe({
      next: (result: IngestResult) => {
        entry.status = 'done';
        entry.result = result;
        this.entries.update(e => [...e]);
        this.content.set('');
        this.documentName.set('');
        this.uploading.set(false);
      },
      error: (err: Error) => {
        entry.status = 'error';
        entry.error = err.message;
        this.entries.update(e => [...e]);
        this.uploading.set(false);
      }
    });
  }
}
