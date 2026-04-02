import { Component, computed, effect, ElementRef, inject, OnDestroy, OnInit, signal, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ChatStreamService } from '../../services/chat-stream.service';
import { ChatSessionService } from '../../services/chat-session.service';
import { FeedbackService } from '../../services/feedback.service';
import { VedaApiService } from '../../services/veda-api.service';
import { ChatMessage, EphemeralAttachment, RagStreamChunk } from '../../shared/models';
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
  @ViewChild('fileInput') fileInputRef?: ElementRef<HTMLInputElement>;

  private readonly stream      = inject(ChatStreamService);
  private readonly feedback    = inject(FeedbackService);
  private readonly api         = inject(VedaApiService);
  protected readonly chatSession = inject(ChatSessionService);

  question = signal('');
  busy     = signal(false);

  // ── Ephemeral attachment state ────────────────────────────────────────────────
  /** 当前附件：提取文本已就绪；null 表示无附件。 */
  attachment     = signal<EphemeralAttachment | null>(null);
  /** 正在提取文件文本中（loading 状态）。 */
  attachLoading  = signal(false);
  /** 提取错误信息；null 表示无错误。 */
  attachError    = signal<string | null>(null);

  /** Messages waiting to be sent after the current stream finishes. */
  private readonly pendingQueue = signal<Array<{ msgId: string; text: string; ctx: string | null }>>([]);
  protected readonly queueSize  = computed(() => this.pendingQueue().length);

  private streamSub?: Subscription;
  private copyListener?: (e: Event) => void;
  private pasteListener?: (e: Event) => void;

  constructor() {
    // Auto-scroll to bottom whenever the message list changes
    effect(() => {
      this.chatSession.messages();
      setTimeout(() => this.scrollToBottom(), 0);
    });
  }

  // ── Lifecycle ────────────────────────────────────────────────────────────────

  ngOnInit(): void {
    // Implicit feedback: user copies text → ResultAccepted
    this.copyListener = () => this.reportLastSourcesAs('ResultAccepted');
    document.addEventListener('copy', this.copyListener);

    // 全局粘贴：支持 Ctrl+V 粘贴截图
    this.pasteListener = (e: Event) => this.handlePaste(e as ClipboardEvent);
    document.addEventListener('paste', this.pasteListener);
  }

  ngOnDestroy(): void {
    if (this.copyListener)  document.removeEventListener('copy',  this.copyListener);
    if (this.pasteListener) document.removeEventListener('paste', this.pasteListener);
    this.abortStream();
  }

  // ── Stream abort (called by sidebar before session switch) ───────────────────

  abortStream(): void {
    this.streamSub?.unsubscribe();
    this.streamSub = undefined;
    // Cancel queued messages — un-flag them so they appear as plain (unanswered) user messages
    this.pendingQueue().forEach(item =>
      this.chatSession.updateMessage(item.msgId, m => ({ ...m, queued: false }))
    );
    this.pendingQueue.set([]);
    this.chatSession.saveProgress();  // flush partial messages so they survive session switch
    this.busy.set(false);
  }

  // ── Ephemeral attachment handlers ─────────────────────────────────────────────

  openFilePicker(): void {
    this.fileInputRef?.nativeElement.click();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    // Reset so selecting the same file again triggers change event
    input.value = '';
    this.uploadAttachment(file);
  }

  clearAttachment(): void {
    this.attachment.set(null);
    this.attachError.set(null);
  }

  private handlePaste(event: ClipboardEvent): void {
    const items = event.clipboardData?.items;
    if (!items) return;
    for (const item of Array.from(items)) {
      if (item.type.startsWith('image/')) {
        const file = item.getAsFile();
        if (file) {
          this.uploadAttachment(file);
          break;
        }
      }
    }
  }

  private uploadAttachment(file: File): void {
    this.attachment.set(null);
    this.attachError.set(null);
    this.attachLoading.set(true);

    this.api.extractContextFile(file).subscribe({
      next: res => {
        this.attachment.set({ fileName: res.fileName, extractedText: res.text });
        this.attachLoading.set(false);
      },
      error: err => {
        const msg = err?.error?.error ?? '文件提取失败，请重试。';
        this.attachError.set(msg);
        this.attachLoading.set(false);
      }
    });
  }

  // ── Public API ───────────────────────────────────────────────────────────────

  ask(): void {
    const q = this.question().trim();
    if (!q || this.attachLoading()) return;

    // Implicit feedback: submitting a follow-up → previous sources ResultAccepted
    this.reportLastSourcesAs('ResultAccepted');

    const ctx = this.attachment()?.extractedText ?? null;

    const userMsg: ChatMessage = {
      id:     crypto.randomUUID(),
      role:   'user',
      text:   ctx ? `${q}\n📎 ${this.attachment()!.fileName}` : q,
      queued: this.busy()   // mark as queued when a stream is already running
    };
    this.chatSession.addMessage(userMsg);
    this.question.set('');
    // 附件仅用于当前这次提问，提交后清除
    this.attachment.set(null);
    this.attachError.set(null);

    if (this.busy()) {
      this.pendingQueue.update(arr => [...arr, { msgId: userMsg.id, text: q, ctx }]);
      return;
    }

    this.startStream(userMsg.id, q, ctx);
  }

  // ── Private stream helpers ───────────────────────────────────────────────────

  private startStream(userMsgId: string, q: string, ctx: string | null): void {
    // Remove queued flag — the message is now being processed
    this.chatSession.updateMessage(userMsgId, m => ({ ...m, queued: false }));
    this.busy.set(true);

    const assistantMsg: ChatMessage = {
      id:        crypto.randomUUID(),
      role:      'assistant',
      text:      '',
      streaming: true,
      query:     q
    };
    this.chatSession.addMessage(assistantMsg);
    // 固定住本次流对应的 assistant 消息 ID，防止后续入队的用户消息
    // 导致 refreshLastMessage 写错消息（"最后一条"已不是本 assistant 消息）
    const assistantMsgId = assistantMsg.id;

    let streamingText = '';

    const obs$ = ctx
      ? this.stream.streamWithContext(q, ctx)
      : this.stream.stream(q);

    this.streamSub = obs$.subscribe({
      next: (chunk: RagStreamChunk) => {
        if (chunk.type === 'sources') {
          this.chatSession.updateMessage(assistantMsgId, msg => ({ ...msg, sources: chunk.sources }));
        } else if (chunk.type === 'token') {
          streamingText += chunk.token ?? '';
          this.chatSession.updateMessage(assistantMsgId, msg => ({ ...msg, text: streamingText }));
        } else if (chunk.type === 'done') {
          this.chatSession.updateMessage(assistantMsgId, msg => ({
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
        this.processQueue();
      },
      complete: () => {
        this.busy.set(false);
        this.streamSub = undefined;
        this.chatSession.finalizeMessage();
        this.processQueue();
      }
    });
  }

  private processQueue(): void {
    const queue = this.pendingQueue();
    if (queue.length === 0) return;
    const [next, ...rest] = queue;
    this.pendingQueue.set(rest);
    this.startStream(next.msgId, next.text, next.ctx);
  }

  submit(): void { this.ask(); }

  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      if (!this.attachLoading()) this.submit();
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

  private scrollToBottom(): void {
    const el = this.messagesArea?.nativeElement;
    if (el) el.scrollTop = el.scrollHeight;
  }

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
