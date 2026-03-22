import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { VedaApiService } from '../../services/veda-api.service';
import { EvalQuestion, EvaluationReport } from '../../shared/models';

type ActiveTab = 'dataset' | 'reports';

@Component({
  selector: 'app-evaluation',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './evaluation.component.html',
  styleUrl: './evaluation.component.scss',
})
export class EvaluationComponent implements OnInit {
  private readonly api = inject(VedaApiService);

  activeTab = signal<ActiveTab>('dataset');

  // ── Dataset ──────────────────────────────────────────────────────────────────
  questions    = signal<EvalQuestion[]>([]);
  loadingQ     = signal(false);
  showQForm    = signal(false);
  savingQ      = signal(false);
  formQuestion = signal('');
  formExpected = signal('');
  formTags     = signal('');

  // ── Reports ──────────────────────────────────────────────────────────────────
  reports         = signal<EvaluationReport[]>([]);
  loadingR        = signal(false);
  running         = signal(false);
  selectedReport  = signal<EvaluationReport | null>(null);
  runModelOverride = signal('');

  ngOnInit(): void {
    this.loadQuestions();
    this.loadReports();
  }

  // ── Dataset actions ──────────────────────────────────────────────────────────

  loadQuestions(): void {
    this.loadingQ.set(true);
    this.api.listEvalQuestions().subscribe({
      next: list => { this.questions.set(list); this.loadingQ.set(false); },
      error: ()   => this.loadingQ.set(false),
    });
  }

  saveQuestion(): void {
    if (!this.formQuestion().trim() || !this.formExpected().trim()) return;
    const tags = this.formTags().split(',').map(t => t.trim()).filter(Boolean);
    this.savingQ.set(true);
    this.api.saveEvalQuestion({
      question: this.formQuestion(),
      expectedAnswer: this.formExpected(),
      tags,
    }).subscribe({
      next: () => {
        this.savingQ.set(false);
        this.showQForm.set(false);
        this.formQuestion.set('');
        this.formExpected.set('');
        this.formTags.set('');
        this.loadQuestions();
      },
      error: () => this.savingQ.set(false),
    });
  }

  deleteQuestion(id: string): void {
    if (!confirm('Delete this question from the Golden Dataset?')) return;
    this.api.deleteEvalQuestion(id).subscribe(() => this.loadQuestions());
  }

  // ── Reports actions ──────────────────────────────────────────────────────────

  loadReports(): void {
    this.loadingR.set(true);
    this.api.listEvalReports().subscribe({
      next: list => { this.reports.set(list); this.loadingR.set(false); },
      error: ()   => this.loadingR.set(false),
    });
  }

  runEvaluation(): void {
    this.running.set(true);
    this.api.runEvaluation({
      chatModelOverride: this.runModelOverride() || undefined,
    }).subscribe({
      next: report => {
        this.running.set(false);
        this.loadReports();
        this.selectedReport.set(report);
      },
      error: () => this.running.set(false),
    });
  }

  selectReport(report: EvaluationReport): void {
    this.selectedReport.set(report);
  }

  deleteReport(runId: string, event: Event): void {
    event.stopPropagation();
    if (!confirm('Delete this evaluation report?')) return;
    this.api.deleteEvalReport(runId).subscribe(() => {
      this.loadReports();
      if (this.selectedReport()?.runId === runId) {
        this.selectedReport.set(null);
      }
    });
  }

  pct(value: number): string {
    return `${Math.round(value * 100)}%`;
  }

  scoreClass(value: number): string {
    if (value >= 0.7) return 'score-good';
    if (value >= 0.4) return 'score-mid';
    return 'score-bad';
  }
}
