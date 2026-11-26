import { appConfig } from './app.config';

describe('appConfig', () => {
  it('registers change detection, routing, and HTTP', () => {
    expect(appConfig.providers.length).toBe(3);
  });
});
