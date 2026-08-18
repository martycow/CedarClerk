# INPUT PROMPT FOR COWTEXT

Here lives a dynamic prompt for using inside Claude Code.
Consider everything below as a new prompt every time. It may or may not change. You can't tell before you read.

=== Everything below considered as a prompt ===

08.18.2026 1:26PM

# Cedar Clerk — Big Feature Scope (большой скоуп фич) v1

> **Статус:** brainstorm / candidate backlog (кандидатный бэклог). Не ADR. Ничего не считается решённым, пока не попало в `docs/DECISIONS.md`.
> **Дата:** 2026-07-29
> **Фокус автора:** публикация ГДД (GDD, game design document), личные посты в Telegram и на сайте, фотоконтент.
> **Второй фокус:** сделать продукт, который выглядит как инвестируемый (investable), а не как персональная утилита.

---

## 0. Как читать документ

Каждая фича имеет ID вида `GDD-03`, `MED-07` — на них можно ссылаться в `DECISIONS.md`, `TASKS.md` и в брифах для Claude Code.

Метки приоритета:

| Метка | Значение |
|---|---|
| **P0-me** | Нужно лично тебе для твоего сценария (ГДД + личный блог + фото). Без этого продукт для тебя неполный. |
| **P1-product** | Нужно продукту, чтобы за него платили посторонние люди. |
| **P2-moat** | Долгосрочное преимущество (moat, ров), то, что тяжело скопировать. |
| **P3-wow** | Демо-эффект для инвестора / для лендинга, но не критично для работы. |
| **later** | Осознанно отложено. |

Метка сложности: **S** (≤1 сессия), **M** (2–5 сессий), **L** (неделя+), **XL** (отдельная фаза).

---

## 1. Позиционирование, из которого растёт весь скоуп

Сейчас Cedar Clerk описан как *multichannel write-once-publish-everywhere platform* (мультиканальная платформа «написал один раз — опубликовал везде»). Это правда, но это **слишком общая** формулировка: в этой категории уже сидят Buffer, Publer, Hypefury, Postiz. Инвестору такое продавать тяжело.

Твой реальный, честный, ни на кого не похожий wedge (клин, точка входа):

> **Cedar Clerk — рабочее место инди-разработчика для того, чтобы превращать процесс разработки в аудиторию.**
> Документация проекта (ГДД), девлоги, личный блог, фото/видео с процесса, комьюнити и монетизация — в одном месте, с публикацией в Telegram / веб / RSS / соцсети.

Почему это сильнее:

- Никто не совмещает **документацию проекта** и **паблишинг**. Notion умеет документы, но не умеет постить в Telegram. Buffer умеет постить, но ничего не знает про твой проект. Ghost/Substack — блог без Telegram и без ГДД.
- У тебя есть **личный дистрибуционный канал** для догфудинга (Dev Dairy Diary, YouTube), то есть ты одновременно первый пользователь и первый маркетинговый кейс.
- Инди-геймдев — это ниша с высокой потребностью в «показывать процесс» и с готовностью платить за инструменты (они уже платят за Unity, Blender, Substance, Steam).

Всё, что ниже, написано **под это позиционирование**. Оно не отменяет мультиканальность — оно даёт ей причину существовать.

---

## 2. Блок GDD — документация проекта как первоклассная сущность

Сейчас в Cedar Clerk есть посты и черновики. Для ГДД этого мало: ГДД — это дерево, оно живёт годами, оно частично публичное и частично секретное.

| ID | Фича | Приоритет | Сложность |
|---|---|---|---|
| GDD-01 | **Document tree** (дерево документов): вложенные страницы, drag&drop переупорядочивание, breadcrumbs (хлебные крошки) | P0-me | M |
| GDD-02 | **Wiki-links** (вики-ссылки) `[[Название]]` с автодополнением + панель **backlinks** (обратные ссылки) | P0-me, P2-moat | M |
| GDD-03 | **Version history + diff viewer** (история версий и просмотр различий), восстановление любой ревизии | P0-me, P1-product | M |
| GDD-04 | **Block-level visibility** (видимость на уровне блока): каждый блок помечается `public / patrons / private`. Один документ — три разные публичные проекции. Это **ключевая фича** для «делюсь ГДД, но без спойлеров» | P0-me, P2-moat | L |
| GDD-05 | **Public snapshots** (публичные снапшоты): «ГДД v0.3» замораживается и публикуется по стабильной ссылке, а рабочая копия живёт дальше | P0-me | M |
| GDD-06 | **Entity database** (база сущностей): типизированные коллекции — NPC, items, locations, quests, systems — с полями, фильтрами и вставкой карточки сущности в любой пост | P0-me, P2-moat | XL |
| GDD-07 | **Doc templates** (шаблоны документов): feature spec, system design, quest design, postmortem | P1-product | S |
| GDD-08 | **Diagrams**: Mermaid + Excalidraw-эмбед прямо в документ (флоу боя, стейт-машины, экономика) | P0-me, P3-wow | M |
| GDD-09 | **Auto table of contents** (автооглавление) + якорные ссылки на заголовки | P0-me | S |
| GDD-10 | **Inline comments / annotations** (инлайн-комментарии): выделил кусок ГДД → оставил комментарий. Для себя, для тестеров, для будущей команды | P1-product | M |
| GDD-11 | **Glossary** (глоссарий) с тултипами: термин из глоссария подсвечивается везде и объясняется при наведении | P2-moat | M |
| GDD-12 | **Doc status** (статус документа): draft / WIP / approved / deprecated + фильтрация по статусу | P1-product | S |
| GDD-13 | **Full-text search** (полнотекстовый поиск) по всем документам и постам (SQLite FTS5) | P0-me | M |
| GDD-14 | **Import**: Markdown-бандл, Notion export, Google Docs, Confluence | P1-product | M |
| GDD-15 | **Export**: PDF (pitch-ready ГДД), Markdown-бандл, статический сайт | P1-product | M |
| GDD-16 | **Parallel localization** (параллельная локализация): RU/EN версии одного документа связаны, видно рассинхрон | P0-me, P1-product | L |
| GDD-17 | **Git sync** (двусторонняя синхронизация с GitHub-репозиторием): ГДД лежит в репо как Markdown, правится и в вебе, и в VS Code | P2-moat, P3-wow | XL |
| GDD-18 | **Devlog from diff** (девлог из диффа): «что изменилось в ГДД за неделю» → черновик поста | P2-moat, P3-wow | M |

