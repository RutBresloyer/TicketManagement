import { Pipe, PipeTransform } from '@angular/core';

const formatter = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'UTC',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

/** Formats an ISO timestamp as `yyyy-MM-dd HH:mm UTC`, always in UTC, regardless of the browser's time zone. */
@Pipe({ name: 'utcDate' })
export class UtcDatePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) {
      return '';
    }
    const parts = Object.fromEntries(
      formatter.formatToParts(new Date(value)).map((p) => [p.type, p.value]),
    );
    return `${parts['year']}-${parts['month']}-${parts['day']} ${parts['hour']}:${parts['minute']} UTC`;
  }
}
