export type UnitCode = 'kg' | 'piece' | 'dozen' | 'package';
export type PaymentTermCode = 'cash_on_delivery' | 'credit';

export function unitLabel(unitCode: string | null | undefined): string {
  switch (unitCode) {
    case 'kg': return 'kg';
    case 'piece': return 'piezas';
    case 'dozen': return 'docenas';
    case 'package': return 'paquetes';
    default: return 'unidad por confirmar';
  }
}

export function storeKeyLabel(hasStoreKey: boolean | null | undefined): string {
  if (hasStoreKey === true) return 'Con llave del local';
  if (hasStoreKey === false) return 'Sin llave del local';
  return 'Acceso por confirmar';
}

export function paymentTermLabel(paymentTerm: string | null | undefined): string {
  if (paymentTerm === 'cash_on_delivery') return 'Cobro diario contraentrega · requiere ticket';
  if (paymentTerm === 'credit') return 'Crédito';
  return 'Forma de cobro por confirmar';
}

export function businessDateToday(): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Mexico_City', year: 'numeric', month: '2-digit', day: '2-digit'
  }).formatToParts(new Date());
  const get = (type: string) => parts.find((part) => part.type === type)?.value;
  return `${get('year')}-${get('month')}-${get('day')}`;
}
