import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ChatStreamService } from '../../services/chat-stream.service';
import { VedaApiService } from '../../services/veda-api.service';
import { IngestResult, RagStreamChunk, SourceReference } from '../../shared/models';

interface Message {
  role: 'user' | 'assistant' | 'note';
  text: string;
  streaming?: boolean;
  sources?: SourceReference[];
  confidence?: number;
  isHallucination?: boolean;
  noteResult?: IngestResult;
  error?: string;
}

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './chat.component.html',
  styleUrl: './chat.component.scss'
})
export class ChatComponent {
  private stream = inject(ChatStreamService);
  private api = inject(VedaApiService);

  question = signal('');
  messages = signal<Message[]>([]);
  busy = signal(false);
  mode = signal<'ask' | 'remember'>('ask');

  ask(): void {
    const q = this.question().trim();
    if (!q || this.busy()) return;

    this.messages.update((m: Message[]) => [...m, { role: 'user', text: q }]);
    this.question.set('');
    this.busy.set(true);

    const assistant: Message = { role: 'assistant', text: '', streaming: true };
    this.messages.update((m: Message[]) => [...m, assistant]);

    this.stream.stream(q).subscribe({
      next: (chunk: RagStreamChunk) => {
        if (chunk.type === 'sources') {
          assistant.sources = chunk.sources;
        } else if (chunk.type === 'token') {
          assistant.text += chunk.token ?? '';
        } else if (chunk.type === 'done') {
          assistant.streaming = false;
          assistant.confidence = chunk.answerConfidence;
          assistant.isHallucination = chunk.isHallucination;
        }
        this.messages.update((m: Message[]) => [...m]);
      },
      error: () => {
        assistant.text += '\n\n[Connection error — please retry]';
        assistant.streaming = false;
        this.busy.set(false);
        this.messages.update((m: Message[]) => [...m]);
      },
      complete: () => this.busy.set(false)
    });
  }

  remember(): void {
    const text = this.question().trim();
    if (!text || this.busy()) return;

    const now = new Date();
    const pad = (n: number) => String(n).padStart(2, '0');
    const dateLabel = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
    const noteName = `note-${dateLabel}T${pad(now.getHours())}-${pad(now.getMinutes())}`;

    // 在内容前缀注入具体日期，使"今天""昨天"等模糊时间词在检索时有明确锚点
    const contentWithDate = `[${dateLabel}] ${text}`;
    this.question.set('');
    this.busy.set(true);

    this.api.ingestDocument({
      content: contentWithDate,
      documentName: noteName,
      documentType: 'PersonalNote'
    }).subscribe({
      next: (result: IngestResult) => {
        this.messages.update((m: Message[]) => [...m, { role: 'note', text, noteResult: result }]);
        this.busy.set(false);
      },
      error: () => {
        this.messages.update((m: Message[]) => [...m, { role: 'note', text, error: '记录失败，请重试' }]);
        this.busy.set(false);
      }
    });
  }

  submit(): void {
    if (this.mode() === 'ask') {
      this.ask();
    } else {
      this.remember();
    }
  }

  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.submit();
    }
  }
}