> **Комментарий.** GDD-04 + GDD-05 + GDD-18 — это и есть та связка, ради которой продукт стоит строить. «Публикую живой дизайн-документ, аудитория видит его эволюцию, из эволюции автоматически рождается контент». Такого нет ни у кого.

---

## 3. Блок MEDIA — фото, видео, ассеты

У тебя гигабайты видео и постоянный поток скриншотов. Сейчас медиа — самая слабая часть системы.

| ID | Фича | Приоритет | Сложность |
|---|---|---|---|
| MED-01 | **Media library** (медиатека): папки, теги, поиск, повторное использование одного файла в разных постах | P0-me | M |
| MED-02 | **Paste & drag&drop upload** прямо в TipTap, с прогрессом и отменой | P0-me | S |
| MED-03 | **Image pipeline** (конвейер изображений): автоконверт в WebP/AVIF, thumbnails, `srcset` для адаптивности, ленивая загрузка | P0-me, P1-product | M |
| MED-04 | **EXIF stripping** (удаление EXIF), включая GPS-координаты. Для личного блога это вопрос безопасности, а не гигиены | P0-me | S |
| MED-05 | **Albums / galleries** (альбомы и галереи): grid, masonry, lightbox, свайп на мобильном | P0-me | M |
| MED-06 | **Telegram media groups**: альбом на сайте → альбом (media group) в Telegram, а не десять отдельных сообщений | P0-me | M |
| MED-07 | **External storage offload** (вынос хранилища): S3/Cloudflare R2/B2 вместо диска Raspberry Pi. Без этого мультитенантность умрёт от нехватки места | P1-product | M |
| MED-08 | **Media quota per plan** (квота медиа по тарифу) + индикатор занятого места | P1-product | S |
| MED-09 | **Before/after slider** (слайдер «до/после») — блок для прогресса моделинга, ретопологии, текстур | P0-me, P3-wow | S |
| MED-10 | **3D model viewer** (просмотрщик 3D-моделей): glTF/GLB прямо в посте, вращение мышкой. Для геймдев-ниши это визитная карточка | P2-moat, P3-wow | M |
| MED-11 | **Video embeds** (встраивание видео): YouTube с таймкодами, главами и авто-подтягиванием обложки | P0-me | S |
| MED-12 | **Screenshot annotation** (аннотация скриншотов): стрелки, рамки, замазывание — встроенный редактор | P1-product | M |
| MED-13 | **Watermarking** (водяные знаки), опционально по пресету | P1-product | S |
| MED-14 | **AI alt-text** (AI-описания для доступности), кредитируемый | P1-product | S |
| MED-15 | **Audio block** (аудиоблок): плеер для музыки, войс-заметок, саундтрека | P0-me | S |
| MED-16 | **Asset timeline** (лента ассетов): все загруженные изображения по датам — визуальная история проекта, из неё легко собирать ретроспективы | P3-wow | M |

---

## 4. Блок PUBLISH — публикация и мультиканальность

