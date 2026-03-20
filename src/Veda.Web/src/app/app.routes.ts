import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'documents', pathMatch: 'full' },
  {
    path: 'documents',
    loadComponent: () => import('./pages/documents/documents.component').then(m => m.DocumentsComponent),
    title: 'VedaAide – Documents'
  },
  {
    path: 'chat',
    loadComponent: () => import('./pages/chat/chat.component').then(m => m.ChatComponent),
    title: 'VedaAide – Chat'
  },
  {
    path: 'prompts',
    loadComponent: () => import('./pages/prompts/prompts.component').then(m => m.PromptsComponent),
    title: 'VedaAide – Prompts'
  },
  { path: '**', redirectTo: 'documents' }
];
