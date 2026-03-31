import { Routes } from '@angular/router';
import { MsalGuard } from '@azure/msal-angular';

export const routes: Routes = [
  { path: '', redirectTo: 'chat', pathMatch: 'full' },
  {
    path: 'ingest',
    canActivate: [MsalGuard],
    loadComponent: () => import('./pages/ingest/ingest.component').then(m => m.IngestComponent),
    title: 'VedaAide – Ingest'
  },
  {
    path: 'chat',
    canActivate: [MsalGuard],
    loadComponent: () => import('./pages/chat/chat.component').then(m => m.ChatComponent),
    title: 'VedaAide – Chat'
  },
  {
    path: 'documents',
    canActivate: [MsalGuard],
    loadComponent: () => import('./pages/documents/documents.component').then(m => m.DocumentsComponent),
    title: 'VedaAide – Documents'
  },
  {
    path: 'prompts',
    canActivate: [MsalGuard],
    loadComponent: () => import('./pages/prompts/prompts.component').then(m => m.PromptsComponent),
    title: 'VedaAide – Prompts'
  },
  {
    path: 'evaluation',
    canActivate: [MsalGuard],
    loadComponent: () => import('./pages/evaluation/evaluation.component').then(m => m.EvaluationComponent),
    title: 'VedaAide – Evaluation'
  },
  {
    path: 'usage',
    canActivate: [MsalGuard],
    loadComponent: () => import('./pages/usage/usage.component').then(m => m.UsageComponent),
    title: 'VedaAide – Usage'
  },
  { path: '**', redirectTo: 'chat' }
];
