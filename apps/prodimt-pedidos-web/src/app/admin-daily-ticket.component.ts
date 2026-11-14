import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { catchError, forkJoin, map, Observable, of, startWith, switchMap } from 'rxjs';
import { priceText } from './commercial-catalog-api.service';
import { buildDailyTicket, isDailyTicketOrder } from './daily-ticket';
import { unitLabel } from './operational-labels';
import { OrderDataService } from './order-data.service';

type TicketState = {
  date: string | null;
  ticket: ReturnType<typeof buildDailyTicket> | null;
  error: string | null;
};

@Component({
  selector: 'app-admin-daily-ticket',
  imports: [RouterLink, FormsModule],
  templateUrl: './admin-daily-ticket.component.html',
  styleUrl: './admin-daily-ticket.component.css'
})
export class AdminDailyTicketComponent {
  private readonly orders = inject(OrderDataService);
  protected readonly state = signal<TicketState | null>(null);
  protected readonly priceText = priceText;
  protected readonly unitLabel = unitLabel;
  protected readonly paperWidth = signal<58 | 80>(80);
  protected readonly priorBalanceInput = signal('');
  protected readonly priorBalance = computed(() => {
    const value = this.priorBalanceInput().trim();
    if (!/^\d+(?:[.,]\d{1,2})?$/.test(value)) return null;
    const cents = Math.round(Number(value.replace(',', '.')) * 100);
    return Number.isSafeInteger(cents) ? cents / 100 : null;
  });
  protected readonly invalidPriorBalance = computed(() => !!this.priorBalanceInput().trim() && this.priorBalance() === null);
  protected readonly total = computed(() => {
    const consumption = this.state()?.ticket?.consumption;
    const prior = this.priorBalance();
    if (consumption == null || prior === null) return null;
    const cents = Math.round(consumption * 100) + Math.round(prior * 100);
    return Number.isSafeInteger(cents) ? cents / 100 : null;
  });
  protected readonly quantity = new Intl.NumberFormat('es-MX', { maximumFractionDigits: 3 });
  private readonly dateFormat = new Intl.DateTimeFormat('es-MX', {
    timeZone: 'UTC', weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric'
  });

  protected formatDate(date: string): string {
    return this.dateFormat.format(new Date(date + 'T12:00:00Z'));
  }

  constructor() {
    inject(ActivatedRoute).paramMap.pipe(
      switchMap(params => {
        this.priorBalanceInput.set('');
        return this.load(params.get('orderId') ?? '').pipe(startWith(null));
      }),
      takeUntilDestroyed()
    ).subscribe(state => this.state.set(state));
  }

  protected print(): void {
    if (this.state()?.ticket && !this.invalidPriorBalance()) window.print();
  }

  private load(id: string): Observable<TicketState> {
    return this.orders.loadOrderDetail(id).pipe(
      switchMap(selected => {
        const date = selected.orderDate;
        if (!isDailyTicketOrder(selected)) {
          return of({ date, ticket: null, error: 'El ticket está disponible para pedidos de clientes registrados o aceptados.' });
        }
        return this.orders.loadTodayOrders(date).pipe(
          switchMap(summaries => {
            const others = summaries.filter(order => order.id !== id && order.customerId === selected.customerId
              && order.orderDate === date && isDailyTicketOrder(order));
            return others.length
              ? forkJoin([of(selected), ...others.map(order => this.orders.loadOrderDetail(order.id))])
              : of([selected]);
          }),
          map(details => {
            const ticket = buildDailyTicket(selected, details);
            return ticket.lines.length ? { date, ticket, error: null }
              : { date, ticket: null, error: 'Este cliente no tiene productos pedidos para la fecha de servicio.' };
          }),
          catchError(() => of({ date, ticket: null, error: 'No se pudieron cargar todos los pedidos del cliente para esa fecha.' }))
        );
      }),
      catchError(() => of({ date: null, ticket: null, error: 'No se pudo consultar el pedido.' }))
    );
  }
}
