import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { VedaApiService } from '../../../services/veda-api.service';
import { DemoDocument, IngestResult } from '../../models';

interface DemoEntry extends DemoDocument {
  selected: boolean;
  status?: 'ingesting' | 'done' | 'error';
  result?: IngestResult;
}

/**
 * 示例文档库面板：列出预置演示文档，支持勾选后一键加载到知识库。
 * 招聘方可零上传直接体验 RAG 问答效果。
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

  entries  = signal<DemoEntry[]>([]);
  loading  = signal(false);
  ingesting = signal(false);

  readonly recommendedQuestions = [
    'VedaAide 使用了哪些 AI 技术？',
    '项目的架构是怎样设计的？',
    '系统支持哪些文档格式？',
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

  get hasSelection(): boolean {
    return this.entries().some(e => e.selected);
  }

  toggleAll(checked: boolean): void {
    this.entries.update(entries => entries.map(e => ({ ...e, selected: checked })));
  }

  ingestSelected(): void {
    const toIngest = this.entries().filter(e => e.selected && e.status !== 'done');
    if (!toIngest.length) return;

    this.ingesting.set(true);
    let remaining = toIngest.length;

    toIngest.forEach(entry => {
      entry.status = 'ingesting';
      this.entries.update(e => [...e]);

      this.api.ingestDemoDocument(entry.name).subscribe({
        next: result => {
          entry.status = 'done';
          entry.result = result;
          remaining--;
          this.entries.update(e => [...e]);
          if (remaining === 0) this.ingesting.set(false);
        },
        error: () => {
          entry.status = 'error';
          remaining--;
          this.entries.update(e => [...e]);
          if (remaining === 0) this.ingesting.set(false);
        }
      });
    });
  }
}
