import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom, forkJoin, Observable } from 'rxjs';
import { AdminCatalogApiService, AdminCustomerAccessTokenApiResponse, AdminCustomerCatalogApiResponse,
  RevealedCustomerAccessTokenApiResponse } from './admin-catalog-api.service';
import { formatReminderDate, formatReminderExpiration, mexicoDateTimeToIso, normalizeReminderPhone, reminderDateTimeInput } from './customer-reminder';

/** State belongs to the customer configuration, so dismissing its dialog does not discard a draft. */
export class AccessTokenManager {
  private rows: AdminCustomerAccessTokenApiResponse[] = [];
  private epoch = 0;
  private action = 0;
  private read = 0;
  private destroyed = false;
  private serverNow: string | null = null;
  private clockMark = 0;
  private revealed: RevealedCustomerAccessTokenApiResponse | null = null;
  private popup: Window | null = null;
  private originalExpiry: string | null = null;
  description = 'Recuerda hacer tu próximo pedido';
  expiresAt: string | null = null;
  phone = '';
  editingId: string | null = null;
  editingVersion = 0;
  conflict = false;
  unavailable = false;
  saving = false;
  sharing = false;
  revoking = false;
  loading = false;
  error: string | null = null;
  message: string | null = null;
  copyFallback: string | null = null;
  deliveryDate: string | null = null;
  readonly formatDate = formatReminderDate;
  readonly formatExpiration = formatReminderExpiration;

  constructor(private readonly api: AdminCatalogApiService, private readonly document: Document,
    private readonly destroyRef: DestroyRef, private readonly customer: () => AdminCustomerCatalogApiResponse | null,
    private readonly changed: () => void) {
    destroyRef.onDestroy(() => { this.destroyed = true; this.epoch++; this.popup?.close(); this.revealed = null; this.copyFallback = null; });
  }

  get busy(): boolean { return this.saving || this.sharing || this.revoking; }
  get tokens(): AdminCustomerAccessTokenApiResponse[] {
    const now = this.currentServerTime();
    return this.rows.filter(token => token.isActive && token.isUsable !== false
      && (!token.expiresAt || (!!now && Date.parse(token.expiresAt) > now)));
  }
  get link(): string | null {
    return this.revealed && this.revealed.tokenId === this.editingId ? this.linkFor(this.revealed.plainToken) : null;
  }
  get plainToken(): string | null { return this.link ? this.revealed!.plainToken : null; }
  get selectedToken(): AdminCustomerAccessTokenApiResponse | null { return this.tokens.find(token => token.tokenId === this.editingId) ?? null; }
  get pendingChanges(): boolean {
    const token = this.selectedToken;
    const expiry = this.draftExpiry();
    const sameExpiry = !expiry && !token?.expiresAt || !!expiry && !!token?.expiresAt && Date.parse(expiry) === Date.parse(token.expiresAt);
    return !!token && (this.description.trim() !== token.description || !sameExpiry
      || normalizeReminderPhone(this.phone) !== normalizeReminderPhone(this.customer()?.phoneNumber ?? ''));
  }
  get preview(): string {
    return this.messageFor(this.description.trim() || 'Recuerda hacer tu próximo pedido',
      this.draftExpiry(), this.link || '{enlace de acceso}');
  }

  reset(): void {
    this.popup?.close(); this.popup = null;
    this.epoch++; this.action++; this.read++; this.rows = []; this.serverNow = null; this.deliveryDate = null;
    this.saving = this.sharing = this.revoking = this.loading = false;
    this.newDraft();
    void this.refresh();
  }

  newDraft(): void {
    if (this.busy) return;
    this.editingId = null; this.editingVersion = 0; this.conflict = this.unavailable = false;
    this.description = 'Recuerda hacer tu próximo pedido'; this.expiresAt = null;
    this.originalExpiry = null;
    this.phone = this.customer()?.phoneNumber ?? ''; this.revealed = null; this.copyFallback = null;
    this.error = this.message = null;
  }

  edit(token: AdminCustomerAccessTokenApiResponse): void {
    if (this.busy || !this.tokens.some(row => row.tokenId === token.tokenId)) return;
    this.editingId = token.tokenId; this.editingVersion = token.version;
    this.description = token.description; this.expiresAt = reminderDateTimeInput(token.expiresAt);
    this.originalExpiry = token.expiresAt;
    this.phone = this.customer()?.phoneNumber ?? ''; this.conflict = this.unavailable = false;
    this.error = this.message = this.copyFallback = null;
    if (this.revealed?.tokenId !== token.tokenId) this.revealed = null;
  }

