import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlayerPortrait } from './player-portrait';

@Component({
  imports: [PlayerPortrait],
  template: `
    <app-player-portrait
      id="first"
      [playerId]="playerId()"
      [age]="age()"
      primary="#1d673a"
      secondary="#f2f2f2"
      label="Portrait of Lorcan Ravenscroft"
    />
    <app-player-portrait
      id="second"
      playerId="player-2"
      [age]="30"
      label="Portrait of Ewan Loxley"
    />
  `,
})
class Host {
  readonly playerId = signal('player-1');
  readonly age = signal(24);
}

describe('PlayerPortrait', () => {
  let fixture: ComponentFixture<Host>;
  let root: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
    fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    root = fixture.nativeElement as HTMLElement;
  });

  const first = (): HTMLElement => root.querySelector<HTMLElement>('#first')!;

  it('is announced as a picture of the player and draws a single SVG', () => {
    expect(first().getAttribute('role')).toBe('img');
    expect(first().getAttribute('aria-label')).toBe('Portrait of Lorcan Ravenscroft');
    expect(first().querySelectorAll(':scope > svg')).toHaveLength(1);
    expect(first().querySelector('svg')?.namespaceURI).toBe('http://www.w3.org/2000/svg');
  });

  it('wears the club’s shirt', () => {
    expect(first().querySelector('path[fill="#1d673a"]')).not.toBeNull();
  });

  it('redraws when the player ages, replacing the old drawing', async () => {
    const before = first().querySelector('svg')!.innerHTML;

    fixture.componentInstance.age.set(36);
    await fixture.whenStable();

    expect(first().querySelectorAll(':scope > svg')).toHaveLength(1);
    expect(first().querySelector('svg')!.innerHTML).not.toBe(before);
  });

  it('gives two portraits on one page their own ids', () => {
    const idsOf = (host: Element): string[] =>
      Array.from(host.querySelectorAll('[id]')).map((node) => node.id);
    const firstIds = new Set(idsOf(first()));

    expect(firstIds.size).toBeGreaterThan(0);
    expect(idsOf(root.querySelector('#second')!).some((id) => firstIds.has(id))).toBe(false);
  });
});