| ID | Фича | Приоритет | Сложность |
|---|---|---|---|
| PUB-01 | **Content calendar** (контент-календарь): месяц/неделя, drag&drop переноса запланированных постов | P0-me, P3-wow | M |
| PUB-02 | **Per-channel variants** (варианты под канал): один источник → адаптированные версии для Telegram, блога, X, Bluesky, Mastodon, Discord, VK, с превью каждой | P1-product | L |
| PUB-03 | **Edit-after-publish sync** (синхронизация после публикации): правишь пост → обновляется и блог, и уже отправленное сообщение в Telegram (`editMessageText`) | P0-me | M |
| PUB-04 | **Post series** (серии постов): «Devlog #1..#N» с автонавигацией «предыдущий/следующий» и страницей серии | P0-me | M |
| PUB-05 | **Teaser generation** (генерация тизера): длинный пост → короткий анонс со ссылкой для быстрых площадок | P1-product | S |
| PUB-06 | **Telegram rich features**: опросы, квизы, inline-кнопки, тихая отправка, закрепление, предзаданные наборы реакций | P0-me | M |
| PUB-07 | **Link shortener + UTM** (сокращатель ссылок с UTM-метками) и подсчёт кликов | P1-product | M |
| PUB-08 | **Optimal time suggestion** (подсказка лучшего времени) на основе собственной аналитики канала | P1-product, P3-wow | M |
| PUB-09 | **Evergreen queue** (очередь вечнозелёного контента): пул постов, которые система периодически переопубликовывает | P1-product | M |
| PUB-10 | **Weekly digest** (недельный дайджест): автосборка «что было на неделе» из постов, коммитов и изменений ГДД | P2-moat, P3-wow | M |
| PUB-11 | **Approval workflow** (согласование): черновик → ревью → публикация. Нужно, когда появятся команды | P1-product | M |
| PUB-12 | **Failure handling** (обработка сбоев): ретраи, dead-letter очередь, понятное «почему не улетело», ручной повтор | P1-product | M |
| PUB-13 | **Cross-platform threading** (тредирование): длинный текст → тред в X/Mastodon с автонарезкой | later | M |
| PUB-14 | **Timezone-aware scheduling** (планирование с учётом часовых поясов) — обязательно до выхода за пределы РФ/США | P1-product | S |

---

## 5. Блок WEB — публичный сайт/блог тенанта

| ID | Фича | Приоритет | Сложность |
|---|---|---|---|
| WEB-01 | **SSR / pre-render для OG-тегов** (серверный рендеринг для превью в соцсетях) — уже опознано как блокер | P0-me | M |
| WEB-02 | **Custom domain per tenant** (свой домен у тенанта) с автоматическим TLS | P1-product | L |
| WEB-03 | **Themes** (темы оформления): 3–5 пресетов + токены цвета/шрифта, светлая и тёмная | P0-me, P3-wow | M |
| WEB-04 | **Post types** (типы постов): длинный текст, микрозаметка, фотопост, ссылка, девлог, док. Разная вёрстка и разная лента | P0-me | M |
| WEB-05 | **RSS/JSON Feed/Atom** — уже в плане v0.8.0, подтверждаю приоритет | P0-me | S |
| WEB-06 | **Email newsletter** (email-рассылка): подписка с double opt-in, дайджесты, отписка. Это то, чем владеешь ты, а не Telegram | P1-product, P2-moat | L |
| WEB-07 | **Web Push / Telegram-уведомления** о новых постах | P1-product | M |
| WEB-08 | **Site search** (поиск по сайту) | P0-me | S |
| WEB-09 | **Archive & tag landings** (архив по датам и лендинги тегов) | P0-me | S |
| WEB-10 | **Static pages** (статические страницы): About, Now, Links (замена Linktree), Press kit | P0-me | S |
| WEB-11 | **Press kit generator** (генератор пресс-кита): логотипы, скриншоты, факт-лист, контакты — одной кнопкой из ГДД и медиатеки. Геймдевам это нужно всегда и делать это никто не любит | P2-moat, P3-wow | M |
| WEB-12 | **i18n switcher** (переключатель языков) RU/EN на публичной части | P0-me | M |
| WEB-13 | **Accessibility pass** (доступность): контраст, клавиатура, alt, семантика | P1-product | M |
| WEB-14 | **Performance budget** (бюджет производительности): целевые Core Web Vitals, потому что Pi + Angular — рискованная связка | P1-product | M |

---

## 6. Блок COMMUNITY — аудитория и обратная связь

| ID | Фича | Приоритет | Сложность |
|---|---|---|---|
| COM-01 | **Comments v2**: модерация, бан, шэдоубан, антиспам, резервирование ников | P0-me | M |
| COM-02 | **Reactions** (реакции) на постах блога, синхронизированные с реакциями Telegram | P0-me | M |
| COM-03 | **Playtest feedback intake** (приём фидбэка от тестеров): форма → лёгкий трекер задач со статусами | P2-moat | L |
| COM-04 | **In-game bug report widget** (виджет баг-репортов из игры): HTTP-эндпоинт + Unity-сниппет, репорт со скриншотом и логом падает прямо в Cedar Clerk | P2-moat, P3-wow | L |
| COM-05 | **Polls & surveys** (опросы и анкеты) с агрегацией результатов | P1-product | M |
| COM-06 | **AMA / Q&A collection** (сбор вопросов) → пост с ответами | P1-product | S |
| COM-07 | **Discord integration** (интеграция с Discord): вебхуки, зеркалирование девлогов | P1-product | S |
| COM-08 | **Wishlist / Steam CTA blocks** (блоки призыва к действию) с подсчётом кликов | P1-product | S |
| COM-09 | **Comment translation** (перевод комментариев) — уже в планах, обязательно за кредитами | P1-product | M |

---

## 7. Блок MONETIZE — монетизация **для твоих пользователей**, а не только для тебя

Это отдельный, стратегически важный блок. Продукт, который **приносит пользователю деньги**, продаётся радикально легче, чем продукт, который экономит время.

