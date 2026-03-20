import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ChatStreamService } from '../../services/chat-stream.service';
import { RagStreamChunk, SourceReference } from '../../shared/models';

interface Message {
  role: 'user' | 'assistant';
  text: string;
  streaming?: boolean;
  sources?: SourceReference[];
  confidence?: number;
  isHallucination?: boolean;
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

  question = signal('');
  messages = signal<Message[]>([]);
  busy = signal(false);

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

  submit(): void {
    this.ask();
  }

  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.submit();
    }
  }
}
