import { Component, computed, inject, Output, EventEmitter, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ChatSession } from '../../models';
import { ChatSessionService } from '../../../services/chat-session.service';

interface SessionGroup {
  label: string;
  sessions: ChatSession[];
}

@Component({
  selector: 'app-chat-sidebar',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './chat-sidebar.component.html',
  styleUrl: './chat-sidebar.component.scss'
})
export class ChatSidebarComponent {
  /** Emitted before any session switch/create/delete so the parent can abort an active stream. */
  @Output() aboutToSwitch = new EventEmitter<void>();

  protected readonly chatSession = inject(ChatSessionService);

  readonly collapsed = signal<boolean>(this.loadCollapsed());
  readonly searchQuery = signal<string>('');

  /** Id of the session whose title is being edited. */
  readonly editingId = signal<string | null>(null);
  readonly editingTitle = signal<string>('');

  readonly groupedSessions = computed<SessionGroup[]>(() => {
    const q = this.searchQuery().toLowerCase().trim();
    const sessions = q
      ? this.chatSession.sessions().filter(s => s.title.toLowerCase().includes(q))
      : this.chatSession.sessions();
    const now      = Date.now();
    const todayStart     = new Date(new Date().setHours(0, 0, 0, 0)).getTime();
    const yesterdayStart = todayStart - 86_400_000;

    const groups: SessionGroup[] = [];
    const today: ChatSession[]     = [];
    const yesterday: ChatSession[] = [];
    const older: ChatSession[]     = [];

    for (const s of sessions) {
      if (s.createdAt >= todayStart)     today.push(s);
      else if (s.createdAt >= yesterdayStart) yesterday.push(s);
      else                               older.push(s);
    }

    if (today.length)     groups.push({ label: '今天',   sessions: today });
    if (yesterday.length) groups.push({ label: '昨天',   sessions: yesterday });
    if (older.length)     groups.push({ label: '更早',   sessions: older });

    return groups;
  });

  // ── Actions ──────────────────────────────────────────────────────────────────

  newSession(): void {
    this.aboutToSwitch.emit();
    this.chatSession.newSession();
  }

  switchSession(id: string): void {
    if (id === this.chatSession.activeId()) return;
    this.aboutToSwitch.emit();
    this.chatSession.switchSession(id);
  }

  deleteSession(id: string, event: MouseEvent): void {
    event.stopPropagation();
    this.aboutToSwitch.emit();
    this.chatSession.deleteSession(id);
  }

  toggleCollapse(): void {
    this.collapsed.update(v => !v);
    try {
      localStorage.setItem('veda_sidebar_collapsed', String(this.collapsed()));
    } catch { /* storage unavailable */ }
  }

  startEdit(s: ChatSession, event: MouseEvent): void {
    event.stopPropagation();
    this.editingId.set(s.id);
    this.editingTitle.set(s.title);
  }

  confirmEdit(id: string): void {
    const t = this.editingTitle().trim();
    if (t) this.chatSession.setTitle(id, t);
    this.editingId.set(null);
  }

  cancelEdit(): void {
    this.editingId.set(null);
  }

  onEditKeyDown(event: KeyboardEvent, id: string): void {
    if (event.key === 'Enter')  { event.preventDefault(); this.confirmEdit(id); }
    if (event.key === 'Escape') { this.cancelEdit(); }
  }

  private loadCollapsed(): boolean {
    try {
      return localStorage.getItem('veda_sidebar_collapsed') === 'true';
    } catch {
      return false;
    }
  }
}
