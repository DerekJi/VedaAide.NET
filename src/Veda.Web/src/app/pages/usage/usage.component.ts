import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { UsageService, TokenUsageSummary, TokenUsagePeriod } from '../../services/usage.service';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-usage',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './usage.component.html',
  styleUrl: './usage.component.scss'
})
export class UsageComponent implements OnInit {
  private readonly usageService = inject(UsageService);
  readonly auth = inject(AuthService);

  summary = signal<TokenUsageSummary | null>(null);
  loading = signal(true);
  error   = signal<string | null>(null);

  ngOnInit(): void {
    this.usageService.getSummary().subscribe({
      next:  s => { this.summary.set(s); this.loading.set(false); },
      error: () => { this.error.set('Failed to load usage data.'); this.loading.set(false); }
    });
  }

  periodIsEmpty(period: TokenUsagePeriod): boolean {
    return period.totalTokens === 0;
  }
}
