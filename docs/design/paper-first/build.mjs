import { writeFileSync } from 'node:fs';

const I = {
  tree: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3 7 10h3l-4 6h4l-3 4h10l-3-4h4l-4-6h3z"/><path d="M12 20v2"/></svg>',
  doc: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8z"/><path d="M14 3v5h5M9 13h6M9 17h6"/></svg>',
  image: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="16" rx="1.5"/><path d="m3 16 5-5 4 4 3-3 6 6"/><circle cx="16" cy="9" r="1.5"/></svg>',
  globe: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c3 3.5 3 14.5 0 18M12 3c-3 3.5-3 14.5 0 18"/></svg>',
  check: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="4" y="4" width="16" height="16" rx="1.5"/><path d="m8 12 3 3 5-6"/></svg>',
  map: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 6v13l6-2 4 2 6-2V4l-6 2-4-2z"/><path d="M10 4v13M14 6v13"/></svg>',
  cal: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="16" rx="1.5"/><path d="M3 10h18M8 3v4M16 3v4"/></svg>',
  box: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="m12 3 8 4.5v9L12 21l-8-4.5v-9z"/><path d="M4 7.5 12 12l8-4.5M12 12v9"/></svg>',
  send: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M21 3 10 14M21 3l-7 18-4-7-7-4z"/></svg>',
  chart: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/></svg>',
  gear: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/></svg>',
  shield: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3 4 6v6c0 5 3.5 8 8 9 4.5-1 8-4 8-9V6z"/><path d="m9 12 2 2 4-4"/></svg>',
  plus: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg>',
  search: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/></svg>',
  chev: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6"/></svg>',
  left: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="m15 6-6 6 6 6"/></svg>',
  right: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="m9 6 6 6-6 6"/></svg>',
  bell: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M6 16V11a6 6 0 0 1 12 0v5l2 2H4zM10 21h4"/></svg>',
  dots: '<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor"><circle cx="5" cy="12" r="1.8"/><circle cx="12" cy="12" r="1.8"/><circle cx="19" cy="12" r="1.8"/></svg>',
  ext: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 4h6v6M20 4l-9 9M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5"/></svg>',
  pen: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="m4 20 4-1L20 7l-3-3L5 16z"/><path d="m14 6 3 3"/></svg>',
  flag: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 21V4h12l-2 4 2 4H5"/></svg>',
  clock: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>',
  tg: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 4 3 11l6 2 2 6 3-4 5 3z"/><path d="m9 13 12-9"/></svg>',
};

const HEAD = `<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <script src="./support.js"></script>
</head>
<body>
<x-dc>
<helmet>
  <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Source+Sans+3:wght@400;600;700&family=Vollkorn:wght@600;700&family=Literata:ital,wght@0,400;0,600;1,400&family=Martian+Mono:wght@400;600&family=Caveat:wght@500&display=swap">
  <style>
    body { margin: 0; background: #E9E1CC; color: #2C251A; font-family: 'Source Sans 3', 'Segoe UI', system-ui, sans-serif; font-size: 14px; line-height: 1.4; -webkit-font-smoothing: antialiased; }
    a { color: #39543C; text-decoration: none; } a:hover { color: #2C251A; }
    * { box-sizing: border-box; }
    .display { font-family: 'Vollkorn', Georgia, serif; }
    .mono { font-family: 'Martian Mono', Consolas, monospace; }
    .note { font-family: 'Caveat', cursive; }
    .nav a { display: flex; align-items: center; gap: 10px; height: 34px; padding: 0 12px; border-radius: 4px; color: #5B513E; font-size: 14px; font-weight: 600; }
    .nav a:hover { background: rgba(44,37,26,.06); color: #2C251A; }
    .nav a.on { background: #39543C; color: #FAF6EC; }
    .nav a.on svg { color: #FAF6EC; }
    .nav svg { color: #8C7F66; flex: none; }
    .btn { display: inline-flex; align-items: center; gap: 8px; height: 36px; padding: 0 14px; border-radius: 4px; border: 1px solid #CDC1A8; background: #FAF6EC; color: #2C251A; font-weight: 600; font-size: 14px; cursor: pointer; white-space: nowrap; }
    .btn.primary { background: #39543C; border-color: #39543C; color: #FAF6EC; }
    .btn.ghost { background: transparent; border-color: transparent; color: #5B513E; }
    .btn.sm { height: 30px; padding: 0 10px; font-size: 13px; }
    .card { background: #FAF6EC; border: 1px solid #CDC1A8; border-radius: 8px; box-shadow: 0 1px 3px rgba(58,38,16,.14); }
    .tag { display: inline-flex; align-items: center; height: 22px; padding: 0 8px; border-radius: 3px; font-size: 12px; font-weight: 700; letter-spacing: .04em; text-transform: uppercase; }
    .tag.ok { background: #E1E7DE; color: #356842; }
    .tag.muted { background: #E9E1CC; color: #5B513E; }
    .tag.warn { background: #EBE1CE; color: #7A5520; }
    .seg { display: inline-flex; background: #E9E1CC; border-radius: 6px; padding: 3px; gap: 2px; }
    .seg span { padding: 5px 12px; border-radius: 4px; font-size: 13px; font-weight: 600; color: #5B513E; }
    .seg span.on { background: #FAF6EC; color: #2C251A; box-shadow: 0 1px 2px rgba(58,38,16,.14); }
    .row { display: grid; grid-template-columns: 24px minmax(0, 1fr) 120px 210px; align-items: center; gap: 14px; height: 44px; padding: 0 16px; border-top: 1px solid #E4DBC4; font-size: 15px; }
    .row:hover { background: #F5F0E1; }
    .row .t { font-weight: 600; color: #2C251A; }
    .row .m { color: #8C7F66; font-size: 13px; }
    .row svg { color: #8C7F66; }
    .kv { display: grid; grid-template-columns: 96px 1fr; gap: 8px 12px; font-size: 13px; align-items: center; }
    .kv b { color: #8C7F66; font-weight: 600; }
    .field { height: 32px; border: 1px solid #CDC1A8; border-radius: 4px; background: #FAF6EC; padding: 0 10px; display: flex; align-items: center; font-size: 13px; color: #2C251A; }
    .label { font-size: 11px; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; color: #8C7F66; }
    .input { display: flex; align-items: center; gap: 8px; height: 36px; padding: 0 12px; border: 1px solid #CDC1A8; border-radius: 4px; background: #FAF6EC; color: #8C7F66; font-size: 14px; }
    .empty { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 10px; text-align: center; border: 1.5px dashed #CDC1A8; border-radius: 8px; color: #8C7F66; padding: 24px; }
  </style>
</helmet>`;

const TAIL = `</x-dc>
</body>
</html>`;

function sidebar(active, opts = {}) {
  const proj = opts.project ?? 'Dev Dairy Diary';
  const item = (key, icon, label, count) => `<a href="#" class="${active === key ? 'on' : ''}">${icon}<span style="flex:1">${label}</span>${count ? `<span style="font-size:12px;font-weight:600;opacity:.7">${count}</span>` : ''}</a>`;
  return `<aside style="width: 232px; flex: none; display: flex; flex-direction: column; background: #F1EADA; border-right: 1px solid #CDC1A8; height: 100%;">
  <div style="display:flex;align-items:center;gap:10px;height:56px;padding:0 16px;color:#39543C;">${I.tree}<span class="display" style="font-size:18px;font-weight:700;color:#2C251A;">Cedar Clerk</span></div>
  <div style="margin: 0 12px 16px; display:flex; align-items:center; gap:10px; height:44px; padding:0 10px; background:#FAF6EC; border:1px solid #CDC1A8; border-radius:6px;">
    <div style="width:26px;height:26px;border-radius:5px;background:#B98A5E;color:#FAF6EC;display:flex;align-items:center;justify-content:center;font-size:11px;font-weight:700;">${proj.split(' ').map(w=>w[0]).join('').slice(0,2)}</div>
    <div style="flex:1;min-width:0"><div style="font-weight:700;font-size:14px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">${proj}</div><div style="font-size:12px;color:#8C7F66;">${opts.kind ?? 'Blog'} · 2 projects</div></div>
    <span style="color:#8C7F66">${I.chev}</span>
  </div>
  <nav class="nav" style="display:flex;flex-direction:column;gap:2px;padding:0 12px;">
    <div class="label" style="padding:8px 12px 6px;">Write</div>
    ${item('docs', I.doc, 'Documents', '23')}
    ${item('assets', I.image, 'Assets', '77')}
    ${item('site', I.globe, 'Site')}
    <div class="label" style="padding:16px 12px 6px;">Plan</div>
    ${item('board', I.check, 'Tasks', '1')}
    ${item('planner', I.map, 'Planner')}
    ${item('calendar', I.cal, 'Calendar')}
    <div class="label" style="padding:16px 12px 6px;">Ship</div>
    ${item('builds', I.box, 'Builds')}
    ${item('posts', I.send, 'Posts', '24')}
    ${item('metrics', I.chart, 'Metrics')}
  </nav>
  <div style="flex:1"></div>
  <nav class="nav" style="display:flex;flex-direction:column;gap:2px;padding:0 12px 12px;">
    ${item('hub', I.map, 'All projects')}
    ${item('settings', I.gear, 'Settings')}
  </nav>
  <div style="display:flex;align-items:center;gap:10px;height:52px;padding:0 16px;border-top:1px solid #CDC1A8;font-size:13px;color:#5B513E;">
    <div style="width:26px;height:26px;border-radius:50%;background:#39543C;color:#FAF6EC;display:flex;align-items:center;justify-content:center;font-size:11px;font-weight:700;">M</div>
    <span style="flex:1;font-weight:600;">martycow</span>
    <span style="position:relative;color:#8C7F66;">${I.bell}<i style="position:absolute;top:-2px;right:-3px;width:7px;height:7px;border-radius:50%;background:#9E3D27;"></i></span>
  </div>
</aside>`;
}

