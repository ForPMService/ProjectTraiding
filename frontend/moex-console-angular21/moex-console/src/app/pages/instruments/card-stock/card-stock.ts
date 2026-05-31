import { ChangeDetectionStrategy, Component, output } from '@angular/core';
import { PageKey } from '../../../shared/page-key';

interface Metric {
  label: string;
  value: string;
  cls?: string;
}
interface Param {
  label: string;
  value: string;
}
interface CoverageRow {
  data_kind: string;
  interval: string;
  period: string;
  rows: string;
  storage: string;
  status: 'ok' | 'not_loaded';
  status_label: string;
  updated: string;
  action: string;
}
interface Relation {
  secid: string;
  relation: string;
  confidence: string;
}
interface Change {
  date: string;
  attribute: string;
  before: string;
  after: string;
  action: string;
}

@Component({
  selector: 'app-card-stock',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './card-stock.html',
  styleUrl: './card-stock.scss',
})
export class CardStock {
  readonly pageChange = output<PageKey>();

  protected readonly metrics: readonly Metric[] = [
    { label: 'Покрытие', value: '92%', cls: 'val-warn' },
    { label: 'Загружено типов', value: '3 из 5' },
    { label: 'Последнее обновление', value: '09:42' },
    { label: 'Дней без обновления', value: '0' },
  ];

  protected readonly params: readonly Param[] = [
    { label: 'Режим', value: 'TQBR' },
    { label: 'Валюта', value: 'RUB' },
    { label: 'Листинг', value: '1' },
    { label: 'ISIN', value: 'RU0009029540' },
    { label: 'Лот', value: '10' },
    { label: 'Шаг цены', value: '0.01' },
    { label: 'Номинал', value: '3.00 ₽' },
    { label: 'Статус торгов', value: 'active' },
  ];

  protected readonly coverage: readonly CoverageRow[] = [
    { data_kind: 'candles', interval: '1m', period: '01.12 – 31.12', rows: '22 341', storage: 'file', status: 'ok', status_label: 'ok', updated: '09:42', action: 'Открыть задание' },
    { data_kind: 'tradestats', interval: '—', period: '01.12 – 31.12', rows: '18 902', storage: 'file', status: 'ok', status_label: 'ok', updated: '09:41', action: 'Открыть задание' },
    { data_kind: 'orderstats', interval: '—', period: '01.12 – 31.12', rows: '16 320', storage: 'file', status: 'ok', status_label: 'ok', updated: '09:40', action: 'Открыть задание' },
    { data_kind: 'obstats', interval: '—', period: '—', rows: '—', storage: '—', status: 'not_loaded', status_label: 'не загружено', updated: '—', action: 'Загрузить' },
    { data_kind: 'securities', interval: '—', period: 'актуально', rows: '—', storage: 'PostgreSQL', status: 'ok', status_label: 'ok', updated: 'сегодня', action: 'Обновить' },
  ];

  protected readonly relations: readonly Relation[] = [
    { secid: 'SBRF-6.26', relation: 'фьючерс на базовый актив', confidence: 'auto' },
    { secid: 'SBRF-9.26', relation: 'фьючерс на базовый актив', confidence: 'auto' },
  ];

  protected readonly changes: readonly Change[] = [
    { date: '2 дня назад', attribute: 'LISTLEVEL', before: '2', after: '1', action: 'updated' },
    { date: '5 дней назад', attribute: 'LOTSIZE', before: '100', after: '10', action: 'updated' },
  ];

  protected readonly log: readonly { time: string; text: string }[] = [
    { time: '09:42', text: 'candles загружены успешно · 22 341 строк' },
    { time: '09:41', text: 'tradestats загружены · 18 902 строк' },
    { time: '09:40', text: 'orderstats загружены · 16 320 строк' },
    { time: 'вчера', text: 'справочник instruments обновлён' },
    { time: 'вчера', text: 'MarketStatistics выполнен частично · часть полей не получена' },
  ];
}
