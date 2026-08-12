import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { FeedbackService } from '../../../services/feedback.service';
import { CHAT_LABELS, ChatLang } from '../../chat-labels';

/**
 * Explicit feedback bar: 👍 / 👎 buttons shown once at the end of each assistant message.
 * The buttons are disabled after clicking to prevent duplicate submissions.
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
  @Input() lang: ChatLang = 'zh';

  @Output() voted = new EventEmitter<'up' | 'down'>();

  given: 'up' | 'down' | null = null;

  private readonly feedback = inject(FeedbackService);

  readonly labels = CHAT_LABELS;

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