  async reloadEditing(): Promise<void> {
    if (this.busy || !this.editingId) return;
    const id = this.editingId;
    if (await this.refresh()) {
      const token = this.tokens.find(row => row.tokenId === id);
      if (token) this.edit(token);
      else { this.unavailable = true; this.error = 'Este token ya no está vigente. Tu borrador se conserva; puedes crear un enlace nuevo.'; }
      this.changed();
    }
  }

  async refresh(): Promise<boolean> {
    const customer = this.customer();
    if (!customer || this.busy || this.destroyed) return false;
    const epoch = this.epoch; const read = ++this.read;
    this.loading = true;
    try {
      const result = await this.wait(forkJoin({ tokens: this.api.getAccessTokens(customer.id, true), metadata: this.api.getNextDeliveryDate() }));
      if (!this.current(customer.id, epoch) || read !== this.read) return false;
      this.setClock(result.metadata);
      this.rows = result.tokens;
      if (this.revealed && !this.tokens.some(token => token.tokenId === this.revealed?.tokenId)) this.revealed = null;
      this.unavailable = !!this.editingId && !this.tokens.some(token => token.tokenId === this.editingId);
      return true;
    } catch (error) {
      if (this.current(customer.id, epoch) && read === this.read) this.error = this.errorText(error, 'No se pudieron consultar los tokens activos.');
      return false;
    } finally {
      if (this.current(customer.id, epoch) && read === this.read) { this.loading = false; this.changed(); }
    }
  }

  async save(andShare = false): Promise<void> {
    const customer = this.customer();
    if (!customer || this.busy || this.conflict || this.unavailable) return;
    const phone = normalizeReminderPhone(this.phone);
    const expiresAt = this.draftExpiry();
    const description = this.description.trim();
    this.error = this.message = this.copyFallback = null;
    if (!phone) { this.error = 'Captura un teléfono de México de 10 dígitos o un número internacional con + y código de país.'; return; }
    if (!expiresAt) { this.error = 'Selecciona una fecha y hora válidas de vencimiento, en horario de Ciudad de México.'; return; }
    if (!description || description.length > 200) { this.error = 'Captura una descripción de 1 a 200 caracteres.'; return; }
    const popup = andShare ? this.reservePopup() : null;
    if (andShare && !popup) return;
    const epoch = this.epoch; const action = ++this.action; this.read++; this.loading = false;
    const editingId = this.editingId; const expectedVersion = this.editingVersion;
    this.saving = true;
    let saved = false;
    try {
      const metadata = await this.wait(this.api.getNextDeliveryDate());
      if (!this.current(customer.id, epoch, action)) return;
      this.setClock(metadata);
      if (Date.parse(expiresAt) <= Date.parse(metadata.serverNow)) throw new Error('El vencimiento debe ser posterior a la hora actual de Ciudad de México.');
      if (phone !== customer.phoneNumber) {
        await this.wait(this.api.updateCustomerPhone(customer.id, phone));
        if (!this.current(customer.id, epoch, action)) return;
        customer.phoneNumber = phone; this.customer()!.phoneNumber = phone; this.phone = phone;
      }
      const result = editingId
        ? await this.wait(this.api.updateAccessToken(customer.id, editingId, { expectedVersion, description, expiresAt }))
        : await this.wait(this.api.createAccessToken(customer.id, { description, expiresAt, reminderLink: true }));
      if (!this.current(customer.id, epoch, action)) return;
      saved = true;
      this.editingId = result.tokenId; this.editingVersion = result.version;
      this.description = result.description; this.expiresAt = reminderDateTimeInput(result.expiresAt);
      this.originalExpiry = result.expiresAt;
      if ('plainToken' in result) this.revealed = result;
      else if (this.revealed?.tokenId === result.tokenId) this.revealed = { ...this.revealed, ...result };
      this.upsert(result);
      this.message = editingId ? 'Cambios del token guardados. El token y su enlace se conservan.' : 'Enlace creado. Puedes volver a copiarlo o compartirlo mientras esté vigente.';
      if (popup) {
        const fresh = await this.wait(forkJoin({ token: this.api.revealAccessToken(customer.id, result.tokenId), metadata: this.api.getNextDeliveryDate() }));
        if (!this.current(customer.id, epoch, action)) return;
        if (fresh.token.version !== result.version) throw new Error('El token volvió a cambiar después del guardado. Revisa sus datos antes de compartirlo.');
        this.acceptReveal(fresh.token, fresh.metadata, customer.id, result.tokenId);
        popup.location.href = this.whatsAppUrl(phone, fresh.token);
        this.message = 'Cambios guardados. WhatsApp abierto con el mensaje preparado; confirma el envío desde WhatsApp.';
      }
    } catch (error) {
      popup?.close();
      if (this.current(customer.id, epoch, action)) {
        this.error = this.errorText(error, saved ? 'Los cambios se guardaron, pero no se pudo preparar WhatsApp.' : 'No se pudieron guardar los datos del token. Tu borrador se conserva.');
        if (!saved && editingId && error instanceof HttpErrorResponse && error.status === 409) this.conflict = true;
      }
    } finally {
      if (!this.current(customer.id, epoch, action)) popup?.close();
      else { this.saving = false; this.popup = null; this.changed(); if (saved) void this.refresh(); }
    }
  }