function header(title, meta, actions, sub = '') {
  return `<header style="display:flex;align-items:flex-end;justify-content:space-between;gap:24px;padding:28px 40px 20px;">
  <div style="display:flex;flex-direction:column;gap:6px;">
    ${sub}
    <h1 class="display" style="margin:0;font-size:27px;font-weight:700;line-height:1.1;">${title}</h1>
    <div style="display:flex;align-items:center;gap:10px;font-size:14px;color:#5B513E;">${meta}</div>
  </div>
  <div style="display:flex;gap:8px;">${actions}</div>
</header>`;
}

const shell = (side, body) => `${HEAD}
<div style="width:1440px;height:900px;display:flex;background:#E9E1CC;overflow:hidden;">
${side}
<main style="flex:1;min-width:0;display:flex;flex-direction:column;">
${body}
</main>
</div>
${TAIL}`;

/* ---------- Main: project dashboard ---------- */
const docs = [
  ['Coyote vs ACME','Regular post','30 Aug, 20:11','ok','Live'],
  ['0.2.0','Changelog','26 Aug, 20:55','ok','Live'],
  ['Devlog — Cedar Clerk Closed Beta Test','Devlog post','25 Aug, 15:23','ok','Live'],
  ['Apple Bigotry','Note','25 Aug, 15:08','muted','Draft'],
  ['Childhood','Devlog post','23 Aug, 10:01','ok','Live'],
  ['Do you use AI?','Devlog post','17 Aug, 07:46','ok','Live'],
  ['U.S. Cable Television','Devlog post','12 Aug, 18:25','ok','Live'],
  ['Competition begins','Devlog post','12 Aug, 17:54','muted','Archived'],
  ['New Server','Devlog post','11 Aug, 22:12','ok','Live'],
  ['Meme time (again)','Devlog post','10 Aug, 00:46','ok','Live'],
  ['Social Media Test','Devlog post','08 Aug, 01:22','ok','Live'],
  ['Astoria Trip','Devlog post','05 Aug, 15:01','ok','Live'],
  ['Multicultural Update','Devlog post','01 Aug, 12:48','ok','Live'],
  ['My Claude Workflow','Devlog post','30 Jul, 18:27','ok','Live'],
];
const docRows = docs.map(([t,k,d,c,s]) => `<div class="row">${I.doc}<span class="t">${t}</span><span class="m">${k}</span><span style="display:flex;align-items:center;justify-content:space-between;gap:8px;"><span class="m">${d}</span><span class="tag ${c}">${s}</span></span></div>`).join('\n');

const mainBody = `
${header('Dev Dairy Diary', `<span class="tag ok">Active</span><span>Blog</span><span style="color:#CDC1A8">·</span><span>23 documents</span><span style="color:#CDC1A8">·</span><span>last edit 30 Aug</span>`,
  `<button class="btn">${I.gear}Settings</button><button class="btn primary">${I.plus}New document</button>`)}
<div style="flex:1;min-height:0;display:grid;grid-template-columns:minmax(0,1fr) 320px;gap:24px;padding:0 40px 32px;">
  <section style="display:flex;flex-direction:column;gap:20px;min-height:0;">
    <div class="card" style="display:flex;align-items:center;gap:18px;padding:16px 18px;">
      <div style="width:96px;height:64px;border-radius:4px;background:linear-gradient(135deg,#2B3B4E,#0F1720);flex:none;"></div>
      <div style="flex:1;min-width:0;display:flex;flex-direction:column;gap:4px;">
        <div class="label">Continue writing</div>
        <div class="display" style="font-size:20px;font-weight:700;">Coyote vs ACME</div>
        <div style="font-size:13px;color:#8C7F66;">Regular post · 59 words · published to blog and Telegram · edited 30 Aug, 20:11</div>
      </div>
      <button class="btn primary">${I.pen}Open</button>
    </div>
    <div class="card" style="flex:1;min-height:0;display:flex;flex-direction:column;overflow:hidden;">
      <div style="display:flex;align-items:center;gap:12px;padding:12px 16px;">
        <span class="display" style="font-size:18px;font-weight:700;flex:1;">Documents <span style="color:#8C7F66;font-weight:600;font-size:15px;">23</span></span>
        <div class="seg"><span class="on">All</span><span>Live</span><span>Drafts</span><span>Archived</span></div>
        <div class="input" style="width:220px;height:32px;">${I.search}<span>Search title or tag</span></div>
      </div>
      <div style="overflow:auto;flex:1;">
        ${docRows}
      </div>
    </div>
  </section>
  <aside style="display:flex;flex-direction:column;gap:16px;">
    <div class="card" style="padding:16px 18px;display:flex;flex-direction:column;gap:12px;">
      <div style="display:flex;justify-content:space-between;align-items:baseline;"><div class="label">Sprint S1</div><span style="font-size:12px;color:#8C7F66;">28 days left</span></div>
      <div class="display" style="font-size:17px;font-weight:700;line-height:1.2;">Cedar Clerk Closed Beta Test</div>
      <div style="height:6px;border-radius:3px;background:#E9E1CC;overflow:hidden;"><div style="width:8%;height:100%;background:#39543C;"></div></div>
      <div style="font-size:13px;color:#5B513E;">0 of 1 tasks done · until 29 Sep</div>
    </div>
    <div class="card" style="padding:16px 18px;display:flex;flex-direction:column;gap:12px;">
      <div class="label">Up next</div>
      <div style="display:flex;align-items:center;gap:10px;padding:10px 12px;border:1px solid #CDC1A8;border-radius:4px;background:#F5F0E1;">
        <span style="width:8px;height:8px;border-radius:50%;background:#A8721F;flex:none;"></span>
        <span style="flex:1;font-weight:600;">Дописать пост про Mac Mini</span>
        <span class="tag muted" style="height:20px;">P2</span>
      </div>
      <a href="#" style="font-size:13px;font-weight:600;">Open board →</a>
    </div>
    <div class="card" style="padding:16px 18px;display:flex;flex-direction:column;gap:12px;">
      <div class="label">Where it goes</div>
      <div class="kv">
        <b>Blog</b><span style="display:flex;align-items:center;gap:6px;">martycow.cedarclerk.app ${I.ext}</span>
        <b>Telegram</b><span style="display:flex;align-items:center;gap:6px;">@devdairydiaryen ${I.ext}</span>
        <b>Public page</b><span style="color:#8C7F66;">not published</span>
        <b>Assets</b><span>77 files · 937 MB</span>
        <b>Builds</b><span>1 version</span>
      </div>
    </div>
  </aside>
</div>`;

