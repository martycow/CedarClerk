---
owner: marty
last_verified: 2026-08-18
source_of_truth_for: терминология проекта — что значат слова, которыми говорят код и доки
guard: none
---

# Терминология Cedar Clerk

Словарь проектных терминов — того, что не гуглится, потому что значение здешнее. Извлечён из кода и
живых доков 18.08.2026 (пять параллельных проходов + слияние); каждый термин подтверждён источником.
Общетехнические слова (SQLite, OAuth) сюда не входят. Пользовательский глоссарий постов — другая
вещь: это фича блога (`GlossaryTerm`), а не этот файл.

## Документ и публикация

| Термин | По-русски | Значение | Источник |
|---|---|---|---|
| Draft | черновик | Центральная сущность контента: TipTap-документ с автосейвом, ревизиями, переводами, тегами и папкой; с ADR-102 — документ любого типа внутри Project, а не только пост | `Entities.cs`, ARCHITECTURE §Data model |
| CedarJson | — | Канонический внутренний формат документа — TipTap JSON в `Draft.CedarJson`; хранится как есть и никогда не переписывается под конкретную сеть | `Entities.cs:256` |
| DocumentType | тип документа | Строка на Draft (ADR-102): `post/design/script/plot/changelog/note`; default `post`, публикуемы только `post` и `changelog` | `Core/DocumentTypes.cs` |
| One document, many renderers | «один документ, много рендереров» | Ядро архитектуры: один CedarJson рендерится во все выходы; ни одна поверхность не ведёт параллельный ручной контент (единственное исключение — override text) | ARCHITECTURE §Core idea |
| Renderer | рендерер | Чистый C#-класс в Core, превращающий CedarJson в формат назначения; канонический для Telegram — `CedarToTelegramBlocksRenderer`; инварианты: экранирование `< > &` + тест на каждый узел | ARCHITECTURE:29-33, rules/renderers.md |
| PublishTarget / IPublishTarget | таргет | Сущность: подключённый аккаунт владельца в одной сети («куда может уйти пост»); интерфейс: назваться, описать лимиты, отправить — и ничего больше | `Publishing/IPublishTarget.cs`, `Entities.cs:843` |
| PublishNetworks | сеть | Строковые ключи сетей — `"telegram"`, `"bluesky"`, `"x"`; строки, а не enum: значение живёт в SQLite, API и Angular (ADR-078) | `Core/PublishCapabilities.cs:5-14` |
| PublishCapabilities | возможности сети | Лимиты сети как данные, а не поведение: MaxCharacters, MaxMediaItems, Supports*, DerivesShortPost — редактор читает их и предупреждает до отправки | `Core/PublishCapabilities.cs` |
| PublishJob | джоб очереди публикаций | Durable-строка «одна публикация в один таргет» (ADR-081): Pending→Running→Succeeded\|Failed\|**Unknown**; Unknown (процесс умер во время отправки) не ретраится — слепой ретрай превращает один пост в два | `Entities.cs:767+`, `Publishing/PublishJobRunner.cs` |
| PublishOutcome / Receipt | — | Результат отправки вместо исключения: Receipt (RemoteId; PublicUrl может быть null) либо читаемая ошибка + статус-код | `Publishing/IPublishTarget.cs:46,66` |
| Micro-thread | микроблог-тред | ADR-094: документ целиком уходит в X/Bluesky цепочкой reply — резка в родных единицах сети, нумерация «N/M», ссылка на блог в последней части | `Core/MicroThreadSplitter.cs` |
| Thread part | часть треда | Один PublishJob на **часть**, а не на тред (T-106): упавший на части 4 тред возобновляется с части 4; ThreadPartRef несёт ReplyTo и Root | `Entities.cs:779-787` |
| Override text | авторский текст под сеть | `DraftTargetText` per (draft, network, language): кросс-пост — самостоятельный пост, а не анонс (ADR-077); пусто → тизер, публикация никогда не блокируется | `Entities.cs:818-835` |
| Teaser | тизер | Автоматический короткий пост со ссылкой на блог — fallback при пустом override; capability DerivesShortPost превращает «слишком длинно» из отказа в информацию | `Entities.cs:823-825`, ADR-093 |
| PrimaryLanguage | основной язык | Основной язык черновика (ADR-064): документ на самом Draft канонический и написан на нём; по умолчанию русский | `Entities.cs:257-259` |
| DraftTranslation | перевод черновика | Версия документа на другом языке: свои Title и CedarJson на (DraftId, Language) | `Entities.cs:494-511` |
| SourceSnapshotJson | снапшот исходника перевода | Снимок исходного CedarJson на момент синхронизации перевода — даёт поблочный дифф «что изменилось с тех пор» вместо булева «устарел» | `Entities.cs:504-510` |
| DraftRevision | ревизия | Неизменяемая история по языкам: строка на каждый явный сейв и публикацию (Kind: save\|telegram\|blog) — история правок и безопасный дифф перед публикацией | `Entities.cs:513-525` |
| ShrinkGuard | — | Когда сейв требует явного подтверждения: инцидент 29.07 — автосейв сохранил мгновенно-пустой редактор; «подозрительно» = было ≥200 видимых символов и осталось ≤20% | `Core/ShrinkGuard.cs` |
| .cedar (CedarPackage) | формат .cedar | Экспорт/импорт документа: zip-контейнер (аналог .docx) с document.json + assets/; переводы в экспорт сознательно не входят | `Core/CedarPackage.cs`, ARCHITECTURE:130-132 |