| ID | Фича | Приоритет | Сложность |
|---|---|---|---|
| MON-01 | **Paid memberships** (платные подписки на блог): тиры, платный контент, гейт на уровне поста | P2-moat | XL |
| MON-02 | **Paywalled blocks** (платные блоки) — переиспользует механику GDD-04 (`patrons`) | P2-moat | M |
| MON-03 | **Private Telegram channel management** (управление приватным каналом): бот выдаёт и отзывает персональные инвайты по статусу подписки | P2-moat, P3-wow | L |
| MON-04 | **Tips & donations** (донаты): Telegram Stars, Tribute, внешние ссылки | P1-product | M |
| MON-05 | **Digital goods delivery** (доставка цифровых товаров): обои, артбуки, билды, ключи | P1-product | L |
| MON-06 | **Early access tiers** (ранний доступ): пост выходит подписчикам на N дней раньше, потом открывается всем — автоматически | P2-moat | M |
| MON-07 | **Referral program** (реферальная программа) для самого Cedar Clerk | P1-product | M |
| MON-08 | **Payout dashboard** (панель выплат): сколько заработал тенант, откуда, комиссия | P1-product | L |

---

## 8. Блок AI — умный слой (всё через кредиты)

Жёсткое правило уже зафиксировано: любая функция на Claude API идёт через credits system (система кредитов).

| ID | Фича | Приоритет | Сложность |
|---|---|---|---|
| AI-01 | **Rewrite / tone / length** (переписать, сменить тон, сжать или развернуть) | P1-product | S |
| AI-02 | **RU↔EN перевод поста** с сохранением форматирования и связкой версий (см. GDD-16) | P0-me | M |
| AI-03 | **Devlog from commits** (девлог из git-коммитов): подключил репозиторий → черновик поста за неделю | P2-moat, P3-wow | L |
| AI-04 | **Video transcript → post** (расшифровка видео в пост): у тебя гигабайты записей процесса, это прямой конвертер сырья в контент | P0-me, P2-moat | L |
| AI-05 | **Voice note → post** (голосовая заметка в пост): наговорил по дороге — получил черновик | P0-me, P3-wow | M |
| AI-06 | **Screenshot → post draft** (скриншот в черновик поста) | P3-wow | M |
| AI-07 | **Brand voice profile** (профиль авторского голоса): обучен на твоих прошлых постах, чтобы AI не превращал тебя в LinkedIn | P2-moat | M |
| AI-08 | **Repurposing** (переупаковка): длинный пост → тред → твит → описание для YouTube → тизер для Telegram, одним действием | P1-product, P3-wow | M |
| AI-09 | **Project-aware assistant** (ассистент, знающий проект): RAG по твоему ГДД и постам, отвечает и пишет в контексте твоей игры | P2-moat, P3-wow | XL |
| AI-10 | **SEO meta + tags suggestion** (подсказка мета-описаний и тегов) | P1-product | S |
| AI-11 | **Consistency checker** (проверка консистентности ГДД): «в разделе про экономику сказано 3 валюты, в разделе про магазин — 2» | P2-moat, P3-wow | L |

> **AI-04 и AI-11 — самые «инвесторские» пункты во всём документе.** Первое решает боль, которая есть у тебя буквально сейчас (терабайты сырья, ноль контента). Второе делает продукт незаменимым для любого, у кого документация больше 20 страниц.

---

## 9. Блок ANALYTICS

Напоминание из ранее принятых решений: **сбор данных должен начаться раньше, чем UI**, задним числом не восстановить.

| ID | Фича | Приоритет | Сложность |
|---|---|---|---|
| ANL-01 | **Collection layer** (слой сбора): `PostStatSnapshot`, `ReactionEvent`, site view beacon, `message_reaction_count` в `allowed_updates` | P0-me (срочно) | M |
| ANL-02 | **Post performance dashboard** (панель эффективности постов) | P1-product | M |
| ANL-03 | **Channel growth** (рост канала): подписчики, прирост, отписки | P1-product | M |
| ANL-04 | **Funnel** (воронка): Telegram → блог → wishlist/донат | P1-product, P3-wow | L |
| ANL-05 | **Format comparison** (сравнение форматов): что заходит — фото, длинный текст, девлог, видео | P1-product | M |
| ANL-06 | **Weekly report** (еженедельный отчёт) прямо в Telegram | P1-product, P3-wow | S |
| ANL-07 | **A/B headlines** (A/B-тест заголовков) | later | L |
| ANL-08 | **CSV/JSON export** | P1-product | S |

---

## 10. Блок PLATFORM — то, из-за чего инвестор поверит в масштабируемость

