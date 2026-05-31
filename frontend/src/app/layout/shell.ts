import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { PageKey } from '../shared/page-key';
import { Dashboard } from '../pages/dashboard/dashboard';
import { Catalog } from '../pages/instruments/catalog/catalog';
import { CardStock } from '../pages/instruments/card-stock/card-stock';
import { CardFutures } from '../pages/instruments/card-futures/card-futures';

interface NavItem {
  key: PageKey;
  label: string;
  enabled: boolean;
}

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Dashboard, Catalog, CardStock, CardFutures],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  /** Единственный источник состояния навигации. */
  protected readonly currentPage = signal<PageKey>('dashboard');

  protected readonly navItems: readonly NavItem[] = [
    { key: 'dashboard', label: 'Dashboard', enabled: true },
    { key: 'catalog-stocks', label: 'Инструменты', enabled: true },
    { key: 'catalog-stocks', label: 'Загрузки', enabled: false },
    { key: 'catalog-stocks', label: 'Операции', enabled: false },
    { key: 'catalog-stocks', label: 'Календарь', enabled: false },
    { key: 'catalog-stocks', label: 'Ограничения', enabled: false },
    { key: 'catalog-stocks', label: 'Издержки', enabled: false },
  ];

  /** Хлебная крошка раздела для topbar — выводится из текущей страницы. */
  protected readonly breadcrumb = computed(() =>
    this.isInstruments(this.currentPage()) ? 'Инструменты' : 'Dashboard',
  );

  private isInstruments(page: PageKey): boolean {
    return page.startsWith('catalog') || page.startsWith('card');
  }

  protected navActive(index: number): boolean {
    const page = this.currentPage();
    if (index === 0) return page === 'dashboard';
    if (index === 1) return this.isInstruments(page);
    return false;
  }

  protected go(item: NavItem, index: number): void {
    if (!item.enabled) return;
    this.currentPage.set(index === 1 ? 'catalog-stocks' : item.key);
  }
}
