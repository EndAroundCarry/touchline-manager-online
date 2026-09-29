import { newsCategoryLabel } from './news-presentation';

describe('news presentation', () => {
  it('names every category the server can send, and falls back to the code', () => {
    expect(newsCategoryLabel('division')).toBe('Division');
    expect(newsCategoryLabel('transfer')).toBe('Transfer');
    expect(newsCategoryLabel('result')).toBe('Result');
    expect(newsCategoryLabel('future')).toBe('future');
  });
});
