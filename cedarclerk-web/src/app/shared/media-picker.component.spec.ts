import { TestBed } from '@angular/core/testing';
import { AssetsService } from '../core/assets.service';
import { MediaPickerComponent } from './media-picker.component';

describe('image-only media selection', () => {
    it('restricts the library and upload input to images for banner selection', async () => {
        const list = vi.fn(async () => ({ items: [], total: 0, counts: { image: 0, video: 0, audio: 0 }, buckets: [] }));
        const upload = vi.fn();
        TestBed.configureTestingModule({ providers: [{ provide: AssetsService, useValue: { list, upload } }] });
        const fixture = TestBed.createComponent(MediaPickerComponent);
        fixture.componentRef.setInput('imagesOnly', true);
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
        expect(list).toHaveBeenCalledWith(expect.objectContaining({ type: 'image' }));
        expect(fixture.nativeElement.querySelector('.picker-leaves')).toBeNull();
        expect(fixture.nativeElement.querySelector('input[type="file"]').accept).toBe('image/*');
        await fixture.componentInstance.onUploadPicked({ target: { files: [new File(['audio'], 'clip.mp3', { type: 'audio/mpeg' })], value: 'clip.mp3' } } as unknown as Event);
        expect(upload).not.toHaveBeenCalled();
        expect(fixture.componentInstance.error()).toBeTruthy();
        fixture.destroy();
    });
});