  async copy(token: AdminCustomerAccessTokenApiResponse, kind: 'token' | 'link' | 'message'): Promise<void> {
    await this.useToken(token, kind);
  }

  async share(token?: AdminCustomerAccessTokenApiResponse): Promise<void> {
    const target = token ?? this.tokens.find(row => row.tokenId === this.editingId);
    if (target) await this.useToken(target, 'whatsapp');
  }

  async revoke(token: AdminCustomerAccessTokenApiResponse): Promise<void> {
    const customer = this.customer();
    if (!customer || this.busy) return;
    const epoch = this.epoch; const action = ++this.action; this.read++; this.loading = false;
    this.revoking = true; this.error = this.message = this.copyFallback = null;
    try {
      await this.wait(this.api.revokeAccessToken(customer.id, token.tokenId));
      if (!this.current(customer.id, epoch, action)) return;
      this.rows = this.rows.filter(row => row.tokenId !== token.tokenId);
      if (this.revealed?.tokenId === token.tokenId) this.revealed = null;
      if (this.editingId === token.tokenId) this.unavailable = true;
      this.message = 'Token revocado. Ya no aparece entre los tokens activos.';
    } catch (error) {
      if (this.current(customer.id, epoch, action)) this.error = this.errorText(error, 'No se pudo revocar el token.');
    } finally {
      if (this.current(customer.id, epoch, action)) { this.revoking = false; this.changed(); }
    }
  }

  private async useToken(token: AdminCustomerAccessTokenApiResponse, kind: 'token' | 'link' | 'message' | 'whatsapp'): Promise<void> {
    const customer = this.customer();
    if (!customer || this.busy || !token.canReveal) return;
    const phone = normalizeReminderPhone(customer.phoneNumber ?? '');
    this.error = this.message = this.copyFallback = null;
    if (kind === 'whatsapp' && !phone) { this.error = 'Guarda un teléfono válido antes de abrir WhatsApp.'; return; }
    const popup = kind === 'whatsapp' ? this.reservePopup() : null;
    if (kind === 'whatsapp' && !popup) return;
    const epoch = this.epoch; const action = ++this.action; this.read++; this.loading = false;
    this.sharing = true;
    try {
      const fresh = await this.wait(forkJoin({ token: this.api.revealAccessToken(customer.id, token.tokenId), metadata: this.api.getNextDeliveryDate() }));
      if (!this.current(customer.id, epoch, action)) return;
      this.acceptReveal(fresh.token, fresh.metadata, customer.id, token.tokenId);
      if (popup) {
        popup.location.href = this.whatsAppUrl(phone!, fresh.token);
        this.message = 'WhatsApp abierto con el mensaje preparado. Confirma el envío desde WhatsApp.';
      } else {
        const value = kind === 'token' ? fresh.token.plainToken : kind === 'link' ? this.linkFor(fresh.token.plainToken)
          : this.messageFor(fresh.token.description, fresh.token.expiresAt, this.linkFor(fresh.token.plainToken));
        try {
          const clipboard = this.document.defaultView?.navigator.clipboard;
          if (!clipboard) throw new Error('Clipboard unavailable');
          await clipboard.writeText(value);
          if (!this.current(customer.id, epoch, action)) return;
          this.message = kind === 'token' ? 'Token copiado.' : kind === 'link' ? 'Enlace copiado.' : 'Mensaje copiado.';
        } catch {
          if (this.current(customer.id, epoch, action)) {
            this.copyFallback = value;
            this.error = 'No pudimos copiarlo automáticamente. Selecciona el contenido para copiarlo manualmente.';
          }
        }
      }
    } catch (error) {
      popup?.close();
      if (this.current(customer.id, epoch, action)) {
        this.error = this.errorText(error, 'No se pudo recuperar un token vigente. Actualiza la lista antes de compartirlo.');
        this.revealed = null;
        if (error instanceof HttpErrorResponse && [404, 409].includes(error.status)) {
          this.rows = this.rows.filter(row => row.tokenId !== token.tokenId);
          if (this.editingId === token.tokenId) this.unavailable = true;
        }
      }
    } finally {
      if (!this.current(customer.id, epoch, action)) popup?.close();
      else { this.sharing = false; this.popup = null; this.changed(); }
    }
  }

