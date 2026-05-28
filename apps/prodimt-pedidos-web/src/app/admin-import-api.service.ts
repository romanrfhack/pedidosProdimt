import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

export interface ImportTemplatesApiResponse {
  maxFileSizeBytes: number;
  mode: string;
  templates: ImportTemplateApiResponse[];
}

export interface ImportTemplateApiResponse {
  importType: string;
  description: string;
  columns: ImportColumnApiResponse[];
  templatePath: string;
  examplePath: string;
}

export interface ImportColumnApiResponse {
  name: string;
  required: boolean;
  description: string;
}

export interface ImportCsvApiRequest {
  content: string;
  fileName: string | null;
}

export interface ImportValidationApiResponse {
  importType: string;
  totalRows: number;
  validRows: number;
  errorCount: number;
  warningCount: number;
  proposedCreateCount: number;
  proposedUpdateCount: number;
  proposedDeactivateCount: number;
  errors: ImportIssueApiResponse[];
  warnings: ImportIssueApiResponse[];
  proposedChanges: ImportProposedChangeApiResponse[];
}

export interface ImportIssueApiResponse {
  rowNumber: number;
  field: string | null;
  code: string;
  message: string;
  rawValue: string | null;
}

export interface ImportProposedChangeApiResponse {
  rowNumber: number;
  action: string;
  entityType: string;
  entityId: string | null;
  entityDisplayName: string;
  summary: string;
}

export interface ImportApplyApiResponse {
  importType: string;
  totalRows: number;
  createdCount: number;
  updatedCount: number;
  skippedCount: number;
  warningCount: number;
  auditLogIds: string[];
  errors: ImportIssueApiResponse[];
}

@Injectable({ providedIn: 'root' })
export class AdminImportApiService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  getTemplates(): Observable<ImportTemplatesApiResponse> {
    return this.http.get<ImportTemplatesApiResponse>(`${this.apiBaseUrl}/api/admin/import/templates`);
  }

  validate(importType: string, request: ImportCsvApiRequest): Observable<ImportValidationApiResponse> {
    return this.http.post<ImportValidationApiResponse>(
      `${this.apiBaseUrl}/api/admin/import/${importType}/validate`,
      request
    );
  }

  apply(importType: string, request: ImportCsvApiRequest): Observable<ImportApplyApiResponse> {
    return this.http.post<ImportApplyApiResponse>(
      `${this.apiBaseUrl}/api/admin/import/${importType}/apply`,
      request
    );
  }
}
