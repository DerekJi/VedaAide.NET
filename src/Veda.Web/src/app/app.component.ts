import { Component } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell">
      <nav class="sidebar">
        <div class="logo">
          <span class="logo-icon">⚡</span>
          <span class="logo-text">VedaAide</span>
        </div>
        <ul class="nav-links">
          <li>
            <a routerLink="/chat" routerLinkActive="active">
              <span class="nav-icon">💬</span> Chat
            </a>
          </li>
          <li>
            <a routerLink="/ingest" routerLinkActive="active">
              <span class="nav-icon">📥</span> Ingest
            </a>
          </li>
          <li>
            <a routerLink="/prompts" routerLinkActive="active">
              <span class="nav-icon">✏️</span> Prompts
            </a>
          </li>
        </ul>
      </nav>
      <main class="content">
        <router-outlet />
      </main>
    </div>
  `,
  styleUrl: './app.component.scss'
})
export class AppComponent {}
