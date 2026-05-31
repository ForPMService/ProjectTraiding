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
interface Contract {
  secid: string;
  type: string;
  expiration: string;
  days: string;
  weekend: string;
  status: string;
  accent: boolean;
}
interface Relation {
  secid: string;
  relation: string;
  asset_code: string;
  confidence: string;
}

@Component({
  selector: 'app-card-futures',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './card-futures.html',
  styleUrl: './card-futures.scss',
})
export class CardFutures {
  readonly pageChange = output<PageKey>();

  protected readonly metrics: readonly Metric[] = [
    { label: 'Покрытие', value: '88%', cls: 'val-warn' },
    { label: 'Загружено типов', value: '2 из 3' },
    { label: 'До экспирации', value: '12 дн', cls: 'val-error' },
    { label: 'Откр. интерес', value: '1 245 000' },
  ];

  protected readonly params: readonly Param[] = [
    { label: 'Базовый актив', value: 'Si' },
    { label: 'Объём лота', value: '1' },
    { label: 'Режим', value: 'RFUD' },
    { label: 'Шаг цены', value: '1' },
    { label: 'Экспирация', value: '20.06.2026' },
    { label: 'Цена шага', value: '1.00 ₽' },
    { label: 'Дата поставки', value: '22.06.2026' },
    { label: 'Верхний лимит', value: '98 500' },
    { label: 'ГО', value: '15 420 ₽' },
    { label: 'Нижний лимит', value: '82 300' },
    { label: 'Откр. интерес', value: '1 245 000' },
    { label: 'Комиссия биржи', value: '3.21 ₽' },
    { label: 'Скальперская ком.', value: '0.50 ₽' },
  ];

  protected readonly coverage: readonly CoverageRow[] = [
    { data_kind: 'candles', interval: '1m', period: '01.12 – 31.12', rows: '14 220', storage: 'file', status: 'ok', status_label: 'ok', updated: '09:39', action: 'Открыть задание' },
    { data_kind: 'futoi', interval: '—', period: '01.12 – 31.12', rows: '1 440', storage: 'file', status: 'ok', status_label: 'ok', updated: '09:39', action: 'Открыть задание' },
    { data_kind: 'securities', interval: '—', period: 'актуально', rows: '—', storage: 'PostgreSQL', status: 'ok', status_label: 'ok', updated: 'сегодня', action: 'Обновить' },
  ];

  protected readonly contracts: readonly Contract[] = [
    { secid: 'SiM6', type: 'quarterly', expiration: '20.06.2026', days: '12', weekend: 'нет', status: '← текущий', accent: true },
    { secid: 'SiU6', type: 'quarterly', expiration: '19.09.2026', days: '102', weekend: 'нет', status: 'следующий', accent: true },
    { secid: 'SiZ6', type: 'quarterly', expiration: '18.12.2026', days: '192', weekend: 'нет', status: '—', accent: false },
  ];

  protected readonly relations: readonly Relation[] = [
    { secid: '—', relation: 'future_underlying', asset_code: 'Si', confidence: 'auto' },
  ];

  protected readonly log: readonly { time: string; text: string }[] = [
    { time: '09:39', text: 'candles загружены · 14 220 строк' },
    { time: '09:39', text: 'futoi загружены · 1 440 строк (asset_code: Si)' },
    { time: 'вчера', text: 'справочник instruments обновлён' },
  ];
}