## Блог и читатели

| Термин | По-русски | Значение | Источник |
|---|---|---|---|
| annotation | аннотация | TipTap-узел, помечающий фрагмент статьи для якорных реакций/комментариев; null = «вся статья»; понятие только блоговое | `Core/CedarToBlogHtmlRenderer.cs` |
| article title | читательский заголовок | `Draft.ArticleTitle`: заголовок для читателя отдельно от рабочего имени драфта («devlog 14 (final final)» — имя файла, не заголовок) | `Entities.cs` |
| private post | приватный пост | Пост с `IsPrivate`, опубликованный в блог, но доступный по инвайту/регистрации; остальным страница неотличима от 404 | `BlogEndpoints.cs` |
| semi-public post | полупубличный пост | `IsListedWhilePrivate`: приватный пост виден в индексе карточкой с замком и без тизера — меняется «что рекламируется», не «что читаемо» | `Entities.cs`, `BlogEndpoints.cs` |
| registration gate | регистрационный гейт | Форма (B3) незваному посетителю приватного поста вместо 404; отправка сразу ставит cookie — сбор аудитории, не верификация | `BlogEndpoints.cs`, `Core/RegistrationFormSet.cs` |
| form preset | пресет формы | Именованная переиспользуемая форма (N12): применение **копирует** JSON в драфт — правка пресета не меняет живой пост | `Entities.cs` (FormPreset) |
| invite | инвайт на приватный пост | `PostInvite` — строка на приглашённый email; токен `?invite=` выдаёт подписанную cookie; удаление строки отзывает доступ немедленно | `Entities.cs`, `BlogEndpoints.cs` |
| reader access token | токен доступа читателя | `PostRegistration.AccessToken` (T-064): accessUrl после формы, чтобы доступ переносился в другой браузер; отзыв — на одного читателя | `Entities.cs` |
| revoked registration | отозванная регистрация | `PostRegistration.IsRevoked`: доступ отозван, строка остаётся для истории | `Entities.cs` |
| watermark | водяной знак | `WatermarkText`, замощённый поверх листа приватного поста (SVG-тайл base64 в CSS-фоне): отпугивание, не защита | `Core/WatermarkRenderer.cs` |
| copy protection | защита от копирования | `DisableCopy`: блок выделения/copy/контекст-меню на листе приватного поста; из той же семьи сдерживания, что watermark | `BlogEndpoints.cs` |
| visitor hash | хеш посетителя | SHA-256 от IP с солью — анонимная идентичность без хранения сырых IP: дедуп реакций (ADR-016), «один голос», троттлинг форм | `BlogEndpoints.cs` |
| reaction | реакция | Анонимный like/dislike на статью или аннотацию, одна на (draft, annotation, visitor); повторный клик снимает | `Entities.cs` |
| poll vote | голос в опросе | Опросы — блок только для блога (ADR-055): один голос на (poll, visitor), смена ответа обновляет строку | `Entities.cs`, `BlogEndpoints.cs` |
| view counter | счётчик просмотров | `ViewCount`: атомарный UPDATE + дедуп 30-минутной cookie, общий для всех языков поста (ADR-023) | `BlogEndpoints.cs` |
| geo rollup | гео-сводка | `BlogViewGeoDaily`: дневной агрегат по (владелец, день, страна, язык) из CF-IPCountry/Accept-Language; агрегат, не журнал визитов (ADR-097) | `Entities.cs`, `Core/ReaderGeo.cs` |
| header slots | слоты шапки | До 3 настраиваемых элементов подзаголовка (подпись/URL/локация/дата/длина/время чтения/слова/просмотры); третий — Pro | `Core/HeaderSlotRenderer.cs` |
| cross-link | кросс-ссылка | Взаимные ссылки поста между поверхностями («Смотреть в Telegram» ↔ «Read on the blog»), тексты владельца локализуемы (I15) | `Consts.CrossLinks` |
| user glossary | пользовательский глоссарий | `GlossaryTerm` — термины владельца (per-owner, per-language, алиасы под падежи), подсвечиваются в блоге с тултипом; только первое вхождение | `Core/GlossaryScanner.cs` |