/* ---------- Hub ---------- */
const hubBody = `
${header('Projects', `<span>2 active</span><span style="color:#CDC1A8">·</span><span>0 archived</span>`,
  `<button class="btn primary">${I.plus}New project</button>`)}
<div style="padding:0 40px 32px;display:flex;flex-direction:column;gap:20px;">
  <div style="display:flex;align-items:center;gap:12px;">
    <div class="seg"><span class="on">All</span><span>Active</span><span>Archived</span><span>Shared with me</span></div>
    <div style="flex:1"></div>
    <div class="input" style="width:240px;height:32px;">${I.search}<span>Find a project</span></div>
  </div>
  <div style="display:grid;grid-template-columns:repeat(3, minmax(0, 1fr));gap:20px;">
    <div class="card" style="overflow:hidden;display:flex;flex-direction:column;">
      <div style="height:160px;background:linear-gradient(160deg,#C9A784,#8F6A48);position:relative;">
        <span class="tag ok" style="position:absolute;top:12px;right:12px;background:#FAF6EC;">Active</span>
      </div>
      <div style="padding:16px 18px 18px;display:flex;flex-direction:column;gap:8px;">
        <div class="display" style="font-size:20px;font-weight:700;">Dev Dairy Diary</div>
        <div style="font-size:13px;color:#5B513E;">Blog · blog.mooexe.dev · Telegram</div>
        <div style="display:flex;gap:16px;font-size:13px;color:#8C7F66;margin-top:6px;">
          <span><b style="color:#2C251A;">23</b> documents</span><span><b style="color:#2C251A;">1</b> open task</span><span>edited 30 Aug</span>
        </div>
      </div>
    </div>
    <div class="card" style="overflow:hidden;display:flex;flex-direction:column;">
      <div style="height:160px;background:linear-gradient(160deg,#A9834F,#6E4E2B);position:relative;display:flex;align-items:center;justify-content:center;">
        <span class="display" style="font-size:40px;font-weight:700;color:#F3EDDE;opacity:.9;">CS</span>
        <span class="tag ok" style="position:absolute;top:12px;right:12px;background:#FAF6EC;">Active</span>
      </div>
      <div style="padding:16px 18px 18px;display:flex;flex-direction:column;gap:8px;">
        <div class="display" style="font-size:20px;font-weight:700;">Cedar Station</div>
        <div style="font-size:13px;color:#5B513E;">Game · press kit · itch.io</div>
        <div style="display:flex;gap:16px;font-size:13px;color:#8C7F66;margin-top:6px;">
          <span><b style="color:#2C251A;">1</b> document</span><span><b style="color:#2C251A;">74</b> assets</span><span>edited 31 Jul</span>
        </div>
      </div>
    </div>
    <div class="empty" style="min-height:262px;background:transparent;">
      <span style="width:40px;height:40px;border-radius:50%;background:#E9E1CC;display:flex;align-items:center;justify-content:center;color:#39543C;">${I.plus}</span>
      <div style="font-weight:700;color:#2C251A;font-size:15px;">Start a new project</div>
      <div style="font-size:13px;max-width:220px;">A blog, a game, a newsletter — one place to write, plan and publish it.</div>
    </div>
  </div>
  <div class="card" style="padding:16px 18px;display:grid;grid-template-columns:1fr 1fr 1fr;gap:24px;">
    <div><div class="label">This week</div><div style="margin-top:6px;font-size:15px;">3 posts published · 2 scheduled</div></div>
    <div><div class="label">Needs attention</div><div style="margin-top:6px;font-size:15px;">1 draft older than 7 days · Telegram not linked on Cedar Station</div></div>
    <div><div class="label">Next scheduled</div><div style="margin-top:6px;font-size:15px;">Thu 4 Sep, 19:00 — Telegram</div></div>
  </div>
</div>`;

/* ---------- Editor ---------- */
const rail = (active) => {
  const it = (key, icon) => `<a href="#" class="${active===key?'on':''}" style="width:38px;height:38px;justify-content:center;padding:0;">${icon}</a>`;
  return `<aside class="nav" style="width:56px;flex:none;display:flex;flex-direction:column;align-items:center;gap:6px;padding:10px 0;background:#F1EADA;border-right:1px solid #CDC1A8;height:100%;">
  <div style="color:#39543C;height:36px;display:flex;align-items:center;">${I.tree}</div>
  ${it('docs', I.doc)}${it('assets', I.image)}${it('board', I.check)}${it('calendar', I.cal)}${it('posts', I.send)}${it('metrics', I.chart)}
  <div style="flex:1"></div>${it('settings', I.gear)}
  <div style="width:26px;height:26px;border-radius:50%;background:#39543C;color:#FAF6EC;display:flex;align-items:center;justify-content:center;font-size:11px;font-weight:700;margin-top:6px;">M</div>
</aside>`;
};
const tb = (label) => `<span style="height:28px;min-width:28px;padding:0 6px;border-radius:4px;display:inline-flex;align-items:center;justify-content:center;font-size:13px;font-weight:600;color:#5B513E;">${label}</span>`;
const tbSep = `<span style="width:1px;height:18px;background:#CDC1A8;margin:0 4px;"></span>`;
const editorBody = `
<div style="display:flex;align-items:center;gap:12px;height:56px;padding:0 20px;border-bottom:1px solid #CDC1A8;background:#F1EADA;">
  <a href="#" style="display:flex;align-items:center;gap:6px;color:#5B513E;font-weight:600;">${I.left}Documents</a>
  <span style="color:#CDC1A8">/</span>
  <span style="font-weight:700;">Coyote vs ACME</span>
  <span class="tag ok">Live</span>
  <div style="flex:1"></div>
  <div class="seg"><span class="on">Write</span><span>Preview</span><span>Publish</span><span>Stats</span></div>
  <div style="flex:1"></div>
  <span style="font-size:13px;color:#8C7F66;">Saved · 59 words</span>
  <button class="btn sm">Preview</button>
  <button class="btn sm primary">${I.send}Publish changes</button>
</div>
<div style="flex:1;min-height:0;display:flex;">
  <div style="flex:1;min-width:0;display:flex;flex-direction:column;background:#E9E1CC;">
    <div style="display:flex;align-items:center;gap:2px;height:44px;padding:0 24px;">
      <span style="display:inline-flex;align-items:center;gap:6px;height:28px;padding:0 10px;border:1px solid #CDC1A8;border-radius:4px;background:#FAF6EC;font-size:13px;font-weight:600;">Paragraph ${I.chev}</span>
      ${tbSep}${tb('<b>B</b>')}${tb('<i>I</i>')}${tb('<u>U</u>')}${tb('<s>S</s>')}${tb('&lt;/&gt;')}
      ${tbSep}${tb('H1')}${tb('H2')}${tb('❝')}${tb('•')}${tb('1.')}
      ${tbSep}${tb('Link')}${tb('Image')}${tb('Video')}${tb('Gallery')}${tb('Embed')}
      ${tbSep}${tb('Divider')}${tb('Spoiler')}${tb('Poll')}
      <div style="flex:1"></div>
      <span style="font-size:12px;color:#8C7F66;">RU · original</span>
      <span class="seg" style="margin-left:8px;"><span class="on">RU</span><span>EN</span></span>
    </div>
    <div style="flex:1;min-height:0;overflow:auto;padding:12px 24px 40px;">
      <article style="max-width:720px;margin:0 auto;background:#FAF6EC;border:1px solid #CDC1A8;border-radius:8px;box-shadow:0 1px 3px rgba(58,38,16,.14);padding:48px 64px 56px;font-family:'Literata',Georgia,serif;font-size:17px;line-height:1.75;color:#2C251A;">
        <h1 class="display" style="font-size:34px;line-height:1.15;margin:0 0 20px;">Coyote vs ACME</h1>
        <p style="margin:0 0 20px;">Сегодня сходили с друзьями на Coyote vs ACME. И я просто в восторге!</p>
        <figure style="margin:0 0 20px;">
          <div style="height:300px;border-radius:4px;background:linear-gradient(135deg,#1D2A3A 0%,#0F1720 60%,#3B2A18 100%);"></div>
          <figcaption style="font-family:'Source Sans 3',sans-serif;font-size:13px;color:#8C7F66;text-align:center;margin-top:8px;">Мне хочется скриншотить и выкладывать ВСЕ кадры с койотом :D</figcaption>
        </figure>
        <p style="margin:0 0 20px;">В меру драматичный, в меру трогательный, но самое главное очень смешной фильм! Не хочется на эмоциях говорить 10/10, но я получил ровно то, что ожидал.</p>
        <p style="margin:0;">Поэтому мой совет поддержать создателей и пойти в кино. Учитывая всю историю создания, будет очень грустно, если фильм провалится в прокате.</p>
      </article>
    </div>
  </div>
  <aside style="width:300px;flex:none;border-left:1px solid #CDC1A8;background:#F1EADA;overflow:auto;display:flex;flex-direction:column;">
    <div style="padding:16px 18px;border-bottom:1px solid #CDC1A8;display:flex;flex-direction:column;gap:12px;">
      <div class="label">Publishing</div>
      <div class="kv">
        <b>Status</b><span class="tag ok" style="justify-self:start;">Live</span>
        <b>Visibility</b><span>Public</span>
        <b>Blog</b><span style="display:flex;align-items:center;gap:6px;">coyote-vs-acme ${I.ext}</span>
        <b>Telegram</b><span style="display:flex;align-items:center;gap:6px;">@devdairydiaryen ${I.ext}</span>
        <b>X · Bluesky</b><span style="color:#8C7F66;">not sent</span>
      </div>
      <button class="btn sm" style="justify-content:center;">Share preview link</button>
    </div>
    <div style="padding:16px 18px;border-bottom:1px solid #CDC1A8;display:flex;flex-direction:column;gap:12px;">
      <div class="label">Document</div>
      <div class="kv">
        <b>Type</b><span class="field" style="height:28px;justify-content:space-between;">Regular post ${I.chev}</span>
        <b>Folder</b><span class="field" style="height:28px;">Random Thoughts</span>
        <b>Series</b><span style="color:#8C7F66;">—</span>
        <b>Tags</b><span style="display:flex;gap:6px;flex-wrap:wrap;"><span class="tag muted" style="text-transform:none;letter-spacing:0;">#life</span><span class="tag muted" style="text-transform:none;letter-spacing:0;">#movies</span><span style="color:#8C7F66;font-size:12px;">+ add</span></span>
        <b>Location</b><span>Portland, OR</span>
        <b>Slug</b><span class="mono" style="font-size:12px;">coyote-vs-acme</span>
      </div>
    </div>
    <div style="padding:16px 18px;display:flex;flex-direction:column;gap:12px;">
      <div class="label">Languages</div>
      <div style="display:flex;flex-direction:column;gap:6px;">
        <div style="display:flex;align-items:center;justify-content:space-between;padding:8px 10px;border:1px solid #CDC1A8;border-radius:4px;background:#FAF6EC;"><span style="font-weight:600;">Русский</span><span style="font-size:12px;color:#8C7F66;">original</span></div>
        <div style="display:flex;align-items:center;justify-content:space-between;padding:8px 10px;border:1px solid #CDC1A8;border-radius:4px;background:#FAF6EC;"><span style="font-weight:600;">English</span><span style="font-size:12px;color:#356842;">translated · in sync</span></div>
      </div>
      <a href="#" style="font-size:13px;font-weight:600;">+ Add language</a>
    </div>
  </aside>
</div>`;

