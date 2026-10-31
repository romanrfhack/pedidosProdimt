import { Component, EventEmitter, Input, Output } from '@angular/core';

@Component({
  selector: 'app-customer-working-week',
  template: `
    <p class="week-help">Marca los días que trabaja. Sin marcar = descanso.</p>
    <div class="weekdays" role="group" aria-label="Días de trabajo">
      @for (day of weekdays; track day.value) {
        <label class="weekday" [class.working]="!restDays.includes(day.value)" [attr.title]="day.label + (restDays.includes(day.value) ? ': descansa' : ': trabaja')">
          <input type="checkbox" [attr.aria-label]="day.label" [checked]="!restDays.includes(day.value)" [disabled]="disabled" (change)="toggle(day.value)">
          <span class="short-day" aria-hidden="true">{{ day.short }}</span><span class="full-day" aria-hidden="true">{{ day.label }}</span>
        </label>
      }
    </div>
  `,
  styles: [`
    :host { display:block; container-type:inline-size; }
    .week-help { color:#536b77; font-size:.77rem; line-height:1.45; margin:0 0 10px; }
    .weekdays { display:grid; grid-template-columns:repeat(7,minmax(0,1fr)); gap:5px; }
    .weekday { display:flex; flex-direction:column; align-items:center; justify-content:center; gap:6px; min-width:0; min-height:62px; padding:8px 2px; cursor:pointer; border:1px solid #dce3e9; border-radius:9px; background:#f1f4f7; color:#6d7d88; font-size:.73rem; }
    .weekday.working { background:#edf7f2; border-color:#c6e2d3; color:#225c47; }
    .weekday:focus-within { outline:2px solid #246c70; outline-offset:2px; }
    .weekday input { width:18px; height:18px; min-height:18px; padding:0; margin:0; accent-color:#276c55; cursor:pointer; }
    .weekday:has(input:disabled) { cursor:wait; opacity:.7; }
    .full-day { display:none; }
    @container (min-width:530px) { .short-day { display:none; } .full-day { display:inline; } }
  `]
})
export class CustomerWorkingWeekComponent {
  @Input() restDays: number[] = [];
  @Input() disabled = false;
  @Output() restDaysChange = new EventEmitter<number[]>();
  protected readonly weekdays = [
    { value:1, label:'Lunes', short:'Lun' }, { value:2, label:'Martes', short:'Mar' },
    { value:3, label:'Miércoles', short:'Mié' }, { value:4, label:'Jueves', short:'Jue' },
    { value:5, label:'Viernes', short:'Vie' }, { value:6, label:'Sábado', short:'Sáb' },
    { value:7, label:'Domingo', short:'Dom' }
  ];

  protected toggle(day: number): void {
    if (this.disabled) return;
    this.restDaysChange.emit(this.restDays.includes(day)
      ? this.restDays.filter(value => value !== day)
      : [...this.restDays, day].sort((a,b) => a-b));
  }
}