## Тарифы и деньги

| Термин | По-русски | Значение | Источник |
|---|---|---|---|
| PlanTiers | уровни тарифа | Byte-enum Free(0)/Pro(1)/ProPlus(2)/Forever(3); строки планов: pro $3, proplus $6, trial → tier ProPlus | `Core/PlanTiers.cs`, `Consts.Plans` |
| TTFP | время до первой публикации | Time to first publish — медиана от `AspNetUsers.CreatedAt` до первой публикации (min по Succeeded `PublishJob` и `BlogPublishedAt`); вспомогательная к активации из BUSINESS §4 | METRICS §3.1 (ADR-126) |
| PlanLimitations | лимиты тарифов | Центральный гейт по tier: каналы 1/3/10, подпись с Pro, AI с ProPlus, слоты 3 против 2, квоты хранилища | `Core/PlanLimitations.cs` |
| storage quota | квота хранилища | 100 МБ / 1 ГБ / 3 ГБ / 100 ГБ (ADR-129); остаток риска — медиа на диске машины до шага 2 T-172 | `PlanLimitations.cs`, MULTITENANCY §1 |
| Trial | триал за $1 | $1 за 7 дней ProPlus, один раз на аккаунт (`TrialUsedAt`); в метриках — «фильтр намерения» | `Consts.Plans`, BUSINESS §4 |
| Forever / Founder-код | вечный тариф основателя | Постоянный Pro (100 ГБ, без AI) отдельным founder-инвайтом без платёжки; «навсегда» = PlanExpiresAt=null; раздача на джемах | ADR-022, BUSINESS §6 |
| Grace | грейс-период | 2 дня поверх 30-дневной подписки — лаг renewal-вебхука не «мигает» пользователем в Free | `Core/SubscriptionPlanHelper.cs` |
| credit | кредит | Внутренняя валюта за то, что стоит реальных денег за использование (X-пост = 1 кредит, себестоимость ~$0.20); цена ~2× себестоимости | `Core/CreditPacks.cs` |
| credit wallet | кошелёк кредитов | Предоплаченный кошелёк (ADR-092): баланс = SUM(Delta) по леджеру; пополнение теми же платёжными флоу, что подписки | `CreditWallet.cs` |
| CreditEntry | леджер кредитов | Одно движение (+покупка/−публиш); леджер, а не счётчик — у баланса есть аудит-трейл; уникальная пара (Reason, Ref) = идемпотентность | `Entities.cs` |
| CreditReasons | причины движения | Словарь леджера, общий с UI: purchase / x-post / admin-grant | `Core/CreditPacks.cs` |
| credit pack | пакет кредитов | 10/$4, 50/$18, 100/$30 (Stars 200/900/1500⭐); payload «credits-{pack}:{user}» — та же схема {what}:{who}, что у планов | `Core/CreditPacks.cs` |
| AiDailyLimit / AiUsage | дневная AI-квота | 20 вызовов/сутки на пользователя; до 600/мес на $6 ProPlus — единственная статья, способная сделать тариф убыточным | `PlanLimitations.cs`, BUSINESS §3 |
| attribution signature | подпись-атрибуция | Free всегда получает «Published with Cedar Clerk» со ссылкой (апгрейд-крючок); Pro+ заменяет или убирает; гейт централизован в `ResolveSignature` (ADR-034) | `PlanLimitations.cs` |
| InviteCode | инвайт-код | Реальные коды (IF2): Code/Label/MaxUses/ExpiresAt; деактивируются, не удаляются — иначе стирается атрибуция; fallback — конфиг | `Entities.cs`, ADR-122 |
| Payment.ExternalId | дедуп платежа | Stripe session / Stars charge / PayPal order id — защита от дублей при повторных вебхуках | `Entities.cs`, `BillingEndpoints.cs` |
| FreeChannelSwitchCooldown | кулдаун смены канала | На Free канал меняется не чаще раза в 7 дней — иначе один аккаунт обслуживал бы несколько каналов по очереди | `PlanLimitations.cs` |
| активация | — | Метрика №1: доля зарегистрировавшихся, опубликовавших ≥1 пост за первую неделю; низкая = проблема онбординга, трафик покупать рано | BUSINESS §4 |
| отток (churn) | — | Метрика №3: платящие, не продлившиеся за месяц; порог 5%/мес (рядом №2 — конверсия Free→платный, №4 — MRR без разовых) | BUSINESS §4 |

