import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'chat', pathMatch: 'full' },
  {
    path: 'ingest',
    loadComponent: () => import('./pages/ingest/ingest.component').then(m => m.IngestComponent),
    title: 'VedaAide – Ingest'
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
  {
    path: 'evaluation',
    loadComponent: () => import('./pages/evaluation/evaluation.component').then(m => m.EvaluationComponent),
    title: 'VedaAide – Evaluation'
  },
  { path: '**', redirectTo: 'chat' }
];
