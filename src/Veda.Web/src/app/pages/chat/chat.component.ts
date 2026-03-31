import { Component, ElementRef, inject, OnDestroy, OnInit, signal, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ChatStreamService } from '../../services/chat-stream.service';
import { FeedbackService } from '../../services/feedback.service';
import { RagStreamChunk, SourceReference } from '../../shared/models';
import { FeedbackBarComponent } from '../../shared/components/feedback-bar/feedback-bar.component';
import { CHAT_LABELS, ChatLang, detectChatLang } from '../../shared/chat-labels';

interface Message {
  role: 'user' | 'assistant';
  text: string;
  streaming?: boolean;
  sources?: SourceReference[];
  confidence?: number;
  isHallucination?: boolean;
  sourcesExpanded?: boolean;
  query?: string;        // the question that produced this assistant answer
  lang?: ChatLang;       // detected language of the answer
}

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [FormsModule, FeedbackBarComponent],
  templateUrl: './chat.component.html',
  styleUrl: './chat.component.scss'
})
export class ChatComponent implements OnInit, OnDestroy {
  @ViewChild('messagesArea') messagesArea?: ElementRef<HTMLElement>;

  private readonly stream   = inject(ChatStreamService);
  private readonly feedback = inject(FeedbackService);

  readonly sessionId = crypto.randomUUID();

  question = signal('');
  messages = signal<Message[]>([]);
  busy     = signal(false);

  private lastQuery     = '';
  private copyListener?: (e: Event) => void;

  // ── Lifecycle ────────────────────────────────────────────────────────────────

  ngOnInit(): void {
    // 隐式反馈：用户复制文本 → ResultAccepted
    this.copyListener = () => this.reportLastSourcesAs('ResultAccepted');
    document.addEventListener('copy', this.copyListener);
  }

  ngOnDestroy(): void {
    if (this.copyListener) document.removeEventListener('copy', this.copyListener);
  }

  // ── Public API ───────────────────────────────────────────────────────────────

  ask(): void {
    const q = this.question().trim();
    if (!q || this.busy()) return;

    // 隐式反馈：追问 → 上一轮来源 ResultAccepted
    this.reportLastSourcesAs('ResultAccepted');

    this.messages.update(m => [...m, { role: 'user', text: q }]);
    this.question.set('');
    this.busy.set(true);
    this.lastQuery = q;

    const assistant: Message = {
      role: 'assistant',
      text: '',
      streaming: true,
      query: q
    };
    this.messages.update(m => [...m, assistant]);

    this.stream.stream(q).subscribe({
      next: (chunk: RagStreamChunk) => {
        if (chunk.type === 'sources') {
          assistant.sources = chunk.sources;
        } else if (chunk.type === 'token') {
          assistant.text += chunk.token ?? '';
        } else if (chunk.type === 'done') {
          assistant.streaming   = false;
          assistant.confidence  = chunk.answerConfidence;
          assistant.isHallucination = chunk.isHallucination;
          assistant.lang        = detectChatLang(assistant.text);
        }
        this.messages.update(m => [...m]);
      },
      error: () => {
        assistant.text    += '\n\n[Connection error — please retry]';
        assistant.streaming = false;
        this.busy.set(false);
        this.messages.update(m => [...m]);
      },
      complete: () => this.busy.set(false)
    });
  }

  submit(): void { this.ask(); }

  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.submit();
    }
  }

  toggleSources(msg: Message): void {
    msg.sourcesExpanded = !msg.sourcesExpanded;
    this.messages.update(m => [...m]);
  }

  getChunkIds(msg: Message): string[] {
    return (msg.sources ?? []).map(s => s.chunkId).filter((id): id is string => !!id);
  }

  labelsFor(msg: Message) {
    return CHAT_LABELS[msg.lang ?? 'zh'];
  }

  // ── Private helpers ──────────────────────────────────────────────────────────

  private reportLastSourcesAs(type: 'ResultAccepted' | 'ResultRejected'): void {
    const msgs = this.messages();
    const last  = [...msgs].reverse().find(m => m.role === 'assistant' && !m.streaming);
    if (!last?.sources?.length) return;

    last.sources.forEach(src => {
      if (src.chunkId) {
        this.feedback.record({
          userId:           'anonymous',
          type,
          sessionId:        this.sessionId,
          relatedChunkId:   src.chunkId,
          relatedDocumentId: src.documentId ?? undefined,
          query:            last.query
        });
      }
    });
  }
}