/* ---------- Board ---------- */
const col = (name, count, inner) => `<div style="display:flex;flex-direction:column;min-height:0;background:#F1EADA;border:1px solid #CDC1A8;border-radius:8px;">
  <div style="display:flex;align-items:center;gap:8px;padding:12px 14px;"><span style="font-weight:700;">${name}</span><span style="font-size:12px;color:#8C7F66;font-weight:600;">${count}</span><div style="flex:1"></div><span style="color:#8C7F66;">${I.plus}</span></div>
  <div style="flex:1;padding:0 10px 10px;display:flex;flex-direction:column;gap:8px;">${inner}</div>
</div>`;
const emptyCol = (txt) => `<div class="empty" style="flex:1;border-color:#D9CFB4;padding:16px;font-size:13px;"><span>${txt}</span></div>`;
const boardBody = `
${header('Tasks', `<span>1 open</span><span style="color:#CDC1A8">·</span><span>Sprint S1 — Cedar Clerk Closed Beta Test</span><span style="color:#CDC1A8">·</span><span>28 days left</span>`,
  `<button class="btn">${I.flag}Sprints</button><button class="btn primary">${I.plus}New task</button>`)}
<div style="padding:0 40px 32px;flex:1;min-height:0;display:flex;flex-direction:column;gap:16px;">
  <div style="display:flex;align-items:center;gap:12px;">
    <div class="seg"><span class="on">Board</span><span>List</span></div>
    <div class="seg"><span class="on">S1</span><span>No sprint</span><span>All</span></div>
    <div style="flex:1"></div>
    <div class="input" style="width:240px;height:32px;">${I.search}<span>Search tasks</span></div>
  </div>
  <div style="flex:1;min-height:0;display:grid;grid-template-columns:repeat(4, minmax(0, 1fr));gap:16px;">
    ${col('Backlog', 0, emptyCol('Ideas and someday tasks land here'))}
    ${col('Planned', 0, emptyCol('Drag a task here to plan it for S1'))}
    ${col('In progress', 1, `<div class="card" style="padding:12px 14px;display:flex;flex-direction:column;gap:10px;">
      <div style="font-weight:600;font-size:15px;">Дописать пост про Mac Mini</div>
      <div style="display:flex;align-items:center;gap:8px;font-size:12px;color:#8C7F66;"><span class="tag warn" style="height:20px;">P2</span><span>S1</span><div style="flex:1"></div><span style="display:flex;align-items:center;gap:4px;">${I.doc} 1</span></div>
    </div>`)}
    ${col('Done', 0, emptyCol('Nothing finished yet in this sprint'))}
  </div>
</div>`;

/* ---------- Calendar ---------- */
const days = ['Mon','Tue','Wed','Thu','Fri','Sat','Sun'];
const cells = [];
const grid = [31,1,2,3,4,5,6, 7,8,9,10,11,12,13, 14,15,16,17,18,19,20, 21,22,23,24,25,26,27, 28,29,30,1,2,3,4];
grid.forEach((d, i) => {
  const out = (i === 0) || (i >= 31);
  const today = d === 1 && i === 1;
  let inner = '';
  if (d === 4 && !out) inner = `<div style="display:flex;align-items:center;gap:6px;padding:5px 8px;border-radius:4px;background:#E1E7DE;color:#356842;font-size:12px;font-weight:600;">${I.tg}<span style="flex:1;overflow:hidden;white-space:nowrap;text-overflow:ellipsis;">19:00 Coyote vs ACME</span></div>`;
  if (d === 8) inner = `<div style="display:flex;align-items:center;gap:6px;padding:5px 8px;border-radius:4px;border:1px dashed #8A6F4C;color:#5B513E;font-size:12px;font-weight:600;">${I.clock}<span>Weekly slot · 19:00</span></div>`;
  if (d === 15 || d === 22 || d === 29) inner = `<div style="display:flex;align-items:center;gap:6px;padding:5px 8px;border-radius:4px;border:1px dashed #CDC1A8;color:#8C7F66;font-size:12px;font-weight:600;">${I.clock}<span>Weekly slot · 19:00</span></div>`;
  cells.push(`<div style="display:flex;flex-direction:column;gap:6px;padding:8px;border-right:1px solid #E4DBC4;border-bottom:1px solid #E4DBC4;background:${out ? '#F1EADA' : '#FAF6EC'};min-height:0;">
    <span style="display:inline-flex;align-items:center;justify-content:center;width:26px;height:26px;border-radius:50%;font-size:13px;font-weight:700;${today ? 'background:#39543C;color:#FAF6EC;' : 'color:' + (out ? '#CDC1A8' : '#5B513E') + ';'}">${d}</span>
    ${inner}
  </div>`);
});
const calendarBody = `
${header('September 2026', `<span style="display:inline-flex;align-items:center;gap:6px;"><i style="width:8px;height:8px;border-radius:50%;background:#356842;"></i>Telegram</span><span style="display:inline-flex;align-items:center;gap:6px;"><i style="width:8px;height:8px;border-radius:50%;background:#3E5A76;"></i>Blog</span><span style="color:#CDC1A8">·</span><span>1 scheduled · 4 open slots</span>`,
  `<span class="seg"><span class="on">Month</span><span>Week</span><span>Queue</span></span><button class="btn">${I.left}</button><button class="btn">Today</button><button class="btn">${I.right}</button><button class="btn primary">${I.plus}Schedule</button>`)}
<div style="padding:0 40px 32px;flex:1;min-height:0;display:flex;flex-direction:column;">
  <div class="card" style="flex:1;min-height:0;display:grid;grid-template-rows:36px repeat(5, minmax(0,1fr));overflow:hidden;">
    <div style="display:grid;grid-template-columns:repeat(7, minmax(0,1fr));border-bottom:1px solid #CDC1A8;background:#F1EADA;">${days.map(d=>`<div class="label" style="display:flex;align-items:center;padding:0 12px;">${d}</div>`).join('')}</div>
    ${[0,1,2,3,4].map(r=>`<div style="display:grid;grid-template-columns:repeat(7, minmax(0,1fr));min-height:0;">${cells.slice(r*7, r*7+7).join('')}</div>`).join('')}
  </div>
  <div class="note" style="font-size:17px;color:#5B513E;margin-top:10px;transform:rotate(-1deg);transform-origin:left;">Drag a post onto a day to schedule it; dashed cards are your weekly queue slots.</div>
</div>`;

