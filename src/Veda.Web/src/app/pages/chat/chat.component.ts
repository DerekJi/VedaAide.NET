import { Component, ElementRef, inject, OnDestroy, OnInit, signal, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ChatStreamService } from '../../services/chat-stream.service';
import { ChatSessionService } from '../../services/chat-session.service';
import { FeedbackService } from '../../services/feedback.service';
import { ChatMessage, RagStreamChunk } from '../../shared/models';
import { FeedbackBarComponent } from '../../shared/components/feedback-bar/feedback-bar.component';
import { ChatSidebarComponent } from '../../shared/components/chat-sidebar/chat-sidebar.component';
import { CHAT_LABELS, detectChatLang } from '../../shared/chat-labels';

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [FormsModule, FeedbackBarComponent, ChatSidebarComponent],
  templateUrl: './chat.component.html',
  styleUrl: './chat.component.scss'
})
export class ChatComponent implements OnInit, OnDestroy {
  @ViewChild('messagesArea') messagesArea?: ElementRef<HTMLElement>;

  private readonly stream      = inject(ChatStreamService);
  private readonly feedback    = inject(FeedbackService);
  protected readonly chatSession = inject(ChatSessionService);

  question = signal('');
  busy     = signal(false);

  private lastQuery      = '';
  private streamSub?: Subscription;
  private copyListener?: (e: Event) => void;

  // ── Lifecycle ────────────────────────────────────────────────────────────────

  ngOnInit(): void {
    // Implicit feedback: user copies text → ResultAccepted
    this.copyListener = () => this.reportLastSourcesAs('ResultAccepted');
    document.addEventListener('copy', this.copyListener);
  }

  ngOnDestroy(): void {
    if (this.copyListener) document.removeEventListener('copy', this.copyListener);
    this.abortStream();
  }

  // ── Stream abort (called by sidebar before session switch) ───────────────────

  abortStream(): void {
    this.streamSub?.unsubscribe();
    this.streamSub = undefined;
    this.busy.set(false);
  }

  // ── Public API ───────────────────────────────────────────────────────────────

  ask(): void {
    const q = this.question().trim();
    if (!q || this.busy()) return;

    // Implicit feedback: follow-up question → previous sources ResultAccepted
    this.reportLastSourcesAs('ResultAccepted');

    const userMsg: ChatMessage = { id: crypto.randomUUID(), role: 'user', text: q };
    this.chatSession.addMessage(userMsg);
    this.question.set('');
    this.busy.set(true);
    this.lastQuery = q;

    const assistantMsg: ChatMessage = {
      id:        crypto.randomUUID(),
      role:      'assistant',
      text:      '',
      streaming: true,
      query:     q
    };
    this.chatSession.addMessage(assistantMsg);

    let streamingText = '';

    this.streamSub = this.stream.stream(q).subscribe({
      next: (chunk: RagStreamChunk) => {
        if (chunk.type === 'sources') {
          this.chatSession.refreshLastMessage(msg => ({ ...msg, sources: chunk.sources }));
        } else if (chunk.type === 'token') {
          streamingText += chunk.token ?? '';
          this.chatSession.refreshLastMessage(msg => ({ ...msg, text: streamingText }));
        } else if (chunk.type === 'done') {
          this.chatSession.refreshLastMessage(msg => ({
            ...msg,
            streaming:       false,
            confidence:      chunk.answerConfidence,
            isHallucination: chunk.isHallucination,
            lang:            detectChatLang(streamingText)
          }));
        }
      },
      error: () => {
        this.chatSession.refreshLastMessage(msg => ({
          ...msg,
          text:      msg.text + '\n\n[Connection error — please retry]',
          streaming: false
        }));
        this.busy.set(false);
        this.streamSub = undefined;
      },
      complete: () => {
        this.busy.set(false);
        this.streamSub = undefined;
        this.chatSession.finalizeMessage();
      }
    });
  }

  submit(): void { this.ask(); }

  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.submit();
    }
  }

  toggleSources(msg: ChatMessage): void {
    this.chatSession.updateMessage(msg.id, m => ({ ...m, sourcesExpanded: !m.sourcesExpanded }));
  }

  copyMessage(msg: ChatMessage): void {
    navigator.clipboard.writeText(msg.text).catch(() => {
      // Fallback for browsers without clipboard API
      const ta = document.createElement('textarea');
      ta.value = msg.text;
      document.body.appendChild(ta);
      ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
    });
  }

  getChunkIds(msg: ChatMessage): string[] {
    return (msg.sources ?? []).map(s => s.chunkId).filter((id): id is string => !!id);
  }

  labelsFor(msg: ChatMessage) {
    return CHAT_LABELS[msg.lang ?? 'zh'];
  }

  // ── Private helpers ──────────────────────────────────────────────────────────

  private reportLastSourcesAs(type: 'ResultAccepted' | 'ResultRejected'): void {
    const msgs = this.chatSession.messages();
    const last  = [...msgs].reverse().find(m => m.role === 'assistant' && !m.streaming);
    if (!last?.sources?.length) return;

    last.sources.forEach(src => {
      if (src.chunkId) {
        this.feedback.record({
          userId:            'anonymous',
          type,
          sessionId:         this.chatSession.activeId(),
          relatedChunkId:    src.chunkId,
          relatedDocumentId: src.documentId ?? undefined,
          query:             last.query
        });
      }
    });
  }
}