| ID | Фича | Приоритет | Сложность |
|---|---|---|---|
| PLT-01 | **Public REST API + webhooks** (публичный API и вебхуки) | P1-product, P3-wow | L |
| PLT-02 | **Zapier / Make / n8n коннекторы** | P3-wow | M |
| PLT-03 | **CLI + GitHub Action**: `cedar publish devlog.md` из CI | P2-moat, P3-wow | M |
| PLT-04 | **Unity Editor plugin** (плагин для редактора Unity): скриншот из Game View → черновик поста, не выходя из движка. Ниша, которую ты знаешь лучше всех конкурентов | P2-moat, P3-wow | L |
| PLT-05 | **Obsidian / VS Code sync** (синхронизация с локальными редакторами) | P2-moat | L |
| PLT-06 | **Browser clipper** (расширение-клиппер) для сбора референсов в проект | P3-wow | M |
| PLT-07 | **PWA + offline drafts** (прогрессивное веб-приложение с офлайн-черновиками) | P1-product | L |
| PLT-08 | **Teams, roles, audit log** (команды, роли, журнал действий) | P1-product | L |
| PLT-09 | **2FA + session management** (двухфакторная аутентификация и управление сессиями) | P1-product | M |
| PLT-10 | **GDPR: data export & deletion** (выгрузка и удаление данных) | P1-product | M |
| PLT-11 | **Moderation queue + abuse controls** (очередь модерации и защита от злоупотреблений) — обязательно до публичного запуска | P1-product | M |
| PLT-12 | **Backup & restore per tenant** (резервное копирование и восстановление по тенанту) | P1-product | M |
| PLT-13 | **Migration path SQLite → Postgres** (путь миграции на Postgres) — не делать сейчас, но заложить абстракции | P1-product | L |
| PLT-14 | **Observability** (наблюдаемость): структурные логи, метрики, health, алерты в Telegram | P1-product | M |
| PLT-15 | **Template & theme marketplace** (магазин шаблонов и тем) | later, P3-wow | XL |
| PLT-16 | **White-label** (белая марка) для студий | later | L |

---

## 11. Честная оценка: что здесь опасно

Скоуп выше — это несколько лет работы для одного человека. Если пытаться сделать всё, не будет сделано ничего. Три конкретных риска:

1. **Raspberry Pi как продакшн для мультитенантного SaaS.** Для догфудинга — отлично. Для платящих чужих людей — нет: диск, бэкапы, аптайм, SLA. MED-07 и план переезда нужны раньше, чем первый платный внешний клиент.
2. **Мультитенантность до product-market fit.** Phase 6 стоит дорого. Есть альтернативный порядок: сначала довести однопользовательский сценарий до состояния «я публикую отсюда каждый день и мне не хочется в Telegram напрямую», и только потом открывать регистрацию. Это не отменяет Phase 6 — это вопрос, что раньше.
3. **AI без кредитов = неограниченный убыток.** Уже зафиксировано, повторяю, потому что AI-04 (видео) и AI-09 (RAG) в разы дороже перевода комментариев.

---

## 12. Рекомендуемая последовательность (мнение, не решение)

**Волна 1 — «мой инструмент работает» (сейчас):**
`ANL-01` (сбор данных, задним числом не сделать) → `WEB-01` (OG-теги) → `MED-01/02/03/04` (медиатека) → `MED-05/06` (альбомы + media groups) → `GDD-01/09/13` (дерево, оглавление, поиск).

Результат: ты полностью съезжаешь с ручного постинга и ведёшь ГДД внутри своего продукта.

**Волна 2 — «уникальность» :**
`GDD-03` (история) → `GDD-04` (видимость блоков) → `GDD-05` (снапшоты) → `PUB-03` (edit-sync) → `PUB-04` (серии) → `AI-02` (RU/EN).

Результат: появляется то, чего нет у конкурентов — публичный живой дизайн-документ без спойлеров.

**Волна 3 — «продукт для чужих»:**
`MED-07` (S3) → `PLT-11/12/14` (модерация, бэкапы, наблюдаемость) → `WEB-02` (домены) → биллинг end-to-end → `ANL-02/03`.

**Волна 4 — «инвестиционная витрина»:**
`AI-04` (видео→пост) → `GDD-18`/`AI-03` (девлог из диффов и коммитов) → `PLT-04` (Unity-плагин) → `WEB-11` (пресс-кит) → `MON-01/03`.

---

## 13. Что нужно инвестору, помимо фич

Фичи — не то, что покупают. Подготовь параллельно:

- **Метрики продукта:** activation rate (доля дошедших до первой публикации), TTFP — time to first post (время до первого поста), WAU/MAU, retention D7/D30, число подключённых каналов на пользователя, ARPU.
- **Догфудинг как доказательство:** Dev Dairy Diary и YouTube, полностью ведомые из Cedar Clerk, с публичной динамикой роста. Это самый убедительный слайд, который у тебя может быть.
- **Оценка рынка снизу вверх:** количество активных инди-разработчиков (Steam-релизы в год, itch.io, участники геймджемов) × реалистичный ARPU. Не «рынок SaaS для контента $X млрд».
- **Конкурентная карта:** Notion / Confluence (документы, не публикуют), Buffer / Publer / Postiz (публикуют, не знают о проекте), Ghost / Substack / Boosty (блог и монетизация, без Telegram и без ГДД), Steam Devblog (только Steam). Пустая клетка — твоя.
- **Стратегия дистрибуции:** контент-маркетинг силами собственного девлога, геймджемы, Discord-сообщества, Unity/Godot-тусовки.
- **Правовой минимум:** ToS, Privacy Policy, DPA, обработка платежей, политика NSFW (открытый вопрос уже висит).

---

## 14. Открытые вопросы, которые блокируют этот скоуп

1. Порядок: сначала довести личный сценарий, или сначала Phase 6 (мультитенантность)?
2. ГДД — отдельная сущность (`Document`) или расширение существующих постов? От этого зависит вся модель данных блока GDD.
3. Хранилище медиа: остаёмся на Pi до какого объёма и по какому триггеру уезжаем на S3/R2?
4. Политика NSFW — блокирует и артист-вертикаль, и часть личного контента.
5. Домен для блогов тенантов — отдельный продуктовый домен или поддомен `mooexe.dev`?
6. Кредиты: единая валюта на все AI-функции, или отдельные лимиты по фичам?

