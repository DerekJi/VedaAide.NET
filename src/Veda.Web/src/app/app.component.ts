import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { MsalModule } from '@azure/msal-angular';
import { AuthService } from './services/auth.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MsalModule],
  template: `
    <div class="shell">
      <nav class="sidebar">
        <div class="logo">
          <img class="logo-icon" src="logo.svg" alt="VedaAide logo" width="32" height="32">
          <span class="logo-text">VedaAide</span>
        </div>
        <ul class="nav-links">
          <li>
            <a routerLink="/chat" routerLinkActive="active">
              <span class="nav-icon">💬</span> Chat
            </a>
          </li>
          <li>
            <a routerLink="/documents" routerLinkActive="active">
              <span class="nav-icon">📂</span> Documents
            </a>
          </li>
          <li>
            <a routerLink="/ingest" routerLinkActive="active">
              <span class="nav-icon">📥</span> Ingest
            </a>
          </li>
          @if (auth.isAdmin()) {
          <li>
            <a routerLink="/prompts" routerLinkActive="active">
              <span class="nav-icon">✏️</span> Prompts
            </a>
          </li>
          <li>
            <a routerLink="/evaluation" routerLinkActive="active">
              <span class="nav-icon">📊</span> Evaluation
            </a>
          </li>
          }
          <li>
            <a routerLink="/usage" routerLinkActive="active">
              <span class="nav-icon">🔢</span> Usage
            </a>
          </li>
        </ul>
        <div class="user-panel">
          @if (auth.isLoggedIn()) {
            <div class="user-info">
              <span class="user-name" title="{{ auth.userEmail() }}">{{ auth.userName() }}</span>
            </div>
            <button class="btn-logout" (click)="auth.logout()">退出登录</button>
          } @else {
            <button class="btn-login" (click)="auth.login()">
              <span>🔑</span> 登录
            </button>
          }
        </div>
      </nav>
      <main class="content">
        <router-outlet />
      </main>
    </div>
  `,
  styleUrl: './app.component.scss'
})
export class AppComponent {
  readonly auth = inject(AuthService);
}

