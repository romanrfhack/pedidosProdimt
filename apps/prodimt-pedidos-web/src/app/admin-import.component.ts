import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AdminImportApiService,
  ImportApplyApiResponse,
  ImportIssueApiResponse,
  ImportTemplateApiResponse,
  ImportValidationApiResponse
} from './admin-import-api.service';

@Component({
  selector: 'app-admin-import',
  imports: [FormsModule],
  template: `
    <section class="page stack" data-testid="admin-import">
      <div class="page-title">
        <p class="eyebrow">Admin</p>
        <h2>Importacion</h2>
      </div>

      @if (isLoadingTemplates) {
        <div class="notice">Cargando plantillas...</div>
      }

      @if (errorMessage) {
        <div class="alert error">{{ errorMessage }}</div>
      }

      @if (resultMessage) {
        <div class="alert success">{{ resultMessage }}</div>
      }

      @if (!isLoadingTemplates) {
        <section class="detail-panel stack">
          <div class="catalog-grid">
            <label>
              <span>Tipo</span>
              <select name="importType" [(ngModel)]="selectedImportType" (ngModelChange)="clearResults()">
                @for (template of templates; track template.importType) {
                  <option [value]="template.importType">{{ labelFor(template.importType) }}</option>
                }
              </select>
            </label>
            <label>
              <span>Archivo CSV</span>
              <input name="csvFile" type="file" accept=".csv,text/csv" (change)="onFileSelected($event)">
            </label>
          </div>

          @if (selectedTemplate()) {
            <div class="notice">
              <strong>{{ selectedTemplate()?.description }}</strong>
              <small>Plantilla: {{ selectedTemplate()?.templatePath }}</small>
              <small>Ejemplo: {{ selectedTemplate()?.examplePath }}</small>
            </div>
          }

          <label>
            <span>Contenido CSV</span>
            <textarea name="csvContent" rows="10" [(ngModel)]="csvContent" (ngModelChange)="clearResults()"></textarea>
          </label>

          <div class="review-actions">
            <button type="button" class="primary compact" [disabled]="isBusy || !csvContent.trim()" (click)="validate()">
              Validar
            </button>
            <button
              type="button"
              class="primary compact"
              [disabled]="isBusy || !validation || validation.errorCount > 0"
              (click)="apply()">
              Aplicar importacion
            </button>
          </div>
        </section>

        @if (validation) {
          <section class="detail-panel stack" data-testid="import-validation">
            <strong>Resultado de validacion</strong>
            <div class="import-summary">
              <span>Total: {{ validation.totalRows }}</span>
              <span>Validas: {{ validation.validRows }}</span>
              <span>Errores: {{ validation.errorCount }}</span>
              <span>Advertencias: {{ validation.warningCount }}</span>
              <span>Crear: {{ validation.proposedCreateCount }}</span>
              <span>Actualizar: {{ validation.proposedUpdateCount }}</span>
              <span>Desactivar: {{ validation.proposedDeactivateCount }}</span>
            </div>

            @if (validation.errors.length > 0) {
              <div class="stack" data-testid="import-errors">
                <strong>Errores</strong>
                @for (issue of validation.errors; track issueKey(issue)) {
                  <div class="alert error">{{ formatIssue(issue) }}</div>
                }
              </div>
            }

            @if (validation.warnings.length > 0) {
              <div class="stack" data-testid="import-warnings">
                <strong>Advertencias</strong>
                @for (issue of validation.warnings; track issueKey(issue)) {
                  <div class="alert warning">{{ formatIssue(issue) }}</div>
                }
              </div>
            }

            @if (validation.proposedChanges.length > 0) {
              <div class="line-list" data-testid="import-proposed">
                <strong>Cambios propuestos</strong>
                @for (change of validation.proposedChanges; track change.rowNumber + change.entityDisplayName + change.action) {
                  <div class="line-row">
                    <span>
                      <strong>{{ change.action }} · {{ change.entityDisplayName }}</strong>
                      <small>Fila {{ change.rowNumber }} · {{ change.summary }}</small>
                    </span>
                  </div>
                }
              </div>
            }
          </section>
        }

        @if (applyResult) {
          <section class="detail-panel stack" data-testid="import-apply-result">
            <strong>Importacion aplicada</strong>
            <div class="import-summary">
              <span>Total: {{ applyResult.totalRows }}</span>
              <span>Creados: {{ applyResult.createdCount }}</span>
              <span>Actualizados: {{ applyResult.updatedCount }}</span>
              <span>Omitidos: {{ applyResult.skippedCount }}</span>
              <span>Auditoria: {{ applyResult.auditLogIds.length }}</span>
            </div>
          </section>
        }
      }
    </section>
  `
})
export class AdminImportComponent {
  private readonly api = inject(AdminImportApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);