## Инди-модуль и десктоп

| Термин | По-русски | Значение | Источник |
|---|---|---|---|
| Project | проект | Игра как контейнер документов, задач, спринтов и индекса ассетов; всегда содержит ≥1 документ, архивируется, а не удаляется | `Entities.IndieDev.cs` |
| ProjectType | тип проекта | fullgame/jam/prototype/released — решает только стартовый документ при создании, и ничего после | `Core/ProjectTypes.cs` |
| GameTask | задача | Набор фиксированных полей, не документ (граница ADR-106): Description — plain text; имя GameTask, потому что Task занято async | `Entities.IndieDev.cs` |
| Sprint | спринт | Имя + две даты + невозобновляемый номер из счётчика проекта (не MAX+1); статуса-колонки нет — planned/current/finished выводится из дат (ADR-111) | `Entities.IndieDev.cs`, `Core/SprintStates.cs` |
| Build | билд | Запись о версии игры: номер, заметки, дата, состав; сущность, а не тег, о гите не знает (ADR-112) | `Entities.IndieDev.cs` |
| changelog generator | генератор чейнджлога | `POST /api/builds/{id}/changelog`: из заметок билда и закрытых задач собирается документ типа changelog — дальше обычный документ | `Modules/IndieDev/BuildEndpoints.cs` |
| EntityLink | связь сущностей | Ручная связь двух вещей проекта (обобщение TaskLink, T-141); «используется в» нельзя обнаружить — только указать руками | `Entities.IndieDev.cs` |
| asset index | индекс ассетов | Облачное описание папки проекта: один корень (`AssetRootPath`), скан агентом, батчи по 500; пропавший файл помечается MissingSince, не удаляется | `Entities.IndieDev.cs`, INDIEDEV |
| AssetEntry | строка индекса | Один файл: путь + метаданные + превью; байты не копируются. Сознательно не `Asset` — тот является загруженным медиа с квотой | `Entities.IndieDev.cs` |
| AssetKinds | вид ассета | Вид по расширению (image/model/audio/…): открывать десятки тысяч файлов нельзя; движковые кэши отсекает SkippedDirectories; у `.blend` — встроенное превью | `Core/AssetKinds.cs`, `Core/BlendThumbnail.cs` |
| агент ФС | агент файловой системы | Тот же `CedarClerk.Server.exe` с `Cedar:Agent:Enabled`: ранний выход в Program.cs — ни базы, ни Identity, только `/agent/*`; читает диск, ничего не пишет и не запускает (ADR-117) | `Modules/Agent/`, DESKTOP |
| bearer token per launch | токен на запуск | Первый замок агента: 32 байта на каждый запуск, через env, на диск не пишется; без токена отказ во всём | `Modules/Agent/AgentEndpoints.cs` |
| granted roots | выданные корни | Второй замок: папки, разрешённые жестом человека (диалог ОС), сравнение по пути с завершающим разделителем; лежат у оболочки, с сервера не берутся | `Modules/Agent/AgentGrants.cs` |
| fingerprint | отпечаток | Превью+метаданные вместо файла с другой машины: чип «Отпечаток», «Показать в проводнике» скрыт; обычное состояние экрана, раз индекс общий | DESKTOP §«Файл или отпечаток» |
| файл или отпечаток | — | Центральный вопрос экрана ассетов: `isLocal()` сравнивает sourceMachine.id с машиной моста и по умолчанию отвечает «ложь» — у браузера моста нет | DESKTOP, UI-INVENTORY |
| sourceMachine | машина-источник | Стабильный GUID машины из `machine.json` (имя обновляется, id — никогда) в `Project.AssetRootMachineId/Name` | `CedarClerk.Desktop/main.js` |
| thumbs budget | бюджет превью | Потолок объёма `thumbs/` в облаке (по умолчанию 2 ГБ на владельца) — один из числовых пределов открытого канала записи индекса | `Consts.cs:91`, DESKTOP |
| working material | рабочий материал | Непубликуемые типы (design/script/plot/note): отказ на общем пути публикации для всех сетей сразу | `Core/DocumentTypes.cs` |
| Cedar:Modules:IndieDev | флаг модуля | Включает весь модуль; выключение возвращает приложение до модуля целиком — обратимость во флаге, не в ветке (ADR-101) | `Program.cs`, INDIEDEV |

