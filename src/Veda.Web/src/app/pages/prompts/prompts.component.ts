import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { VedaApiService } from '../../services/veda-api.service';
import { PromptTemplate } from '../../shared/models';

const DOC_TYPE_LABELS: Record<number, string> = {
  0: 'Bill / Invoice',
  1: 'Specification',
  2: 'Report',
  3: 'Other',
};

@Component({
  selector: 'app-prompts',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './prompts.component.html',
  styleUrl: './prompts.component.scss'
})
export class PromptsComponent implements OnInit {
  private readonly api = inject(VedaApiService);

  templates = signal<PromptTemplate[]>([]);
  loading   = signal(false);
  saving    = signal(false);
  showForm  = signal(false);
  isEditing = signal(false);

  formName    = signal('');
  formVersion = signal('1.0');
  formContent = signal('');
  formDocType = signal('');

  readonly docTypeOptions = [
    { value: '',  label: 'Universal (all types)' },
    { value: '0', label: 'Bill / Invoice' },
    { value: '1', label: 'Specification' },
    { value: '2', label: 'Report' },
    { value: '3', label: 'Other' },
  ];

  ngOnInit(): void {
    this.loadTemplates();
  }

  loadTemplates(): void {
    this.loading.set(true);
    this.api.listPrompts().subscribe({
      next: list => { this.templates.set(list); this.loading.set(false); },
      error: ()   => this.loading.set(false),
    });
  }

  openCreate(): void {
    this.isEditing.set(false);
    this.formName.set('');
    this.formVersion.set('1.0');
    this.formContent.set('');
    this.formDocType.set('');
    this.showForm.set(true);
  }

  openEdit(t: PromptTemplate): void {
    this.isEditing.set(true);
    this.formName.set(t.name);
    this.formVersion.set(t.version);
    this.formContent.set(t.content);
    this.formDocType.set(t.documentType != null ? String(t.documentType) : '');
    this.showForm.set(true);
  }

  save(): void {
    const docType = this.formDocType() !== '' ? Number(this.formDocType()) : undefined;
    this.saving.set(true);
    this.api.savePrompt({
      name: this.formName(),
      version: this.formVersion(),
      content: this.formContent(),
      documentType: docType,
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.showForm.set(false);
        this.loadTemplates();
      },
      error: () => this.saving.set(false),
    });
  }

  delete(id: number): void {
    if (!confirm('Delete this template? This cannot be undone.')) return;
    this.api.deletePrompt(id).subscribe(() => this.loadTemplates());
  }

  cancel(): void {
    this.showForm.set(false);
  }

  docTypeLabel(value: number | undefined): string {
    if (value == null) return 'Universal';
    return DOC_TYPE_LABELS[value] ?? 'Unknown';
  }
}

