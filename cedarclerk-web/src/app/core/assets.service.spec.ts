import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AssetMeta, AssetsService } from './assets.service';

const META: AssetMeta = {
    id: 'a1', fileName: 'fog.png', contentType: 'image/png',
    sizeBytes: 402_115, width: 1920, height: 1080, projectId: null,
};

describe('AssetsService.meta', () => {
    let api: AssetsService;
    let http: HttpTestingController;

    beforeEach(() => {
        TestBed.resetTestingModule();
        TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
        api = TestBed.inject(AssetsService);
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => http.verify());

    it('files uploads into the selected project at upload time', async () => {
        const answer = api.upload(new File(['image'], 'cover.png', { type: 'image/png' }), 'project-a');
        const request = http.expectOne(r => r.url === '/api/assets');
        expect(request.request.params.get('projectId')).toBe('project-a');
        request.flush({ id: 'a1', url: '/media/cover.png' });
        await answer;
    });

    it('asks by id when the node carries one', async () => {
        const answer = api.meta({ id: 'a1', path: '2026/08/fog.png' });
        const req = http.expectOne(r => r.url === '/api/assets/meta');
        expect(req.request.params.get('id')).toBe('a1');
        // The id wins outright rather than being sent alongside: two keys would let the server
        // resolve a different row than the one the document names.
        expect(req.request.params.get('path')).toBeNull();
        req.flush(META);
        expect(await answer).toEqual(META);
    });

    // The whole point of the path key: a document written before media nodes carried an asset id
    // still answers, because its src has always been the asset's own path.
    it('falls back to the media path when there is no id', async () => {
        const answer = api.meta({ id: null, path: '2026/08/fog.png' });
        const req = http.expectOne(r => r.url === '/api/assets/meta');
        expect(req.request.params.get('path')).toBe('2026/08/fog.png');
        req.flush(META);
        expect(await answer).toEqual(META);
    });

    // A media node may point at a file this owner has no asset row for at all — an external URL,
    // a file since deleted. That is a fact the inspector omits, not an error it reports.
    it('answers nothing when the file is not found', async () => {
        const answer = api.meta({ path: 'gone.png' });
        http.expectOne(r => r.url === '/api/assets/meta')
            .flush('not found', { status: 404, statusText: 'Not Found' });
        expect(await answer).toBeNull();
    });

    it('asks nothing at all when it has neither key', async () => {
        expect(await api.meta({ id: null, path: null })).toBeNull();
        http.expectNone(r => r.url === '/api/assets/meta');
    });
});