---

## 15. Следующий шаг

Предлагаю не превращать этот документ в план, а сделать из него ровно два действия:

1. Пройтись по таблицам и проставить своё «да / нет / потом» напротив каждого ID.
2. Из всех, что получили «да» в волне 1, записать ADR в `docs/DECISIONS.md` и только после этого готовить бриф для Claude Code.

Иначе документ рискует стать ещё одним источником рассинхрона, а против этого у тебя уже есть жёсткое правило.


# Cedar Clerk For Indie Game Developers

# Name
Original name is `Cedar Clerk`. But I should start thinking if it's good in a sense of marketting and "searchability". 
However, when speaking of a SaaS focused on Indie Game Developer, I should think of something different. I still like `Cedar Clerk` but for now WIP-name is `Cedar Clerk For Indie Developers`

# Overview
`Cedar Clerk` is a SaaS which is good for writing longreads with richful content and posting it to different social media. I thought, as an Indie Game Developer, that maybe I should start narrowing the target audience of it. I'm an Indie Game Developer myself, also I'm a Programmer/Game Designer/Game Producer/Game Writter/Sound Designer/Game Music Producer/Filmmaker/Game Marketer and Game Analyst. So, basically the target audience (me) is much wider than just writing long posts with pictures.

I want it to grow as a toolkit platform which is good for writing longreads with richful content, posting it to different social media AND also be good to plan and develop video games, write changelogs, manage assets and plugins, make calculations for a budget, some helpers to maintain game design and more.

# Project Structure
One codebase, one installation, the base lives as "role" or "module", not as fork.
So, `Cedar Clerk` stays the same, but it's divided into modules with addition of new modules.

# Form of distrubution
Right now I'm still the only user of `Cedar Clerk` even though Stripe/Paypal/Telegram stars payments work (and also it now contains Credits for posting to X). Right now it's a website with address `cedarclerk.mooexe.dev`. I'd like to keep it as a website BUT I also would like to have a Desktop App. For example, Asset management will definitely need some local application. And I just like desktop apps more. They're more convinient for me.

# Git Structure
* `master` is now only used for the LATEST stable version of `Cedar Clerk`. Each commit to this branch is tagged with version. EVERY deploy operation must be done only from `master` and ONLY from it.
* `dev` is a general development branch. 
* I created a new branch `indiedev_module` from `dev` for a new features related to game development, because I'm still not sure if that's a working business model. In the future, if my idea would work, this branch might be deleted.

# New Inside Structure
* Each post is just an independent document with a `post` type. It can be tagged and put into folders
* Other documents are also independent pieces with their own types. Each document may or may not refer to others. But in general you have a `Project` which MUST include at least one document (in most of cases a post, or something else)
* There might be not just documents attached to a project, but some other interactive entities. Like an `Asset Viewer` type, `References board`, `Brainsform session` and many many more

# New Features
* New publishing targets (`itch.io devlog` via their API, `Steam announcements` via Partner API, `IndieDB`, `LinkedIn`)
* `Press Kit`. Like `presskit()` but more awesome. It'd be good to have a self-updating generated `Press Kit` document
* Build/Version tagging. And therefore some extensive tagging system
* `Asset Viewer` or `Asset Manager`
* For now `Glossary` stays where it stays, but should work globally for `posts`. I should be able to create a `Local Glossary` with the scope of a project.
* `Brainstorm session` is just a convinient way to organize random thought into something creative and useful
* `References board` is some kind of a gallery which can be used as a `Reference board`. 
* `Game Script Writer`. Is set of tools which make it more convinient to do work as a Game Writter
* `Game Plot Writer`. Basically the same as `Game Script Writer` but more brief
* `Game Design Helpers` are set of tools which helps to think, plan and create new game mechanics and systems
* Basic `Task Tracker`. It should have a Name, Status, Priority, Description and Assignee fields. Each task should be somehow linked to other documents and the whole `Task Tracker` must be synchronized with everything else
* `Workflow planner`. Just a tool to plan the development workflow/pipeline
* `Development Planner`. Tasks, Sprints, Deadlines etc goes here
* `Code Documentation`. Not neccessary but would be cool to have it. You can just see the code documentation in a convinient way.


# TASKS
1) Make a deep research of a current state of `Cedar Clerk`
2) Make a Desktop app out of it
3) Make desicions and think of your ideas of how better to build such project
4) Think of the list of features that MUST be in it, and the list of features that MIGHT be in it
5) Deeply think of UI/UX. And make a prompt for `Claude Design` to make a UI prototype for the whole project
6) Update related .md files inside of project and add new .md files if needed. Think deep about what's the best way to do so

# Session Brief — Visual Polish Sweep (OG + Motion + View Transitions)

**Режим:** сначала **PLANNING-ONLY**. Не пиши код, пока я не подтвержу план.
**Оценка:** ~10 часов, 3 независимых коммита.
**Migrations:** ноль. Entity-изменений в этой сессии нет.

---

## 0. Pre-work checklist (hard stop-gates)

Выполни по порядку. На любом FAIL — остановись и напиши мне, не продолжай.

