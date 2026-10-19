import { Component, ElementRef, Renderer2, computed, effect, inject, input } from '@angular/core';
import { FALLBACK_KIT, SvgNode, portraitLook, portraitScene } from './portrait-designs';

/** Gives each portrait on a page its own ids, because two drawings sharing them would paint with each other's. */
let nextPortraitId = 0;

/**
 * Draws a player's portrait: a face generated from the player's id, aged to the player's age, in the club's shirt
 * and crest (`portrait-designs.ts`).
 *
 * The size is whatever the host gives it (`class="w-48 aspect-[5/6]"`); the drawing fills it and crops to fit. The
 * drawing is built element by element with the renderer, so no markup is ever parsed.
 */
@Component({
  selector: 'app-player-portrait',
  template: '',
  host: {
    class: 'block overflow-hidden',
    role: 'img',
    '[attr.aria-label]': 'label()',
  },
})
export class PlayerPortrait {
  /** The player's id, which decides the face. */
  readonly playerId = input.required<string>();

  /** The player's age in years, which ages the face. */
  readonly age = input.required<number>();

  /** The club's main colour as `#rrggbb`: the shirt. */
  readonly primary = input<string>(FALLBACK_KIT.primary);

  /** The club's second colour as `#rrggbb`: the collar and the crest's stripes. */
  readonly secondary = input<string>(FALLBACK_KIT.secondary);

  /** The club crest's shape code. */
  readonly crestShape = input<string>(FALLBACK_KIT.crestShape);

  /** The club crest's stripes code. */
  readonly crestPattern = input<string>(FALLBACK_KIT.crestPattern);

  /** What assistive technology announces, such as "Portrait of Lorcan Ravenscroft". */
  readonly label = input.required<string>();

  private readonly key = `portrait-${nextPortraitId++}`;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly renderer = inject(Renderer2);

  /** The drawing, redrawn only when the player, the age or the kit changes. */
  protected readonly scene = computed(() =>
    portraitScene(
      portraitLook(this.playerId(), this.age()),
      {
        primary: this.primary(),
        secondary: this.secondary(),
        crestShape: this.crestShape(),
        crestPattern: this.crestPattern(),
      },
      this.key,
    ),
  );

  constructor() {
    effect(() => {
      const svg = this.build(this.scene());

      this.renderer.setAttribute(svg, 'width', '100%');
      this.renderer.setAttribute(svg, 'height', '100%');
      this.renderer.setAttribute(svg, 'aria-hidden', 'true');
      this.renderer.setAttribute(svg, 'focusable', 'false');
      this.renderer.setStyle(svg, 'display', 'block');

      for (const child of Array.from(this.host.childNodes)) {
        this.renderer.removeChild(this.host, child);
      }

      this.renderer.appendChild(this.host, svg);
    });
  }

  private build(node: SvgNode): Element {
    const element = this.renderer.createElement(node.tag, 'svg') as Element;

    for (const [name, value] of Object.entries(node.attrs)) {
      // The SVG namespace comes with createElement; an xmlns attribute set by hand is not allowed.
      if (name !== 'xmlns') {
        this.renderer.setAttribute(element, name, String(value));
      }
    }

    for (const child of node.children ?? []) {
      this.renderer.appendChild(element, this.build(child));
    }

    return element;
  }
}
