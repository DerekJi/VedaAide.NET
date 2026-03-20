import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { VedaApiService } from '../../services/veda-api.service';
import { IngestResult } from '../../shared/models';

interface UploadEntry {
  name: string;
  status: 'pending' | 'uploading' | 'done' | 'error';
  result?: IngestResult;
  error?: string;
}

@Component({
  selector: 'app-documents',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './documents.component.html',
  styleUrl: './documents.component.scss'
})
export class DocumentsComponent {
  private api = inject(VedaApiService);

  documentName = signal('');
  content = signal('');
  documentType = signal('');
  entries = signal<UploadEntry[]>([]);
  uploading = signal(false);

  readonly documentTypes = ['', 'Specification', 'Report', 'BillInvoice', 'Other'];

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.documentName.set(file.name);
    const reader = new FileReader();
    reader.onload = () => this.content.set(reader.result as string);
    reader.readAsText(file);
  }

  ingest(): void {
    if (!this.content() || !this.documentName()) return;
    this.uploading.set(true);
    const entry: UploadEntry = { name: this.documentName(), status: 'uploading' };
    this.entries.update((e: UploadEntry[]) => [entry, ...e]);

    this.api.ingestDocument({
      content: this.content(),
      documentName: this.documentName(),
      documentType: this.documentType() || undefined
    }).subscribe({
      next: (result: IngestResult) => {
        entry.status = 'done';
        entry.result = result;
        this.entries.update((e: UploadEntry[]) => [...e]);
        this.content.set('');
        this.documentName.set('');
        this.uploading.set(false);
      },
      error: (err: Error) => {
        entry.status = 'error';
        entry.error = err.message;
        this.entries.update((e: UploadEntry[]) => [...e]);
        this.uploading.set(false);
      }
    });
  }
}
