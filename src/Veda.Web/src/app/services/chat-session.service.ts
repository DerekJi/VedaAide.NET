import { Injectable, signal, computed, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ChatMessage, ChatSession } from '../shared/models';
import { VedaApiService } from './veda-api.service';

const STORAGE_KEY = 'veda_chat_sessions';
const MAX_SESSIONS = 50;

@Injectable({ providedIn: 'root' })
export class ChatSessionService {
  private readonly api = inject(VedaApiService);

  private readonly _sessions = signal<ChatSession[]>(this.loadFromStorage());
  private readonly _activeId  = signal<string>('');
  private readonly _messages  = signal<ChatMessage[]>([]);

  /** All sessions (newest first) */
  readonly sessions     = this._sessions.asReadonly();
  /** Currently active session id */
  readonly activeId     = this._activeId.asReadonly();
  /** Messages of the active session (live during streaming) */
  readonly messages     = this._messages.asReadonly();
  /** Active session metadata */
  readonly activeSession = computed(() =>
    this._sessions().find(s => s.id === this._activeId()) ?? null
  );

  constructor() {
    const existing = this._sessions();
    if (existing.length === 0) {
      const id = this.createSessionEntry();
      this._activeId.set(id);
    } else {
      this._activeId.set(existing[0].id);
      this._messages.set([...existing[0].messages]);
    }
    // Attempt to sync from backend (silently fails offline)
    this.syncFromBackend();
  }

  // ── Public API ───────────────────────────────────────────────────────────────

  /** Create a new empty session and make it active. Returns new session id. */
  newSession(): string {
    this.flushCurrentMessages();
    const id = this.createSessionEntry();
    this._activeId.set(id);
    this._messages.set([]);
    this.persist();
    this.syncCreateSession(id, 'New Chat');
    return id;
  }

  /** Switch to an existing session by id. */
  switchSession(id: string): void {
    if (id === this._activeId()) return;
    const session = this._sessions().find(s => s.id === id);
    if (!session) return;
    this.flushCurrentMessages();
    this._activeId.set(id);
    this._messages.set([...session.messages]);
  }

  /** Delete a session. If active, switches to the next available one. */
  deleteSession(id: string): void {
    const sessions = this._sessions();
    const idx = sessions.findIndex(s => s.id === id);
    if (idx === -1) return;

    const updated = sessions.filter(s => s.id !== id);
    // Async backend delete — fire and forget
    this.syncDeleteSession(id);

    if (updated.length === 0) {
      this._sessions.set([]);
      this._messages.set([]);
      const newId = this.createSessionEntry();
      this._activeId.set(newId);
      this.persist();
      this.syncCreateSession(newId, 'New Chat');
      return;
    }

    this._sessions.set(updated);
    if (this._activeId() === id) {
      const next = updated[Math.min(idx, updated.length - 1)];
      this._activeId.set(next.id);
      this._messages.set([...next.messages]);
    }
    this.persist();
  }

  /** Append a message to the active session's live message list. */
  addMessage(message: ChatMessage): void {
    this._messages.update(m => [...m, message]);
  }

  /**
   * Update the last message in the active session.
   * Called frequently during streaming — does NOT write to localStorage.
   */
  refreshLastMessage(updater: (msg: ChatMessage) => ChatMessage): void {
    this._messages.update(msgs => {
      if (msgs.length === 0) return msgs;
      const copy = [...msgs];
      copy[copy.length - 1] = updater(copy[copy.length - 1]);
      return copy;
    });
  }

  /**
   * Flush live messages to the session store and persist to localStorage.
   * Call once when a stream completes.
   */
  finalizeMessage(): void {
    const msgs = this._messages();

    // Auto-generate title from the first user message
    const assistantCount = msgs.filter(m => m.role === 'assistant' && !m.streaming).length;
    if (assistantCount === 1) {
      const userMsg = msgs.find(m => m.role === 'user');
      if (userMsg) {
        const title = userMsg.text.slice(0, 30);
        const id = this._activeId();
        this._sessions.update(sessions =>
          sessions.map(s => s.id === id ? { ...s, title } : s)
        );
      }
    }

    this.flushCurrentMessages();
    this.persist();

    // Sync the last assistant message to the backend
    const lastAssistant = [...msgs].reverse().find(m => m.role === 'assistant' && !m.streaming);
    if (lastAssistant) {
      this.syncAppendMessage(this._activeId(), lastAssistant);
    }
  }

