import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ImportResponse, JobDto, ParseResponse } from '../models/job';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiBaseUrl.replace(/\/$/, '');

  parse(text: string): Observable<ParseResponse> {
    return this.http.post<ParseResponse>(
      `${this.baseUrl}/api/parse`,
      { text },
      { headers: this.headers() },
    );
  }

  importJobs(jobs: JobDto[]): Observable<ImportResponse> {
    return this.http.post<ImportResponse>(
      `${this.baseUrl}/api/jobs/import`,
      { jobs },
      { headers: this.headers() },
    );
  }

  private headers(): HttpHeaders {
    let headers = new HttpHeaders({ 'Content-Type': 'application/json' });
    if (environment.apiKey) {
      headers = headers.set('X-Api-Key', environment.apiKey);
    }
    return headers;
  }
}
