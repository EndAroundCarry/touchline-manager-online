/** A colour as the server stores it: `#` and six lower-case hexadecimal digits. */
const HEX_COLOUR = /^#[0-9a-f]{6}$/;

/**
 * Reads a colour a manager typed or a picker produced into the one form the server accepts.
 *
 * Returns null for anything that is not a six-digit colour, so a half-typed value never reaches the model.
 * The leading `#` is optional when typing, because `1f4e79` is how most colours are copied.
 */
export function normaliseHex(value: string): string | null {
  const trimmed = value.trim().toLowerCase();
  const candidate = trimmed.startsWith('#') ? trimmed : `#${trimmed}`;

  return HEX_COLOUR.test(candidate) ? candidate : null;
}

/** Whether two colours are a usable kit: both valid, and not the same colour. */
export function isKitPair(primary: string, secondary: string): boolean {
  const first = normaliseHex(primary);
  const second = normaliseHex(secondary);

  return first !== null && second !== null && first !== second;
}

/**
 * Whether black or white reads better on a colour, so a label on a swatch is never lost against it.
 *
 * Uses the relative luminance of the colour as the WCAG contrast formula defines it.
 */
export function readableOn(colour: string): '#ffffff' | '#111827' {
  const hex = normaliseHex(colour);

  if (hex === null) {
    return '#111827';
  }

  const channel = (offset: number): number => {
    const value = parseInt(hex.slice(offset, offset + 2), 16) / 255;

    return value <= 0.03928 ? value / 12.92 : Math.pow((value + 0.055) / 1.055, 2.4);
  };

  const luminance = 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);

  return luminance > 0.4 ? '#111827' : '#ffffff';
}

/** A named swatch the picker offers as a shortcut. The spectrum picker remains for everything else. */
export interface KitPreset {
  readonly name: string;
  readonly value: string;
}

/** The shortcut colours: the common kit colours, so the usual choice is one tap rather than a drag. */
export const KIT_PRESETS: readonly KitPreset[] = [
  { name: 'Red', value: '#d62828' },
  { name: 'Maroon', value: '#7b1e3a' },
  { name: 'Orange', value: '#f77f00' },
  { name: 'Yellow', value: '#fcd116' },
  { name: 'Green', value: '#2d9d4b' },
  { name: 'Dark green', value: '#14532d' },
  { name: 'Sky blue', value: '#4cc9f0' },
  { name: 'Blue', value: '#1d4ed8' },
  { name: 'Navy', value: '#12284c' },
  { name: 'Purple', value: '#6a2c91' },
  { name: 'Pink', value: '#f472b6' },
  { name: 'White', value: '#ffffff' },
  { name: 'Grey', value: '#9ca3af' },
  { name: 'Black', value: '#0b0b0f' },
];
