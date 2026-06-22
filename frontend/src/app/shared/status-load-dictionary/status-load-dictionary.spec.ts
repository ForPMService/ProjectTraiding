import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StatusLoadDictionary } from './status-load-dictionary';

describe('StatusLoadDictionary', () => {
  let component: StatusLoadDictionary;
  let fixture: ComponentFixture<StatusLoadDictionary>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StatusLoadDictionary],
    }).compileComponents();

    fixture = TestBed.createComponent(StatusLoadDictionary);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
