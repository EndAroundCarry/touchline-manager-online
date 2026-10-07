import { isKitPair, KIT_PRESETS, normaliseHex, readableOn } from './kit-colours';

describe('kit colours', () => {
  describe('normaliseHex', () => {
    it('lower-cases a colour and keeps its hash', () => {
      expect(normaliseHex('#1F4E79')).toBe('#1f4e79');
    });

    it('accepts a colour typed without the hash, as colours are usually copied', () => {
      expect(normaliseHex('1f4e79')).toBe('#1f4e79');
      expect(normaliseHex('  1F4E79 ')).toBe('#1f4e79');
    });

    it.each(['', '#', '#fff', '#1f4e7', '#1f4e799', '#gggggg', 'red', '#12 456'])(
      'refuses %j, which is not a six-digit colour',
      (value) => {
        expect(normaliseHex(value)).toBeNull();
      },
    );
  });

  describe('isKitPair', () => {
    it('accepts two different colours', () => {
      expect(isKitPair('#1f4e79', '#d6e4f0')).toBe(true);
    });

    it('refuses the same colour twice, however it is written', () => {
      expect(isKitPair('#1f4e79', '#1F4E79')).toBe(false);
      expect(isKitPair('#1f4e79', '1f4e79')).toBe(false);
    });

    it('refuses a pair with an invalid colour', () => {
      expect(isKitPair('#1f4e79', '#12')).toBe(false);
    });
  });

  describe('readableOn', () => {
    it('chooses dark text on a light colour and light text on a dark one', () => {
      expect(readableOn('#ffffff')).toBe('#111827');
      expect(readableOn('#fcd116')).toBe('#111827');
      expect(readableOn('#12284c')).toBe('#ffffff');
      expect(readableOn('#000000')).toBe('#ffffff');
    });
  });

  describe('presets', () => {
    it('are all valid colours with distinct values', () => {
      const values = KIT_PRESETS.map((preset) => normaliseHex(preset.value));

      expect(values.every((value) => value !== null)).toBe(true);
      expect(new Set(values).size).toBe(KIT_PRESETS.length);
    });
  });
});
