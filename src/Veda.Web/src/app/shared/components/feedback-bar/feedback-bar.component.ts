import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { FeedbackService } from '../../../services/feedback.service';

/**
 * 显式反馈条：👍 / 👎 按钮，在每条 assistant 消息末尾显示一次。
 * 点击后按钮禁用，防止重复提交。
 */
@Component({
  selector: 'app-feedback-bar',
  standalone: true,
  templateUrl: './feedback-bar.component.html',
  styleUrl: './feedback-bar.component.scss'
})
export class FeedbackBarComponent {
  @Input({ required: true }) sessionId!: string;
  @Input({ required: true }) query!: string;
  @Input() chunkIds: string[] = [];

  @Output() voted = new EventEmitter<'up' | 'down'>();

  given: 'up' | 'down' | null = null;

  private readonly feedback = inject(FeedbackService);

  vote(type: 'up' | 'down'): void {
    if (this.given) return;
    this.given = type;
    this.voted.emit(type);

    const behaviorType = type === 'up' ? 'ResultAccepted' : 'ResultRejected';
    const chunksToReport = this.chunkIds.length > 0 ? this.chunkIds : [''];

    chunksToReport.forEach(chunkId => {
      this.feedback.record({
        userId: 'anonymous',
        type: behaviorType,
        sessionId: this.sessionId,
        relatedChunkId: chunkId || undefined,
        query: this.query
      });
    });
  }
}
