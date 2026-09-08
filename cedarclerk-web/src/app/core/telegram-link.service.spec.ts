import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TelegramLinkService } from './telegram-link.service';

describe('Telegram popup login', () => {
    beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient()] }); });
    afterEach(() => {
        delete window.Telegram;
        document.querySelectorAll('script[src*="telegram-widget"]').forEach(script => script.remove());
        vi.restoreAllMocks();
    });

    it('reports blocked popups without entering the SDK wait loop', () => {
        const auth = vi.fn();
        window.Telegram = { Login: { auth } };
        vi.spyOn(window, 'open').mockReturnValue(null);
        expect(TestBed.inject(TelegramLinkService).authorizeLogin(123, 'en', vi.fn())).toBe(false);
        expect(auth).not.toHaveBeenCalled();
    });

    it('uses the SDK window and requests no messaging access for sign in', () => {
        const callback = vi.fn();
        const auth = vi.fn();
        window.Telegram = { Login: { auth } };
        const open = vi.spyOn(window, 'open').mockReturnValue({ close: vi.fn() } as unknown as Window);
        expect(TestBed.inject(TelegramLinkService).authorizeLogin(123, 'ru', callback)).toBe(true);
        expect(open).toHaveBeenCalledWith('', 'telegram_oauth_bot123', 'width=550,height=470');
        expect(auth).toHaveBeenCalledWith({ bot_id: '123', lang: 'ru' }, callback);
    });

    it('shares a pending script and allows retry after a failed download', async () => {
        const service = TestBed.inject(TelegramLinkService);
        const first = service.prepare();
        expect(service.prepare()).toBe(first);
        const rejected = expect(first).rejects.toThrow('Failed to load');
        document.querySelector('script[src*="telegram-widget"]')!.dispatchEvent(new Event('error'));
        await rejected;
        expect(document.querySelector('script[src*="telegram-widget"]')).toBeNull();
        const retry = service.prepare();
        window.Telegram = { Login: { auth: vi.fn() } };
        document.querySelector('script[src*="telegram-widget"]')!.dispatchEvent(new Event('load'));
        await retry;
    });
});