  /**
   * Update any message in the active session by id.
   * Useful for UI-only state changes (e.g. toggling sources panel).
   */
  updateMessage(id: string, updater: (msg: ChatMessage) => ChatMessage): void {
    this._messages.update(msgs => msgs.map(m => m.id === id ? updater(m) : m));
  }

  /** Manually update a session title. */
  setTitle(id: string, title: string): void {
    this._sessions.update(sessions =>
      sessions.map(s => s.id === id ? { ...s, title } : s)
    );
    this.persist();
  }

  // ── Private helpers ──────────────────────────────────────────────────────────

  private createSessionEntry(): string {
    const session: ChatSession = {
      id:        crypto.randomUUID(),
      title:     'New Chat',
      createdAt: Date.now(),
      messages:  []
    };
    this._sessions.update(sessions => [session, ...sessions].slice(0, MAX_SESSIONS));
    return session.id;
  }

  /** Copy current live messages (excluding in-flight streaming) back into _sessions. */
  private flushCurrentMessages(): void {
    const id = this._activeId();
    if (!id) return;
    const msgs = this._messages().filter(m => !m.streaming);
    this._sessions.update(sessions =>
      sessions.map(s => s.id === id ? { ...s, messages: msgs } : s)
    );
  }

  private loadFromStorage(): ChatSession[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return [];
      const sessions: ChatSession[] = JSON.parse(raw);
      // Drop any streaming messages left over from a crash
      return sessions.map(s => ({
        ...s,
        messages: s.messages.filter((m: ChatMessage) => !m.streaming)
      }));
    } catch {
      return [];
    }
  }

  private persist(): void {
    try {
      const toSave = this._sessions().map(s => ({
        ...s,
        messages: s.messages.filter(m => !m.streaming)
      }));
      localStorage.setItem(STORAGE_KEY, JSON.stringify(toSave));
    } catch {
      // localStorage may be unavailable (private browsing, storage quota exceeded)
    }
  }

  // ── Backend sync (fire-and-forget, localStorage is the fallback) ─────────────

  private syncFromBackend(): void {
    firstValueFrom(this.api.listChatSessions()).then(remoteSessions => {
      if (!remoteSessions || remoteSessions.length === 0) return;

      const local = this._sessions();
      // Merge: remote is source-of-truth for metadata; preserve local messages for sessions not yet in remote
      const merged: ChatSession[] = remoteSessions.map(r => {
        const existing = local.find(s => s.id === r.sessionId);
        return {
          id:        r.sessionId,
          title:     r.title,
          createdAt: new Date(r.createdAt).getTime(),
          messages:  existing?.messages ?? []
        };
      });
      this._sessions.set(merged);
      if (merged.length > 0 && !merged.find(s => s.id === this._activeId())) {
        this._activeId.set(merged[0].id);
        this._messages.set([...merged[0].messages]);
      }
      this.persist();
    }).catch(() => { /* offline — localStorage remains */ });
  }

  private syncCreateSession(localId: string, title: string): void {
    // The local UUID is already used as the session id; backend creates with a new id.
    // We record the backend id in local storage once known.
    firstValueFrom(this.api.createChatSession(title)).then(r => {
      // Update local session id to match backend id
      this._sessions.update(sessions =>
        sessions.map(s => s.id === localId ? { ...s, id: r.sessionId, title: r.title } : s)
      );
      if (this._activeId() === localId) this._activeId.set(r.sessionId);
      this.persist();
    }).catch(() => { /* offline — local id stays */ });
  }

  private syncDeleteSession(id: string): void {
    firstValueFrom(this.api.deleteChatSession(id)).catch(() => { /* offline */ });
  }

  private syncAppendMessage(sessionId: string, msg: ChatMessage): void {
    firstValueFrom(this.api.appendChatMessage(sessionId, {
      role:            msg.role,
      content:         msg.text,
      confidence:      msg.confidence,
      isHallucination: msg.isHallucination ?? false,
      sources:         msg.sources?.map(s => ({
        documentName: s.documentName,
        chunkContent: s.chunkContent,
        similarity:   s.similarity,
        chunkId:      s.chunkId,
        documentId:   s.documentId
      }))
    })).catch(() => { /* offline */ });
  }
}
