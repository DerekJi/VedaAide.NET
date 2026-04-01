import { Component, computed, inject, Output, EventEmitter, signal } from '@angular/core';
import { ChatSession } from '../../models';
import { ChatSessionService } from '../../../services/chat-session.service';

interface SessionGroup {
  label: string;
  sessions: ChatSession[];
}

@Component({
  selector: 'app-chat-sidebar',
  standalone: true,
  imports: [],
  templateUrl: './chat-sidebar.component.html',
  styleUrl: './chat-sidebar.component.scss'
})
export class ChatSidebarComponent {
  /** Emitted before any session switch/create/delete so the parent can abort an active stream. */
  @Output() aboutToSwitch = new EventEmitter<void>();

  protected readonly chatSession = inject(ChatSessionService);

  readonly collapsed = signal<boolean>(this.loadCollapsed());

  readonly groupedSessions = computed<SessionGroup[]>(() => {
    const sessions = this.chatSession.sessions();
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

  private loadCollapsed(): boolean {
    try {
      return localStorage.getItem('veda_sidebar_collapsed') === 'true';
    } catch {
      return false;
    }
  }
}