  protected templates: ImportTemplateApiResponse[] = [
    {
      importType: 'customers',
      description: 'Clientes externos para piloto.',
      templatePath: 'docs/import-templates/customers.csv',
      examplePath: 'docs/import-templates/examples/customers-demo.csv',
      columns: []
    },
    {
      importType: 'products',
      description: 'Productos o moldes.',
      templatePath: 'docs/import-templates/products.csv',
      examplePath: 'docs/import-templates/examples/products-demo.csv',
      columns: []
    },
    {
      importType: 'customer-frequent-products',
      description: 'Productos frecuentes por cliente.',
      templatePath: 'docs/import-templates/customer-frequent-products.csv',
      examplePath: 'docs/import-templates/examples/customer-frequent-products-demo.csv',
      columns: []
    },
    {
      importType: 'machines',
      description: 'Maquinas internas.',
      templatePath: 'docs/import-templates/machines.csv',
      examplePath: 'docs/import-templates/examples/machines-demo.csv',
      columns: []
    },
    {
      importType: 'customer-machine-assignments',
      description: 'Asignaciones internas cliente-maquina.',
      templatePath: 'docs/import-templates/customer-machine-assignments.csv',
      examplePath: 'docs/import-templates/examples/customer-machine-assignments-demo.csv',
      columns: []
    }
  ];
  protected selectedImportType = 'customers';
  protected csvContent = '';
  protected fileName: string | null = null;
  protected validation: ImportValidationApiResponse | null = null;
  protected applyResult: ImportApplyApiResponse | null = null;
  protected isLoadingTemplates = false;
  protected isBusy = false;
  protected errorMessage: string | null = null;
  protected resultMessage: string | null = null;

  constructor() {
    this.api.getTemplates().subscribe({
      next: (response) => {
        this.templates = response.templates;
        if (!response.templates.some((template) => template.importType === this.selectedImportType)) {
          this.selectedImportType = response.templates[0]?.importType ?? 'customers';
        }
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.showError(error, 'No se pudieron cargar las plantillas.');
      }
    });
  }

  protected selectedTemplate(): ImportTemplateApiResponse | null {
    return this.templates.find((template) => template.importType === this.selectedImportType) ?? null;
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    this.fileName = file.name;
    const reader = new FileReader();
    reader.onload = () => {
      this.csvContent = String(reader.result ?? '');
      this.clearResults();
      this.changeDetector.detectChanges();
    };
    reader.onerror = () => {
      this.errorMessage = 'No se pudo leer el archivo CSV.';
    };
    reader.readAsText(file);
  }

  protected validate(): void {
    this.isBusy = true;
    this.errorMessage = null;
    this.resultMessage = null;
    this.applyResult = null;

    this.api.validate(this.selectedImportType, this.request()).subscribe({
      next: (response) => {
        this.validation = response;
        this.isBusy = false;
        this.resultMessage = response.errorCount === 0
          ? 'CSV validado sin errores bloqueantes.'
          : 'CSV validado con errores.';
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.isBusy = false;
        this.showError(error, 'No se pudo validar el CSV.');
      }
    });
  }

  protected apply(): void {
    if (!this.validation || this.validation.errorCount > 0) {
      return;
    }

    this.isBusy = true;
    this.errorMessage = null;
    this.resultMessage = null;

    this.api.apply(this.selectedImportType, this.request()).subscribe({
      next: (response) => {
        this.applyResult = response;
        this.isBusy = false;
        this.resultMessage = 'Importacion aplicada.';
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        this.isBusy = false;
        this.showError(error, 'No se pudo aplicar la importacion.');
      }
    });
  }

  protected clearResults(): void {
    this.validation = null;
    this.applyResult = null;
    this.resultMessage = null;
    this.errorMessage = null;
  }

  protected labelFor(importType: string): string {
    const labels: Record<string, string> = {
      customers: 'Clientes',
      products: 'Productos',
      'customer-frequent-products': 'Frecuentes',
      machines: 'Maquinas',
      'customer-machine-assignments': 'Asignaciones'
    };

    return labels[importType] ?? importType;
  }

  protected formatIssue(issue: ImportIssueApiResponse): string {
    const row = issue.rowNumber > 0 ? `Fila ${issue.rowNumber}` : 'Archivo';
    const field = issue.field ? ` · ${issue.field}` : '';
    return `${row}${field}: ${issue.message}`;
  }

  protected issueKey(issue: ImportIssueApiResponse): string {
    return `${issue.rowNumber}-${issue.field ?? ''}-${issue.code}-${issue.rawValue ?? ''}`;
  }

  private request() {
    return {
      content: this.csvContent,
      fileName: this.fileName
    };
  }

  private showError(error: unknown, fallback: string): void {
    if (error instanceof HttpErrorResponse && error.error?.error) {
      this.errorMessage = error.error.error;
      this.changeDetector.detectChanges();
      return;
    }

    if (error instanceof HttpErrorResponse && Array.isArray(error.error?.errors) && error.error.errors.length > 0) {
      this.errorMessage = error.error.errors[0].message ?? fallback;
      this.changeDetector.detectChanges();
      return;
    }

    this.errorMessage = fallback;
    this.changeDetector.detectChanges();
  }
}
