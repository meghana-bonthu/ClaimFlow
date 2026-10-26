import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/auth.service';
import { ShellComponent } from './shell.component';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./pages/login.component').then((m) => m.LoginComponent), title: 'Sign in · ClaimFlow' },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', loadComponent: () => import('./pages/dashboard.component').then((m) => m.DashboardComponent), title: 'Dashboard · ClaimFlow' },
      { path: 'claims', loadComponent: () => import('./pages/claims-list.component').then((m) => m.ClaimsListComponent), title: 'Claims · ClaimFlow' },
      {
        path: 'claims/new',
        canActivate: [roleGuard('Submitter', 'Admin')],
        loadComponent: () => import('./pages/submit-claim.component').then((m) => m.SubmitClaimComponent),
        title: 'New claim · ClaimFlow'
      },
      {
        path: 'claims/upload',
        canActivate: [roleGuard('Submitter', 'Admin')],
        loadComponent: () => import('./pages/upload-x12.component').then((m) => m.UploadX12Component),
        title: 'Upload 837 · ClaimFlow'
      },
      { path: 'claims/:id', loadComponent: () => import('./pages/claim-detail.component').then((m) => m.ClaimDetailComponent), title: 'Claim · ClaimFlow' },
      { path: 'reference', loadComponent: () => import('./pages/reference.component').then((m) => m.ReferenceComponent), title: 'Reference data · ClaimFlow' }
    ]
  },
  { path: '**', redirectTo: '' }
];