  private acceptReveal(token: RevealedCustomerAccessTokenApiResponse, metadata: { deliveryDate: string; serverNow: string }, customerId: string, tokenId: string): void {
    if (token.customerId !== customerId || token.tokenId !== tokenId || !token.plainToken || token.isActive === false
      || (token.expiresAt && (!Number.isFinite(Date.parse(token.expiresAt)) || Date.parse(token.expiresAt) <= Date.parse(metadata.serverNow)))) throw new Error('El enlace venció o cambió. Actualiza los tokens activos.');
    this.setClock(metadata); this.revealed = token; this.upsert(token);
  }
  private upsert(token: RevealedCustomerAccessTokenApiResponse | AdminCustomerAccessTokenApiResponse): void {
    const old = this.rows.find(row => row.tokenId === token.tokenId);
    const row: AdminCustomerAccessTokenApiResponse = { tokenId: token.tokenId, customerId: token.customerId,
      description: token.description, expiresAt: token.expiresAt, version: token.version, reminderLink: token.reminderLink,
      createdAt: old?.createdAt ?? this.serverNow ?? '', lastUsedAt: old?.lastUsedAt ?? null,
      isActive: true, isUsable: true, canReveal: true };
    this.rows = [row, ...this.rows.filter(item => item.tokenId !== token.tokenId)];
  }
  private current(customerId: string, epoch: number, action?: number): boolean {
    return !this.destroyed && this.epoch === epoch && this.customer()?.id === customerId && (action === undefined || action === this.action);
  }
  private wait<T>(request: Observable<T>): Promise<T> { return firstValueFrom(request.pipe(takeUntilDestroyed(this.destroyRef))); }
  private setClock(metadata: { deliveryDate: string; serverNow: string }): void {
    if (!Number.isFinite(Date.parse(metadata.serverNow)) || !/^\d{4}-\d{2}-\d{2}$/.test(metadata.deliveryDate)) throw new Error('No se pudo verificar la hora del servidor.');
    this.deliveryDate = metadata.deliveryDate; this.serverNow = metadata.serverNow; this.clockMark = performance.now();
  }
  private currentServerTime(): number { return this.serverNow ? Date.parse(this.serverNow) + Math.max(0, performance.now() - this.clockMark) : 0; }
  private draftExpiry(): string | null {
    return this.originalExpiry && this.expiresAt === reminderDateTimeInput(this.originalExpiry)
      ? this.originalExpiry : mexicoDateTimeToIso(this.expiresAt);
  }
  private linkFor(plainToken: string): string { return `${this.document.location.origin}/cliente#token=${encodeURIComponent(plainToken)}`; }
  private messageFor(description: string, expiresAt: string | null, link: string): string {
    return `Hola, ${this.customer()?.name ?? 'cliente'}. ${description}\n\nHaz tu pedido aquí:\n${link}\n\n${expiresAt ? `Enlace disponible hasta el ${formatReminderExpiration(expiresAt)}, hora de Ciudad de México.` : 'Configura la fecha y hora de vencimiento.'}`;
  }
  private whatsAppUrl(phone: string, token: RevealedCustomerAccessTokenApiResponse): string {
    return `https://wa.me/${phone.slice(1)}?text=${encodeURIComponent(this.messageFor(token.description, token.expiresAt, this.linkFor(token.plainToken)))}`;
  }
  private reservePopup(): Window | null {
    const popup = this.document.defaultView?.open('about:blank', '_blank') ?? null;
    if (!popup) this.error = 'Permite abrir ventanas emergentes o copia el mensaje para compartirlo.';
    else { popup.opener = null; this.popup = popup; }
    return popup;
  }
  private errorText(error: unknown, fallback: string): string {
    return error instanceof HttpErrorResponse ? error.error?.error || fallback : error instanceof Error ? error.message : fallback;
  }
}
