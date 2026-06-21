import { Component, signal } from '@angular/core';
import { Sidebar } from './sidebar/sidebar';
import { Topbar } from './topbar/topbar';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Sidebar, Topbar],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('frontend');
}