/* ---------- Direction B: low-fi sketch keeping the wood frame ---------- */
const sketchBody = `<div style="width:1440px;height:900px;background:#F3EDDE;display:flex;flex-direction:column;font-family:'Caveat',cursive;color:#3B2A18;">
  <div style="height:44px;background:#5A3F27;display:flex;align-items:center;padding:0 20px;color:#F3EDDE;font-size:22px;gap:20px;"><span>Cedar Clerk</span><span style="opacity:.6">/</span><span>Dev Dairy Diary</span><span style="opacity:.6">/</span><span>Dashboard</span><div style="flex:1"></div><span>bell · M</span></div>
  <div style="flex:1;display:flex;">
    <div style="width:64px;background:#6B4B2E;display:flex;flex-direction:column;align-items:center;gap:14px;padding-top:16px;color:#F3EDDE;font-size:15px;">${['Hub','Docs','Board','Cal','Assets','Posts','Stats'].map(l=>`<span style="width:44px;height:44px;border:2px solid #C9A784;border-radius:6px;display:flex;align-items:center;justify-content:center;">${l}</span>`).join('')}</div>
    <div style="flex:1;padding:24px;display:grid;grid-template-columns:2fr 1fr;grid-template-rows:auto 1fr;gap:20px;">
      <div style="grid-column:1/3;border:3px solid #6B4B2E;border-radius:6px;padding:16px 20px;font-size:28px;display:flex;align-items:center;gap:20px;">Dev Dairy Diary — Blog · 23 docs <div style="flex:1"></div><span style="border:3px solid #39543C;border-radius:6px;padding:2px 14px;">+ New document</span></div>
      <div style="border:3px solid #6B4B2E;border-radius:6px;padding:16px 20px;font-size:22px;display:flex;flex-direction:column;gap:10px;"><span>DOCUMENTS (fills the page height)</span>${Array(9).fill(0).map((_,i)=>`<span style="border-bottom:2px dashed #C9A784;padding:6px 0;">${i%3===0?'▣':'▤'} document title ............................ type · date</span>`).join('')}</div>
      <div style="display:flex;flex-direction:column;gap:20px;font-size:22px;">
        <div style="border:3px solid #6B4B2E;border-radius:6px;padding:16px;">Sprint S1 — progress bar<br>0 of 1 · 28 days</div>
        <div style="border:3px solid #6B4B2E;border-radius:6px;padding:16px;">Up next: task card</div>
        <div style="border:3px solid #6B4B2E;border-radius:6px;padding:16px;flex:1;">Links: blog, telegram,<br>public page</div>
      </div>
    </div>
  </div>
  <div style="height:36px;background:#5A3F27;color:#F3EDDE;display:flex;align-items:center;padding:0 20px;font-size:18px;gap:20px;"><span>console (collapsed by default)</span><div style="flex:1"></div><span>synced</span></div>
</div>`;


/* ---------- Editor: Preview tab ---------- */
const tgMsg = (lang, body, cap) => `<div style="width:360px;background:#1B2431;border-radius:12px;padding:14px;color:#E8EDF2;font-family:'Source Sans 3',sans-serif;font-size:14px;line-height:1.45;display:flex;flex-direction:column;gap:10px;">
  <div style="display:flex;align-items:center;gap:8px;"><span style="width:28px;height:28px;border-radius:50%;background:#B98A5E;"></span><span style="font-weight:700;">Dev Dairy Diary ${lang}</span><span style="color:#8FA0B3;font-size:12px;margin-left:auto;">19:00</span></div>
  <div style="font-weight:700;font-size:15px;">Coyote vs ACME</div>
  <div>${body[0]}</div>
  <div style="height:150px;border-radius:8px;background:linear-gradient(135deg,#1D2A3A,#0F1720 60%,#3B2A18);"></div>
  <div style="font-size:12px;color:#8FA0B3;margin-top:-4px;">${cap}</div>
  <div>${body[1]}</div>
  <div>${body[2]}</div>
  <div style="display:flex;gap:8px;font-size:12px;color:#8FA0B3;"><span>#life</span><span>#movies</span><span style="margin-left:auto;">42 views</span></div>
  <div style="display:flex;gap:8px;"><span style="flex:1;text-align:center;padding:8px;border-radius:8px;background:#2A3648;font-weight:600;">Read on the blog</span></div>
</div>`;
const ru = ['Сегодня сходили с друзьями на Coyote vs ACME. И я просто в восторге!','В меру драматичный, в меру трогательный, но самое главное очень смешной фильм! Не хочется на эмоциях говорить 10/10, но я получил ровно то, что ожидал.','Поэтому мой совет поддержать создателей и пойти в кино. Учитывая всю историю создания, будет очень грустно, если фильм провалится в прокате.'];
const en = ['Went to see Coyote vs ACME with friends today. I am simply delighted!','Dramatic in moderation, touching in moderation, but above all a very funny film. I do not want to say 10/10 on emotion alone, but I got exactly what I expected.','So my advice: support the creators and go to the cinema. Given the whole story of how it was made, it would be very sad if it flopped.'];
const previewBody = `
<div style="display:flex;align-items:center;gap:12px;height:56px;padding:0 20px;border-bottom:1px solid #CDC1A8;background:#F1EADA;">
  <a href="#" style="display:flex;align-items:center;gap:6px;color:#5B513E;font-weight:600;">${I.left}Documents</a>
  <span style="color:#CDC1A8">/</span>
  <span style="font-weight:700;">Coyote vs ACME</span>
  <span class="tag ok">Live</span>
  <div style="flex:1"></div>
  <div class="seg"><span>Write</span><span class="on">Preview</span><span>Publish</span><span>Stats</span></div>
  <div style="flex:1"></div>
  <span style="font-size:13px;color:#8C7F66;">Rendered from the saved document</span>
  <button class="btn sm">Send test to @testingandfun</button>
</div>
<div style="flex:1;min-height:0;display:flex;flex-direction:column;">
  <div style="display:flex;align-items:center;gap:12px;height:52px;padding:0 24px;">
    <div class="seg"><span class="on">Telegram</span><span>Blog</span><span>X</span><span>Bluesky</span><span>Discord</span></div>
    <div style="flex:1"></div>
    <span style="font-size:13px;color:#5B513E;">Languages</span>
    <div class="seg"><span class="on">RU + EN</span><span>RU</span><span>EN</span></div>
    <div class="seg" style="margin-left:8px;"><span class="on">Dark</span><span>Light</span></div>
  </div>
  <div style="flex:1;min-height:0;overflow:auto;display:flex;gap:32px;justify-content:center;align-items:flex-start;padding:16px 24px 40px;">
    <div style="display:flex;flex-direction:column;gap:10px;align-items:center;">
      <div class="label">Telegram · RU · @devdairydiary</div>
      ${tgMsg('', ru, 'Мне хочется скриншотить и выкладывать ВСЕ кадры с койотом :D')}
      <div style="font-size:12px;color:#8C7F66;">1 message · 2 blocks · 412 chars of 4096</div>
    </div>
    <div style="display:flex;flex-direction:column;gap:10px;align-items:center;">
      <div class="label">Telegram · EN · @devdairydiaryen</div>
      ${tgMsg('EN', en, 'I want to screenshot and post EVERY frame with the coyote :D')}
      <div style="font-size:12px;color:#8C7F66;">1 message · 2 blocks · 388 chars of 4096</div>
    </div>
    <div style="width:280px;display:flex;flex-direction:column;gap:12px;">
      <div class="card" style="padding:14px 16px;display:flex;flex-direction:column;gap:10px;">
        <div class="label">Preflight</div>
        <div style="display:flex;flex-direction:column;gap:8px;font-size:13px;">
          <span style="display:flex;gap:8px;"><i style="width:8px;height:8px;border-radius:50%;background:#356842;margin-top:5px;flex:none;"></i>Photo has a caption</span>
          <span style="display:flex;gap:8px;"><i style="width:8px;height:8px;border-radius:50%;background:#356842;margin-top:5px;flex:none;"></i>Both languages in sync</span>
          <span style="display:flex;gap:8px;"><i style="width:8px;height:8px;border-radius:50%;background:#7A5520;margin-top:5px;flex:none;"></i>No CTA button — the blog link is added automatically</span>
          <span style="display:flex;gap:8px;"><i style="width:8px;height:8px;border-radius:50%;background:#356842;margin-top:5px;flex:none;"></i>Under the 4096-char limit</span>
        </div>
      </div>
      <div class="card" style="padding:14px 16px;display:flex;flex-direction:column;gap:10px;">
        <div class="label">What differs per platform</div>
        <div class="kv" style="grid-template-columns:70px 1fr;">
          <b>Blog</b><span>full post, serif, comments</span>
          <b>X</b><span>thread of 2, 280 chars each</span>
          <b>Bluesky</b><span>thread of 2, 300 chars</span>
          <b>Discord</b><span style="color:#8C7F66;">not connected</span>
        </div>
      </div>
    </div>
  </div>
</div>`;


