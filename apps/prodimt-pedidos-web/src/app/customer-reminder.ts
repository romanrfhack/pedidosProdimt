const timeZone = 'America/Mexico_City';

/** Interpret datetime-local explicitly in the business zone, independent of the operator's device. */
export function mexicoDateTimeToIso(value: string | null): string | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value ?? '');
  if (!match) return null;
  const wanted = match.slice(1).map(Number);
  const utc = Date.UTC(wanted[0], wanted[1] - 1, wanted[2], wanted[3], wanted[4]);
  const formatter = new Intl.DateTimeFormat('en-CA', {
    timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
  });
  let candidate = utc;
  for (let i = 0; i < 3; i++) {
    const parts = Object.fromEntries(formatter.formatToParts(new Date(candidate)).map(p => [p.type, p.value]));
    const represented = Date.UTC(+parts['year'], +parts['month'] - 1, +parts['day'], +parts['hour'], +parts['minute']);
    if (represented === utc) {
      if (+parts['year'] !== wanted[0] || +parts['month'] !== wanted[1] || +parts['day'] !== wanted[2]
        || +parts['hour'] !== wanted[3] || +parts['minute'] !== wanted[4]) return null;
      return new Date(candidate).toISOString();
    }
    candidate += utc - represented;
  }
  return null;
}

export function normalizeReminderPhone(value: string): string | null {
  if (!/^[+\d\s().-]+$/.test(value)) return null;
  const compact = value.replace(/[\s().-]/g, '');
  if (/^\d{10}$/.test(compact)) return `+52${compact}`;
  if (/^52\d{10}$/.test(compact)) return `+${compact}`;
  return /^\+[1-9]\d{7,14}$/.test(compact) ? compact : null;
}

export function formatReminderDate(value: string): string {
  return new Intl.DateTimeFormat('es-MX', { timeZone, dateStyle: 'full' }).format(new Date(`${value}T12:00:00Z`));
}

export function formatReminderExpiration(value: string): string {
  return new Intl.DateTimeFormat('es-MX', { timeZone, dateStyle: 'long', timeStyle: 'short' }).format(new Date(value));
}