| # | Проверка | Команда | STOP-условие |
|---|---|---|---|
| 1 | Git состояние | `git status --porcelain` | Есть uncommitted работа (возможно от Codex) → показать diff, ждать решения |
| 2 | Расхождение тегов | `git branch --contains 0.10.8` и то же для `0.10.9` | Теги не являются ancestors `master` HEAD → **деплой в этой сессии запрещён**, только локальная работа |
| 3 | Тесты зелёные | `dotnet test` | Не 442/442 → стоп |
| 4 | Build фронта | `ng build` | Warnings/errors → стоп |
| 5 | Локальный запуск | `ASPNETCORE_ENVIRONMENT=LocalNoBot dotnet run` | Нет log-строки «bot disabled» → стоп (иначе 409 Conflict на polling) |

| Аргумент | Что делает |
|---|---|
| `git branch --contains <tag>` | Показывает ветки, в которые входит коммит тега |
| `--porcelain` | Машиночитаемый короткий вывод `git status` |
| `ASPNETCORE_ENVIRONMENT=LocalNoBot` | Профиль без Telegram long-polling |

---

## 1. Discovery (обязательно перед планом)

Не доверяй путям из этого брифа — найди фактические. Docs drift — известный failure mode.

1. Найди файл(ы), которые рендерят HTML блога — начни с `BlogEndpoints`. Выпиши точный путь и место, где формируется `<head>`.
2. Найди CSS блога (в `wwwroot`?). Выпиши путь.
3. Найди определение Cabin design tokens (light/dark). Выпиши путь и текущий формат токенов.
4. Найди, где в Angular объявлен router provider (`provideRouter`). Выпиши путь.
5. Найди поля поста, релевантные для OG: заголовок, cover/первое изображение, excerpt/описание, `PublishedAt`, `UpdatedAt`, язык, флаги private / semi-public.

Отчитайся результатами discovery **до** написания плана.

---

## 2. Задача A — OG / Twitter meta tags (~3 ч)

**Приоритет 1.** Делать первой: наибольшая отдача, наименьший риск.

### Что реализовать

1. В `<head>` страницы поста:
   - `og:type=article`, `og:title`, `og:description`, `og:url`, `og:site_name`, `og:image`, `og:image:width`, `og:image:height`, `og:locale`
   - `article:published_time`, `article:modified_time` (ISO-8601 UTC)
   - `twitter:card=summary_large_image`, `twitter:title`, `twitter:description`, `twitter:image`
   - канонический `<link rel="canonical">`
2. В `<head>` страницы списка/главной блога: те же теги с `og:type=website`.
3. Per-language: для языковых версий поста добавить `og:locale:alternate` + `<link rel="alternate" hreflang="...">` на основе существующих cross-links.
4. Fallback-изображение, когда у поста нет cover: брать avatar/appearance-настройки тенанта, иначе — статический дефолт.

### Жёсткие правила

1. **Private posts: OG-теги НЕ эмитятся вообще** (ни title, ни description, ни image). Semi-public — только `og:title` + fallback-изображение, без `og:description` и без cover. Это утечка контента, защищённого watermark/copy-protection.
2. **Все URL абсолютные `https://`.** За Cloudflare Tunnel схема приходит через `X-Forwarded-Proto` — проверь, что `ForwardedHeaders` middleware сконфигурирован; если нет, это часть задачи.
3. **Весь user text HTML-escape'ится** перед вставкой в атрибут `content` (действующее правило проекта).
4. `og:description` — обрезать до ~200 символов по границе слова, без обрыва внутри HTML-entity.
5. Telegram требует изображение ≥ 300 px и < 5 MB; целевой размер 1200×630.

### Unit-тесты (в `CedarClerk.Core`, обязательно)

1. Заголовок с `<`, `>`, `&`, `"` → корректный escape.
2. Private post → в выводе нет ни одного `og:`.
3. Semi-public post → нет `og:description`, нет cover в `og:image`.
4. Пост без cover → fallback-изображение, абсолютный URL.
5. Description длиннее лимита → обрезка по слову.

### Acceptance criteria

- [ ] `curl -s https://blog.mooexe.dev/<post> | grep 'og:'` возвращает полный набор тегов
- [ ] Ссылка на пост в `@testingandfun` показывает превью с картинкой и описанием
- [ ] Private-пост в Telegram даёт голую ссылку без превью
- [ ] Валидатор `https://www.opengraph.xyz/` — без ошибок
- [ ] Новые unit-тесты зелёные, общий счётчик тестов вырос

> Кэш превью в Telegram живёт долго. Для повторной проверки той же ссылки используй `@WebpageBot` или добавь `?v=2`.

**Коммит:** `og meta tags` + запись в `CHANGELOG.md`.

---

## 3. Задача B — Motion tokens + skeleton loaders (~3 ч)

**Приоритет 2.** Делать до View Transitions — задача C зависит от этих токенов.

### Что реализовать

1. Добавить в Cabin tokens группу motion (не трогая существующие цветовые токены):

| Токен | Назначение | Значение |
|---|---|---|
| `--cabin-dur-fast` | hover, focus, ripple | `120ms` |
| `--cabin-dur-base` | появление элемента | `240ms` |
| `--cabin-dur-slow` | переход страницы | `400ms` |
| `--cabin-ease-standard` | обычные переходы | `cubic-bezier(.2,0,0,1)` |
| `--cabin-ease-spring` | «пружина» | CSS `linear()` approximation |
| `--cabin-ease-exit` | исчезновение | `cubic-bezier(.4,0,1,1)` |