/* ---------- Shared: top bar, labelled rail, document tabs ---------- */
I.undo = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 14 4 9l5-5"/><path d="M4 9h11a5 5 0 0 1 0 10h-3"/></svg>';
I.redo = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m15 14 5-5-5-5"/><path d="M20 9H9a5 5 0 0 0 0 10h3"/></svg>';
I.ok = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="m8 12 3 3 5-6"/></svg>';
I.eye = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/></svg>';
I.upload = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 16V4M6 10l6-6 6 6"/><path d="M4 20h16"/></svg>';
I.desktop = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="12" rx="1.5"/><path d="M8 20h8M12 16v4"/></svg>';
I.mobile = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="7" y="3" width="10" height="18" rx="2"/><path d="M11 18h2"/></svg>';
I.x = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 4l16 16M20 4 4 20"/></svg>';
I.bsky = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 11c-1.5-3-5-7-8-7-1 0-1 1-1 3 0 4 2 7 6 7-3 1-4 3-2 5 2 1 4-1 5-4 1 3 3 5 5 4 2-2 1-4-2-5 4 0 6-3 6-7 0-2 0-3-1-3-3 0-6.5 4-8 7z"/></svg>';
I.sun = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>';
I.refresh = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 12a8 8 0 1 1-2.3-5.7"/><path d="M20 4v5h-5"/></svg>';
I.list = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M9 6h11M9 12h11M9 18h11M4 6h1M4 12h1M4 18h1"/></svg>';
I.link = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1"/><path d="M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1"/></svg>';

const topbar = () => `<div style="display:flex;align-items:center;gap:14px;height:56px;padding:0 20px;background:#F1EADA;border-bottom:1px solid #CDC1A8;flex:none;">
  <div style="display:flex;align-items:center;gap:8px;color:#39543C;">${I.tree}<span class="display" style="font-size:18px;font-weight:700;color:#2C251A;">Cedar Clerk</span></div>
  <span style="width:1px;height:22px;background:#CDC1A8;"></span>
  <span style="display:flex;align-items:center;gap:8px;font-weight:700;font-size:15px;">Dev Dairy Diary ${I.chev}</span>
  <span style="width:1px;height:22px;background:#CDC1A8;margin-left:8px;"></span>
  <span style="display:flex;gap:4px;color:#8C7F66;"><span style="width:30px;height:30px;display:flex;align-items:center;justify-content:center;border-radius:4px;">${I.undo}</span><span style="width:30px;height:30px;display:flex;align-items:center;justify-content:center;border-radius:4px;opacity:.5;">${I.redo}</span></span>
  <span style="display:flex;align-items:center;gap:8px;color:#356842;font-size:13px;">${I.ok}<span style="color:#5B513E;">Saved 2 min ago</span></span>
  <div style="flex:1"></div>
  <span style="display:flex;align-items:center;gap:8px;color:#5B513E;font-size:14px;">${I.cal}Sep 1, 2026</span>
  <span style="width:1px;height:22px;background:#CDC1A8;"></span>
  <span style="display:inline-flex;border-radius:4px;overflow:hidden;"><button class="btn primary" style="border-radius:4px 0 0 4px;">${I.upload}Publish</button><button class="btn primary" style="border-radius:0 4px 4px 0;border-left:1px solid rgba(250,246,236,.3);padding:0 10px;">${I.chev}</button></span>
</div>`;

const railLabeled = (active) => {
  const it = (key, icon, label) => `<a href="#" class="${active===key?'on':''}" style="flex-direction:column;gap:6px;height:64px;width:72px;padding:0;justify-content:center;font-size:12px;">${icon}<span>${label}</span></a>`;
  return `<aside class="nav" style="width:92px;flex:none;display:flex;flex-direction:column;align-items:center;gap:4px;padding:12px 0;background:#F1EADA;border-right:1px solid #CDC1A8;">
  ${it('hub', I.map, 'Hub')}${it('docs', I.doc, 'Docs')}${it('board', I.check, 'Board')}${it('planner', I.flag, 'Planner')}${it('assets', I.image, 'Assets')}${it('calendar', I.cal, 'Calendar')}
  <div style="flex:1"></div>${it('settings', I.gear, 'Settings')}
  <a href="#" style="width:72px;height:40px;justify-content:center;padding:0;border:1px solid #CDC1A8;background:#FAF6EC;">${I.right}</a>
</aside>`;
};

const docTabs = (active) => {
  const tab = (key, icon, label) => `<span style="display:flex;align-items:center;gap:8px;padding:0 4px 12px;font-size:15px;font-weight:600;border-bottom:2px solid ${active===key?'#39543C':'transparent'};color:${active===key?'#2C251A':'#5B513E'};">${icon}${label}</span>`;
  return `<div style="display:flex;gap:28px;padding:0 40px;border-bottom:1px solid #CDC1A8;">${tab('write', I.pen, 'Write')}${tab('preview', I.eye, 'Preview')}${tab('publish', I.upload, 'Publish')}</div>`;
};

const docHeader = (active) => `
<div style="padding:22px 40px 0;display:flex;align-items:flex-start;justify-content:space-between;gap:24px;">
  <div style="display:flex;flex-direction:column;gap:6px;">
    <div style="display:flex;align-items:center;gap:8px;font-size:14px;color:#5B513E;">${I.doc}<span>Blog</span><span style="color:#CDC1A8">·</span><span>23 documents</span></div>
    <div style="display:flex;align-items:center;gap:12px;"><h1 class="display" style="margin:0;font-size:30px;font-weight:700;line-height:1.1;">Coyote vs ACME</h1><span class="tag ok">Live</span></div>
  </div>
  <div style="display:flex;gap:8px;"><button class="btn">${I.dots}</button><button class="btn">Details</button></div>
</div>
<div style="height:18px;"></div>
${docTabs(active)}`;

const footer = (right) => `<div style="display:flex;align-items:center;gap:16px;height:60px;padding:0 40px;border-top:1px solid #CDC1A8;background:#F1EADA;flex:none;">
  <button class="btn">${I.link}Share preview</button>
  <div style="flex:1;text-align:center;font-size:13px;color:#8C7F66;">Last saved 2 min ago · 366 words</div>
  ${right}
</div>`;

/* ---------- Preview: destinations, checks ---------- */
const dest = (icon, name, status, sub, on, thumb) => `<div style="display:flex;align-items:center;gap:12px;padding:12px 14px;border-left:3px solid ${on?'#39543C':'transparent'};background:${on?'#E1E7DE':'transparent'};">
  <div style="flex:1;min-width:0;display:flex;flex-direction:column;gap:4px;">
    <div style="display:flex;align-items:center;gap:8px;font-weight:700;font-size:15px;">${icon}<span>${name}</span>${status}</div>
    <div style="font-size:13px;color:#5B513E;">${sub}</div>
  </div>
  <div style="width:56px;height:70px;border:1px solid #CDC1A8;border-radius:3px;background:#FAF6EC;padding:6px;display:flex;flex-direction:column;gap:3px;">${thumb}</div>
</div>`;
const thumbBlog = `<span style="height:5px;width:60%;background:#CDC1A8;"></span><span style="height:18px;background:#D9CFB4;"></span><span style="height:3px;background:#E4DBC4;"></span><span style="height:3px;background:#E4DBC4;"></span><span style="height:3px;width:70%;background:#E4DBC4;"></span>`;
const thumbTg = `<span style="height:10px;background:#D9CFB4;border-radius:2px;"></span><span style="height:3px;background:#E4DBC4;"></span><span style="height:3px;background:#E4DBC4;"></span><span style="height:8px;background:#D9CFB4;border-radius:2px;margin-top:4px;"></span><span style="height:3px;background:#E4DBC4;"></span>`;
const thumbEmpty = `<span style="height:3px;background:#E4DBC4;"></span><span style="height:3px;background:#E4DBC4;"></span><span style="flex:1;background:#F1EADA;"></span>`;
const ready = `<span class="tag ok" style="height:20px;font-size:11px;">Ready</span>`;
const warnDot = `<span style="width:18px;height:18px;border-radius:50%;background:#EBE1CE;color:#7A5520;display:inline-flex;align-items:center;justify-content:center;font-size:12px;font-weight:700;">!</span>`;
const destinations = (sel) => `<aside class="card" style="width:288px;flex:none;display:flex;flex-direction:column;overflow:hidden;">
  <div class="label" style="padding:16px 16px 10px;">Destinations</div>
  ${dest(I.globe, 'Blog', ready, 'Regular post · Public', sel==='blog', thumbBlog)}
  ${dest(I.send, 'Telegram', ready, 'Split into 2 messages', sel==='tg', thumbTg)}
  ${dest(I.x, 'X', warnDot, 'Not connected', false, thumbEmpty)}
  ${dest(I.bsky, 'Bluesky', warnDot, 'Not connected', false, thumbEmpty)}
  <div style="flex:1"></div>
  <a href="#" style="display:flex;align-items:center;gap:8px;padding:14px 16px;border-top:1px solid #E4DBC4;font-weight:600;color:#2C251A;">${I.gear}Manage destinations</a>
</aside>`;

