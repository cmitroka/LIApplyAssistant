import { Routes } from '@angular/router';
import { JobGridComponent } from './pages/job-grid/job-grid.component';
import { PasteParseComponent } from './pages/paste-parse/paste-parse.component';

export const routes: Routes = [
  { path: '', component: PasteParseComponent },
  { path: 'jobs', component: JobGridComponent },
  { path: '**', redirectTo: '' },
];