2. `--cabin-ease-spring` реализовать через CSS-функцию `linear()` (не через JS-библиотеку). Сгенерировать ~12–16 точек для damped spring, вписать значение как литерал с комментарием о параметрах.
3. Skeleton loader как переиспользуемый Angular-компонент: варианты `text` (строки), `card`, `avatar`, `table-row`. Shimmer — CSS-only, без JS-таймеров.
4. Подключить skeleton минимум в трёх местах: Posts Manager (список), admin panel (таблицы), загрузка редактора.

### Жёсткие правила

1. `@media (prefers-reduced-motion: reduce)` — все durations → `1ms`, shimmer выключается и заменяется статичной заливкой. Это блокирующее требование, не опция.
2. Анимировать **только** `transform` и `opacity`. Никаких `width`/`height`/`top`/`left` в переходах.
3. Skeleton должен иметь `aria-busy="true"` и `aria-live="polite"` на контейнере.
4. Никаких magic numbers в компонентах — только токены.
5. Значения токенов живут **только** в CSS. В `Consts.cs` их не дублировать.

### Acceptance criteria

- [ ] Toggle «Reduce motion» в OS → анимации и shimmer исчезают, layout не ломается
- [ ] Skeleton виден при throttling «Slow 3G» в DevTools, не мигает при быстрой загрузке (min-display 300ms)
- [ ] Light и dark тема: skeleton читаем в обеих
- [ ] Vitest-тест: компонент рендерит нужное число строк по input
- [ ] Проверено в браузере руками, не только по билду

**Коммит:** `motion tokens skeletons` + `CHANGELOG.md`.

---

## 4. Задача C — View Transitions API (~4 ч)

**Приоритет 3.** Две независимые части — не путать их.

### C1. Блог (cross-document, MPA)

Блог server-rendered, поэтому это **cross-document** переходы, не `document.startViewTransition()`.

1. В CSS блога: `@view-transition { navigation: auto; }`
2. Shared element между списком и постом: cover-изображение и заголовок.
3. Проблема уникальности: `view-transition-name` должен быть уникален на странице. На странице поста — задать статически. На странице списка — задавать имя **только кликнутой карточке** через маленький inline-скрипт по `click`, снимать на `pageshow`.
4. Кастомизировать `::view-transition-old(root)` / `::view-transition-new(root)` с `--cabin-dur-slow` и `--cabin-ease-standard`.

### C2. Angular SPA (same-document)

1. Включить `withViewTransitions()` в `provideRouter`.
2. Проверить, что это не конфликтует с signals-driven перерисовкой и с сохранением scroll position.
3. Если конфликт с редактором TipTap — **отключить переходы для маршрутов редактора**, не чинить героически.

### Жёсткие правила

1. Firefox cross-document переходы не поддерживает — degradation обязана быть тихой: обычная навигация без ошибок в консоли.
2. `@media (prefers-reduced-motion: reduce)` отключает переходы полностью.
3. Не трогать порядок загрузки CSS ради переходов, если это ломает FOUC.

### Acceptance criteria

- [ ] Chrome: клик по карточке → cover плавно «переезжает» в hero поста
- [ ] Firefox: та же навигация работает, консоль чистая
- [ ] Reduce motion → мгновенная навигация
- [ ] Back-кнопка не оставляет застрявший `view-transition-name` на карточке
- [ ] Мобильный Chrome: нет лагов на списке из 30+ постов

**Коммит:** `view transitions` + `CHANGELOG.md`.

---

## 5. Docs flow

1. Перед кодом — ADR в `docs/DECISIONS.md`: **«OG-теги не эмитятся для private-постов; semi-public — только title + fallback image»**. Это policy-решение, оно должно быть зафиксировано первым.
2. ADR на motion-токены — короткий, о правиле «только transform/opacity + обязательный reduced-motion».
3. После — обновить `TASKS.md` (перенести пункты live-verify checklist) и `CHANGELOG.md`.
4. `docs/ROADMAP.md` — только append, существующие фазы не перенумеровывать.

---

## 6. Out of scope (не делай, даже если руки чешутся)

1. Динамическая генерация OG-картинок из заголовка через ImageSharp — отдельная задача
2. Rewrite на React / Tauri / Rust
3. Переписывание Angular-компонентов ради «более красивой» анимации
4. Мобильный responsive-редизайн (08.07)
5. Angular SSR — блог уже server-rendered, SSR не нужен
6. **Любой деплой на droplet** — до разрешения расхождения тегов `0.10.8` / `0.10.9`
7. Bump версии в `Consts.cs` — только после разрешения п. 6

---

## 7. Порядок работы

1. Pre-work checklist → отчёт
2. Discovery → отчёт с фактическими путями
3. План с разбивкой по коммитам → **ждать моего OK**
4. ADR
5. Задача A → тесты → браузерная проверка → коммит
6. Задача B → тесты → браузерная проверка → коммит
7. Задача C → браузерная проверка в Chrome и Firefox → коммит

После каждого коммита: короткий отчёт «сделано N из 3, дальше — X», без пересказа диффа.