const checkRow = (state, name, sub) => `<div style="display:flex;align-items:flex-start;gap:12px;padding:12px 0;border-bottom:1px solid #E4DBC4;">
  <span style="width:22px;height:22px;border-radius:50%;flex:none;display:flex;align-items:center;justify-content:center;background:${state==='ok'?'#356842':'#EBE1CE'};color:${state==='ok'?'#FAF6EC':'#7A5520'};font-size:12px;font-weight:700;">${state==='ok'?'<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12 5 5 9-10"/></svg>':'!'}</span>
  <div style="flex:1;display:flex;flex-direction:column;gap:2px;"><span style="font-weight:600;font-size:15px;">${name}</span><span style="font-size:13px;color:#5B513E;">${sub}</span></div>
  <span style="color:#8C7F66;">${I.right}</span>
</div>`;
const checks = (title, rows) => `<aside class="card" style="width:264px;flex:none;display:flex;flex-direction:column;padding:16px 16px 12px;">
  <div style="display:flex;align-items:center;justify-content:space-between;"><span class="label">Checks · ${title}</span><span style="color:#8C7F66;">${I.refresh}</span></div>
  <div style="display:flex;flex-direction:column;margin-top:4px;">${rows}</div>
  <div style="flex:1"></div>
  <button class="btn" style="justify-content:center;">${I.list}View all details</button>
</aside>`;

const previewToolbar = (mode, widthLabel) => `<div style="display:flex;align-items:center;gap:12px;padding:12px 16px;border-bottom:1px solid #E4DBC4;">
  <div class="seg"><span class="${mode==='desktop'?'on':''}" style="display:flex;align-items:center;gap:6px;">${I.desktop}Desktop</span><span class="${mode==='mobile'?'on':''}" style="display:flex;align-items:center;gap:6px;">${I.mobile}Mobile</span></div>
  <div style="flex:1"></div>
  <div style="display:flex;align-items:center;gap:12px;color:#8C7F66;">${I.left}<span class="mono" style="font-size:13px;color:#5B513E;">${widthLabel}</span>${I.right}</div>
  <div style="flex:1"></div>
  <div class="seg"><span class="on">RU</span><span>EN</span></div>
  <span style="color:#8C7F66;">${I.sun}</span>
</div>`;

const blogPage = `<div style="max-width:720px;margin:0 auto;padding:28px 0 40px;font-family:'Literata',Georgia,serif;color:#2C251A;">
  <div style="display:flex;align-items:center;gap:20px;font-family:'Source Sans 3',sans-serif;font-size:14px;color:#5B513E;padding-bottom:20px;border-bottom:1px solid #E4DBC4;margin-bottom:24px;">
    <span class="display" style="font-size:17px;font-weight:700;color:#2C251A;">Dev Dairy Diary</span><div style="flex:1"></div><span>Posts</span><span>About</span><span>Archive</span><span style="color:#8C7F66;">${I.sun}</span>
  </div>
  <div class="mono" style="font-size:11px;letter-spacing:.08em;color:#8C7F66;text-transform:uppercase;">Aug 30, 2026 · 4 min read</div>
  <h1 class="display" style="font-size:34px;line-height:1.15;margin:10px 0 12px;">Coyote vs ACME</h1>
  <p style="margin:0 0 18px;font-size:17px;line-height:1.6;color:#5B513E;">Сегодня сходили с друзьями на Coyote vs ACME. И я просто в восторге!</p>
  <div style="height:300px;border-radius:4px;background:linear-gradient(135deg,#1D2A3A 0%,#0F1720 60%,#3B2A18 100%);"></div>
  <div style="font-size:13px;color:#8C7F66;text-align:center;margin:8px 0 20px;font-style:italic;">Мне хочется скриншотить и выкладывать все кадры с койотом :D</div>
  <p style="margin:0 0 18px;font-size:17px;line-height:1.75;">В меру драматичный, в меру трогательный, но самое главное очень смешной фильм! Не хочется на эмоциях говорить 10/10, но я получил ровно то, что ожидал.</p>
  <p style="margin:0;font-size:17px;line-height:1.75;">Поэтому мой совет поддержать создателей и пойти в кино. Учитывая всю историю создания, будет очень грустно, если фильм провалится в прокате.</p>
</div>`;

const previewBlogBody = `
${topbar()}
<div style="flex:1;min-height:0;display:flex;">
${railLabeled('docs')}
<main style="flex:1;min-width:0;display:flex;flex-direction:column;background:#E9E1CC;">
  ${docHeader('preview')}
  <div style="flex:1;min-height:0;display:flex;gap:16px;padding:16px 40px;">
    ${destinations('blog')}
    <section class="card" style="flex:1;min-width:0;display:flex;flex-direction:column;overflow:hidden;">
      ${previewToolbar('desktop', '720px')}
      <div style="flex:1;min-height:0;overflow:auto;padding:0 32px;">${blogPage}</div>
    </section>
    ${checks('Blog', checkRow('ok','Title','Looks good') + checkRow('ok','Hero image','Image found · 1200×628') + checkRow('ok','Slug','coyote-vs-acme') + checkRow('ok','Visibility','Public') + checkRow('ok','Language','Russian (RU) · English in sync') + checkRow('warn','Link checks','1 warning found'))}
  </div>
  ${footer(`<button class="btn primary">Continue to Publish ${I.right}</button>`)}
</main>
</div>`;

const tgBubble = (inner) => `<div style="background:#FAF6EC;border-radius:12px 12px 12px 4px;padding:10px 12px;font-size:14px;line-height:1.45;color:#2C251A;display:flex;flex-direction:column;gap:8px;box-shadow:0 1px 2px rgba(0,0,0,.12);">${inner}</div>`;
const phone = `<div style="width:390px;margin:20px auto;border:8px solid #2C251A;border-radius:36px;background:#D9CFB4;overflow:hidden;display:flex;flex-direction:column;height:720px;">
  <div style="display:flex;align-items:center;gap:10px;padding:14px 14px 10px;background:#F1EADA;border-bottom:1px solid #CDC1A8;">
    <span style="color:#5B513E;">${I.left}</span>
    <span style="width:34px;height:34px;border-radius:50%;background:#B98A5E;"></span>
    <div style="display:flex;flex-direction:column;"><span style="font-weight:700;">Dev Dairy Diary</span><span style="font-size:12px;color:#8C7F66;">1 234 subscribers</span></div>
  </div>
  <div style="flex:1;padding:12px;display:flex;flex-direction:column;gap:10px;background:#E9E1CC;overflow:hidden;">
    <div style="align-self:center;font-size:11px;color:#8C7F66;background:#F1EADA;padding:2px 8px;border-radius:10px;">Today</div>
    ${tgBubble(`<span style="font-weight:700;">Coyote vs ACME</span><span>Сегодня сходили с друзьями на Coyote vs ACME. И я просто в восторге!</span><div style="height:180px;border-radius:8px;background:linear-gradient(135deg,#1D2A3A,#0F1720 60%,#3B2A18);"></div><span style="font-size:12px;color:#8C7F66;">Мне хочется скриншотить и выкладывать ВСЕ кадры с койотом :D</span><span style="align-self:flex-end;font-size:11px;color:#8C7F66;">19:00</span>`)}
    ${tgBubble(`<span>В меру драматичный, в меру трогательный, но самое главное очень смешной фильм! Не хочется на эмоциях говорить 10/10, но я получил ровно то, что ожидал.</span><span>Поэтому мой совет поддержать создателей и пойти в кино.</span><span style="display:flex;gap:8px;font-size:13px;color:#39543C;">#life #movies</span><div style="text-align:center;padding:8px;border-radius:6px;background:#E1E7DE;color:#39543C;font-weight:600;">Read on the blog</div><span style="align-self:flex-end;font-size:11px;color:#8C7F66;">19:00</span>`)}
  </div>
</div>`;

const previewTgBody = `
${topbar()}
<div style="flex:1;min-height:0;display:flex;">
${railLabeled('docs')}
<main style="flex:1;min-width:0;display:flex;flex-direction:column;background:#E9E1CC;">
  ${docHeader('preview')}
  <div style="flex:1;min-height:0;display:flex;gap:16px;padding:16px 40px;">
    ${destinations('tg')}
    <section class="card" style="flex:1;min-width:0;display:flex;flex-direction:column;overflow:hidden;">
      ${previewToolbar('mobile', '390px')}
      <div style="flex:1;min-height:0;overflow:auto;">${phone}</div>
    </section>
    ${checks('Telegram', checkRow('ok','Channel','@devdairydiary · bot can post') + checkRow('ok','Length','2 messages · 412 of 4096 chars') + checkRow('ok','Media','Photo cached, sent by file_id') + checkRow('warn','CTA button','None — blog link added as text') + checkRow('ok','Schedule','Thu 4 Sep, 19:00 slot') + checkRow('ok','English copy','@devdairydiaryen · in sync'))}
  </div>
  ${footer(`<button class="btn">Send test to @testingandfun</button><button class="btn primary">Continue to Publish ${I.right}</button>`)}