## Операции и процесс

| Термин | По-русски | Значение | Источник |
|---|---|---|---|
| cedar CLI | операционная консоль | Единственный вход для build/test/deploy с ADR-119 (Spectre.Console); ставится `Scripts/install-cli.ps1` первой на свежем клоне | CLAUDE.md, ADR-118/119 |
| pipeline | пайплайн | Логика build/test/deploy, переехавшая из `.ps1` в C# с тестами: Build/Test/DeployPipeline в `CedarClerk.Cli/Pipelines/` | ADR-119 |
| preflight | префлайт | `cedar deploy --preflight`: все проверки (ветка, тег, согласие LIVE с версией прода, удалённый зонд) — и стоп, ничего не трогая | CLAUDE.md |
| GitGuard | — | Проверки перед деплоем: только master, не detached, чистое дерево, тег с CurrentVersion; `test`/`build` их сознательно не применяют | `Pipelines/GitGuard.cs` |
| LIVE / LIVE-PREV | теги LIVE | Локальные теги (никогда не пушатся): LIVE = коммит в проде, переносится после health-check; вытесненный остаётся LIVE-PREV; `--rollback` возвращает | CLAUDE.md, ADR-118 |
| StageBoard | доска этапов | Экран долгой операции в cedar: весь план шагов рисуется заранее и отмечается по ходу; шаги о доске не знают | `Pipelines/StageBoard.cs` |
| droplet / periwinkle | дроплет | Прод: DigitalOcean `cedarclerk-periwinkle` (fra1, 1 vCPU / 2 GB без swap, Ubuntu 24.04), заменил Pi 11.08.2026 | rules/production-environment.md |
| Cloudflare Tunnel | туннель | Единственный вход к проду: cloudflared → Kestrel на loopback:8080; наружу у дроплета один SSH | rules/production-environment.md |
| healthchecks ping | пинг сторожа | Сигнал в healthchecks.io в конце ночного backup.sh — молчаливый провал бэкапа поднимает алерт; у off-box-половины свой чек | rules/production-environment.md §Backups |
| off-box backup / R2 | внешний бэкап | Вторая половина backup.sh (T-147): rclone в Cloudflare R2 — намеренно не DO Spaces (не жить в одном аккаунте с дроплетом); молчит, пока нет ключей | rules/production-environment.md |
| smoke suite | smoke-сьют | Playwright через `Scripts/e2e.ps1`: скрипт владеет сервером, scratch-БД и браузером, бот выключен; `cedar test --smoke` | CLAUDE.md, ADR-070 |
| contrast check | контраст-чек | `check-contrast.mjs` в `cedar test`: цветовые пары в обеих темах по всем таблицам стилей под `src/` и по CSS, который сервер пишет из C#; правило судится в той палитре, чьи условия несёт его собственная цепочка селекторов, и акцент прогоняется по всем пресетам | ADR-137, ADR-152 |
| live-verify | живая верификация | Проверка руками поверх тестов; чек-лист «не проверено вживую» живёт в TASKS.md | ADR-070, DOCS-FLOW |
| pseudo-locale | псевдо-локаль | Раздутые строки вместо переводов для проверки вёрстки (`?pseudo=1`); в профиль не попадает | UI-INVENTORY |
| Cedar Bench | Cedar Bench | Единственный вид приложения: бумага, дерево, сосна, латунь. Значения — оба базовых блока `styles.scss`; продолжает направление набора «Cabin», который переписан на месте, а не поставлен рядом | ADR-136, ADR-137 |
| density mode | режим плотности | Две плотности (ADR-071): comfortable по умолчанию, `[data-density="compact"]` на табличных экранах; различаются только отступы/размеры — ни один цвет | DESIGN |
| surface class | класс поверхности | `data-surface` со значением `chrome` или `paper` на элементе — вторая ось плотности, а не палитры: у хрома минимальный бокс `--hit-chrome` 30px, у бумаги `--hit-target` 44px. Наследуется через `--hit-surface`, никогда не выбирается прикалыванием поверхности | ADR-138, ADR-156 |
| ADR / индекс | ADR-лог | «Сначала ADR, потом код»: тексты — файл на ADR в `docs/adr/`, `DECISIONS.md` — индекс; отмена решения — новый ADR поверх, не правка | DECISIONS, DOCS-FLOW |
| борда | таск-борда | `docs/tasks/BACKLOG.md` — единственный список открытого; сделанные строки удаляются, история в git | DOCS-FLOW |
| T-xxx / Q-xx | ID борды | Стабильные идентификаторы: T — задачи, Q — вопросы к Марти; исходные номера (B*, N*, I*…) в скобках описаний | DOCS-FLOW |
| Input sweep | разбор инбокса | Сверка брифов инбокса с кодом (они бывают старше кода) → борда/ROADMAP + запись «Input sweep» в ROADMAP; по ней датируется перезапись файла | DOCS-FLOW |
| INPUT_PROMPT inbox | промпт-инбокс | `docs/INPUT_PROMPT.md` — единственный инбокс с 18.08.2026; сознательно не коммитится, Марти переписывает целиком | DOCS-FLOW |