</main>
</div>`;

/* ---------- Editor with the same header ---------- */
const editorBody2 = `
${topbar()}
<div style="flex:1;min-height:0;display:flex;">
${railLabeled('docs')}
<main style="flex:1;min-width:0;display:flex;flex-direction:column;background:#E9E1CC;">
  ${docHeader('write')}
  <div style="flex:1;min-height:0;display:flex;gap:16px;padding:16px 40px;">
    <section class="card" style="flex:1;min-width:0;display:flex;flex-direction:column;overflow:hidden;">
      <div style="display:flex;align-items:center;gap:2px;padding:8px 12px;border-bottom:1px solid #E4DBC4;">
        <span style="display:inline-flex;align-items:center;gap:6px;height:28px;padding:0 10px;border:1px solid #CDC1A8;border-radius:4px;background:#FAF6EC;font-size:13px;font-weight:600;">Paragraph ${I.chev}</span>
        ${tbSep}${tb('<b>B</b>')}${tb('<i>I</i>')}${tb('<u>U</u>')}${tb('<s>S</s>')}${tb('&lt;/&gt;')}
        ${tbSep}${tb('H1')}${tb('H2')}${tb('❝')}${tb('•')}${tb('1.')}
        ${tbSep}${tb('Link')}${tb('Image')}${tb('Video')}${tb('Gallery')}${tb('Embed')}
        ${tbSep}${tb('Divider')}${tb('Spoiler')}${tb('Poll')}
        <div style="flex:1"></div>
        <div class="seg"><span class="on">RU</span><span>EN</span></div>
      </div>
      <div style="flex:1;min-height:0;overflow:auto;padding:32px 24px 40px;">
        <article style="max-width:680px;margin:0 auto;font-family:'Literata',Georgia,serif;font-size:17px;line-height:1.75;color:#2C251A;">
          <p style="margin:0 0 20px;">Сегодня сходили с друзьями на Coyote vs ACME. И я просто в восторге!</p>
          <figure style="margin:0 0 20px;">
            <div style="height:300px;border-radius:4px;background:linear-gradient(135deg,#1D2A3A 0%,#0F1720 60%,#3B2A18 100%);"></div>
            <figcaption style="font-family:'Source Sans 3',sans-serif;font-size:13px;color:#8C7F66;text-align:center;margin-top:8px;">Мне хочется скриншотить и выкладывать ВСЕ кадры с койотом :D</figcaption>
          </figure>
          <p style="margin:0 0 20px;">В меру драматичный, в меру трогательный, но самое главное очень смешной фильм! Не хочется на эмоциях говорить 10/10, но я получил ровно то, что ожидал.</p>
          <p style="margin:0;">Поэтому мой совет поддержать создателей и пойти в кино. Учитывая всю историю создания, будет очень грустно, если фильм провалится в прокате.</p>
        </article>
      </div>
    </section>
    <aside class="card" style="width:288px;flex:none;overflow:auto;display:flex;flex-direction:column;">
      <div style="padding:16px 16px;border-bottom:1px solid #E4DBC4;display:flex;flex-direction:column;gap:12px;">
        <div class="label">Document</div>
        <div class="kv">
          <b>Type</b><span class="field" style="height:28px;justify-content:space-between;">Regular post ${I.chev}</span>
          <b>Folder</b><span class="field" style="height:28px;">Random Thoughts</span>
          <b>Series</b><span style="color:#8C7F66;">—</span>
          <b>Tags</b><span style="display:flex;gap:6px;flex-wrap:wrap;"><span class="tag muted" style="text-transform:none;letter-spacing:0;">#life</span><span class="tag muted" style="text-transform:none;letter-spacing:0;">#movies</span><span style="color:#8C7F66;font-size:12px;">+ add</span></span>
          <b>Location</b><span>Portland, OR</span>
          <b>Slug</b><span class="mono" style="font-size:12px;">coyote-vs-acme</span>
        </div>
      </div>
      <div style="padding:16px 16px;border-bottom:1px solid #E4DBC4;display:flex;flex-direction:column;gap:12px;">
        <div class="label">Languages</div>
        <div style="display:flex;flex-direction:column;gap:6px;">
          <div style="display:flex;align-items:center;justify-content:space-between;padding:8px 10px;border:1px solid #CDC1A8;border-radius:4px;background:#FAF6EC;"><span style="font-weight:600;">Русский</span><span style="font-size:12px;color:#8C7F66;">original</span></div>
          <div style="display:flex;align-items:center;justify-content:space-between;padding:8px 10px;border:1px solid #CDC1A8;border-radius:4px;background:#FAF6EC;"><span style="font-weight:600;">English</span><span style="font-size:12px;color:#356842;">in sync</span></div>
        </div>
        <a href="#" style="font-size:13px;font-weight:600;">+ Add language</a>
      </div>
      <div style="padding:16px 16px;display:flex;flex-direction:column;gap:12px;">
        <div class="label">Outline</div>
        <div style="display:flex;flex-direction:column;gap:4px;font-size:13px;color:#5B513E;">
          <span style="padding:6px 8px;border-radius:4px;background:#E1E7DE;color:#2C251A;">1 Сегодня сходили с друзьями…</span>
          <span style="padding:6px 8px;">2 Фото · подпись</span>
          <span style="padding:6px 8px;">3 В меру драматичный…</span>
          <span style="padding:6px 8px;">4 Поэтому мой совет…</span>
        </div>
      </div>
    </aside>
  </div>
  ${footer(`<button class="btn primary">Preview ${I.right}</button>`)}
</main>
</div>`;

const files = {
  'Main.dc.html': shell(sidebar('docs'), mainBody),
  'Hub.dc.html': shell(sidebar('hub'), hubBody),
  'Editor.dc.html': `${HEAD}
<div style="width:1440px;height:900px;display:flex;flex-direction:column;background:#E9E1CC;overflow:hidden;">
${editorBody2}
</div>
${TAIL}`,
  'Preview.dc.html': `${HEAD}
<div style="width:1440px;height:900px;display:flex;flex-direction:column;background:#E9E1CC;overflow:hidden;">
${previewBlogBody}
</div>
${TAIL}`,
  'PreviewTelegram.dc.html': `${HEAD}
<div style="width:1440px;height:900px;display:flex;flex-direction:column;background:#E9E1CC;overflow:hidden;">
${previewTgBody}
</div>
${TAIL}`,
  'Board.dc.html': shell(sidebar('board'), boardBody),
  'Calendar.dc.html': shell(sidebar('calendar'), calendarBody),
  'DirectionB.dc.html': `${HEAD}
${sketchBody}
${TAIL}`,
};
for (const [name, src] of Object.entries(files)) writeFileSync(new URL(name, import.meta.url), src);

writeFileSync(new URL('canvas.json', import.meta.url), JSON.stringify({
  artboards: [
    { file: 'Hub.dc.html',      x: 0,    y: 0,    w: 1440, h: 900, title: 'Projects hub' },
    { file: 'Main.dc.html',     x: 1540, y: 0,    w: 1440, h: 900, title: 'Project dashboard' },
    { file: 'Editor.dc.html',   x: 3080, y: 0,    w: 1440, h: 900, title: 'Editor' },
    { file: 'Preview.dc.html',  x: 4620, y: 0,    w: 1440, h: 900, title: 'Preview — Blog' },
    { file: 'PreviewTelegram.dc.html', x: 6160, y: 0, w: 1440, h: 900, title: 'Preview — Telegram' },
    { file: 'Board.dc.html',    x: 0,    y: 1040, w: 1440, h: 900, title: 'Tasks board' },
    { file: 'Calendar.dc.html', x: 1540, y: 1040, w: 1440, h: 900, title: 'Calendar' },
    { file: 'DirectionB.dc.html', x: 3080, y: 1040, w: 1440, h: 900, title: 'Direction B — wood kept, scaled (sketch)' },
  ],
  annotations: [
    { id: 'brief', x: 0, y: -260, w: 620, text: 'Direction A — "Paper first".\nThe wood frame is gone: one paper canvas, a real sidebar with grouped, labelled navigation (Write / Plan / Ship), a page header with the primary action, and content that fills the viewport (boards, calendar rows, document lists stretch). Console and status bars are removed from the working screens. Every empty state says what to do next.' },
    { id: 'editor-tabs', x: 3080, y: -220, w: 660, text: 'One document, three tabs: Write, Preview, Publish. The top bar is shared (project switcher, undo/redo, saved state, date, Publish). Preview = destinations on the left (with readiness), the rendered page in the middle (desktop/mobile, RU/EN), checks for the selected destination on the right. Selecting Telegram swaps the render to a phone with the real message split and the checks to Telegram ones. Footer carries the next step.' },
    { id: 'alt', x: 3080, y: 2000, w: 520, text: 'Direction B keeps the wood chrome and only fixes scaling and empty states. Cheaper to build, but the frame stays the loudest thing on screen.' },
  ],
  launch: { view: 'canvas' },
}, null, 2));
console.log('built', Object.keys(files).join(', '));
