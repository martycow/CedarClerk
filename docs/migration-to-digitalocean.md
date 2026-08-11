# Переезд Cedar Clerk с Raspberry Pi на DigitalOcean

> **ВЫПОЛНЕНО 11.08.2026.** Прод живёт на дроплете `cedarclerk-periwinkle` (fra1, Ubuntu 24.04.4,
> x86_64, 1 vCPU / 2 ГБ), Pi продакшеном больше не является. Здесь файл остаётся **как журнал
> переезда**: что именно делалось и почему. Источник истины о сегодняшнем состоянии прода —
> `.claude/rules/production-environment.md`, он переписан по факту.
>
> **Что сделано не по плану — бэкапы.** Ночной `sqlite3 .backup` с Pi не переехал: на дроплете нет ни
> crontab, ни `~/bin` (проверено). Вместо него включён платный **еженедельный бэкап дроплета** на
> стороне DigitalOcean. Это не замена: окно потери выросло с суток до недели, восстановление идёт
> целиком машиной, и копия лежит в том же аккаунте, что и оригинал. Разбор — в
> `.claude/rules/production-environment.md` §Backups, работа — в бэклоге `T-071` (поднят до High).
>
> **Осталось незакрытым**: юнит `cedarclerk.service` стоит `disabled` — после ребута дроплета сайт
> сам не поднимется. Чинится одной командой с паролем sudo: `sudo systemctl enable cedarclerk`.

Чек-лист, составленный **по фактическому состоянию прода 11.08.2026** (снято с самой машины, не по памяти), а не по общей инструкции «как перенести приложение». Всё, что здесь названо, существует; всё, чего нет, отмечено отдельно.

Сопутствующие документы: `.claude/rules/production-environment.md` (что нельзя ломать), `.claude/rules/secrets.md` (как обращаться с ключами), `.claude/rules/telegram-bot.md` (почему порядок переключения важнее всего остального), `docs/ARCHITECTURE.md` (пайплайн деплоя).

---

## Что стояло на Pi перед переездом (снято 11.08.2026)

| Что | Значение |
|---|---|
| Железо / ОС | Raspberry Pi 4 8GB, Raspbian 11 Bullseye. **Ядро aarch64, userland armhf (32-бит)** |
| Рантайм | ASP.NET Core 8.0.29 в `~/.dotnet`, SDK нет — Pi ничего не собирает |
| Приложение | `/home/martycow/cedarclerk/app`, юнит `cedarclerk.service`, запуск `dotnet CedarClerk.Server.dll` |
| Данные | `/home/martycow/cedarclerk/data` — **628 МБ**, из них `media/` 469 МБ. Плюс `cedar.db` (+ `-wal`, `-shm`), `dataprotection-keys`, `thumbs`, `import-tmp`, `manual-backups` |
| Секреты | drop-in `/etc/systemd/system/cedarclerk.service.d/data.conf` (`CEDAR_DATA_DIR`, `Cedar__InviteCode`, `Cedar__AdminEmail`, ключи Resend / X / Telegram / платёжек) |
| Сеть | Cloudflare Tunnel `cedarpi` (`cloudflared.service`, работает с 24.07) → `http://localhost:8080`. TLS терминируется в Cloudflare |
| Хосты | `cedarclerk.mooexe.dev` и `blog.mooexe.dev` — **один процесс Kestrel**, блог разведён по `Host` внутри `Program.cs` |
| Бэкапы | cron `30 3 * * *` → `~/bin/cedar-backup.sh`: `sqlite3 .backup` + `rsync` на microSD `/mnt/backup`, 14 копий |
| Диск | 27 ГБ, свободно 5.9 ГБ (77% занято) |

---

## Почему это вообще несложно — и где единственная настоящая опасность

**Приложение переносимое.** `Scripts/deploy.ps1` делает `dotnet publish -c Release` **без `-r`**, то есть кладёт portable-IL. Разница armhf → x86_64 не значит ничего: те же `.dll` запустятся на droplet'е под тем же рантаймом 8.0. Пересобирать под новую архитектуру не нужно.

**Опасность ровно одна, и она про Telegram.** Bot API разрешает **одному** процессу делать `getUpdates` на токен. Если droplet стартует с настроенным `Cedar__Telegram__BotToken`, пока `cedarclerk` на Pi ещё жив, оба процесса получат 409 Conflict и бот сломается у обоих. Поэтому весь порядок ниже построен вокруг одного правила: **токен живёт ровно в одном месте в каждый момент времени.**

---

## Фаза 0 — решения, которые надо принять до первой команды

- [ ] **Размер droplet'а.** Данных 628 МБ, приложение ~200 МБ, рантайм ~200 МБ. По CPU/RAM Cedar Clerk сегодня живёт на Pi 4 в компании других задач, так что 1 vCPU / 1 ГБ ($6/мес) хватит; 2 ГБ ($12/мес) берут не ради нагрузки, а чтобы сборка мусора и `sqlite3 .backup` не толкались на 1 ГБ. **Диск**: минимальные 25 ГБ покрывают сегодняшние 628 МБ с большим запасом — но именно media растут, так что смотреть на них, а не на общий размер.
- [ ] **Регион.** Ближе к читателям, а не к тебе: TLS терминируется в Cloudflare, но исходник всё равно опрашивается. Для аудитории RU/EU — `fra1` или `ams3`.
- [ ] **Обновлять ли .NET заодно.** .NET 8 доживает до ноября 2026. Соблазн переехать на .NET 10 LTS в тот же заход велик — **не надо**: тогда падение будет невозможно объяснить одной причиной. Переезд отдельно, апгрейд рантайма отдельно, между ними неделя работающего прода.
- [ ] **Чем заменить бэкап на microSD.** На droplet'е нет `/mnt/backup`. Без замены `cedar-backup.sh` тихо выйдет по `mountpoint -q` и **не сделает ничего** — скрипт написан молчаливым. Варианты: DO Spaces через `rclone`/`s3cmd` (закрывает давно записанный тех-долг про облачные бэкапы) или DO Volume + снапшоты. Решить **до** переезда, а не после.

---

## Фаза 1 — droplet, до переноса данных

- [ ] Создать droplet: Ubuntu 24.04 LTS, x86_64, SSH-ключ (тот же ed25519, что ходит на Pi).
- [ ] **Завести пользователя `martycow`** и те же пути:
      ```
      adduser --disabled-password --gecos "" martycow
      mkdir -p /home/martycow/cedarclerk/{app,data} /home/martycow/bin
      chown -R martycow:martycow /home/martycow
      ```
      Это не косметика: `Scripts/deploy.ps1` держит `AppDir = /home/martycow/cedarclerk/app` литералом, а `-PiHost` уже параметр. Совпали пути и имя — **скрипт деплоя работает без единой правки**, достаточно `-PiHost martycow@<ip>`.
- [ ] Скопировать `~/.ssh/authorized_keys` для `martycow`.
- [ ] `sudo` без пароля **только** на управление сервисом, как на Pi:
      ```
      echo 'martycow ALL=(ALL) NOPASSWD: /bin/systemctl start cedarclerk, /bin/systemctl stop cedarclerk, /bin/systemctl restart cedarclerk' | sudo tee /etc/sudoers.d/cedarclerk
      ```
- [ ] Рантайм: `aspnetcore-runtime-8.0` в `/home/martycow/.dotnet` (тот же путь — юнит ссылается на него через `DOTNET_ROOT`). SDK не ставить: пусть машина по-прежнему ничего не собирает.
- [ ] `apt install sqlite3 rsync` — нужны бэкапу.
- [ ] **Файрвол.** На Pi приложение защищал домашний NAT; у droplet'а публичный IP. Kestrel слушает `localhost:8080` (`Consts.URLs.Localhost`), то есть снаружи и так недоступен, а туннель ходит наружу — но защита в два слоя дешевле одного:
      ```
      ufw default deny incoming; ufw allow OpenSSH; ufw enable
      ```
      **Порт 8080 наружу не открывать никогда.** Если однажды понадобится `Cedar:Urls` с `0.0.0.0` — это ошибка, а не решение.
- [ ] Скопировать юнит `cedarclerk.service` (он не содержит ничего машинно-специфичного) и `enable` **без** `start`.

---

## Фаза 2 — секреты

- [ ] Скопировать `data.conf` **файлом**, не перенабирая значения:
      ```
      ssh martycow@raspberrypi.local "sudo cat /etc/systemd/system/cedarclerk.service.d/data.conf" \
        | ssh martycow@<ip> "sudo mkdir -p /etc/systemd/system/cedarclerk.service.d && sudo tee /etc/systemd/system/cedarclerk.service.d/data.conf >/dev/null"
      ssh martycow@<ip> "sudo chmod 600 /etc/systemd/system/cedarclerk.service.d/data.conf && sudo systemctl daemon-reload"
      ```
- [ ] **Временно закомментировать в копии `Cedar__Telegram__BotToken`.** Первый запуск на droplet'е должен пройти без токена — иначе 409 у обоих. Локально без токена бот выключается by design и эндпоинты отвечают 503 с внятным текстом, так что проверять всё остальное это не мешает.
- [ ] Ничего не ротировать. Ротация нужна, когда секрет утёк, а не когда переехал; лишняя ротация — это ещё и поход в BotFather, Stripe и X за новыми ключами в день, когда и без того много движения.

---

## Фаза 3 — данные

- [ ] **Остановить сервис на Pi.** Копировать живую SQLite нельзя: рядом лежат `cedar.db-wal` и `cedar.db-shm`, и `scp` заберёт несогласованный набор.
      ```
      ssh martycow@raspberrypi.local "sudo systemctl stop cedarclerk"
      ```
- [ ] Снять согласованную копию базы и забрать всё остальное:
      ```
      ssh martycow@raspberrypi.local "sqlite3 ~/cedarclerk/data/cedar.db \".backup '/tmp/cedar-move.db'\""
      rsync -avz martycow@raspberrypi.local:~/cedarclerk/data/media/ ./move/media/
      rsync -avz martycow@raspberrypi.local:~/cedarclerk/data/dataprotection-keys/ ./move/dataprotection-keys/
      rsync -avz martycow@raspberrypi.local:~/cedarclerk/data/thumbs/ ./move/thumbs/
      scp martycow@raspberrypi.local:/tmp/cedar-move.db ./move/cedar.db
      ```
- [ ] **`dataprotection-keys` — не пропустить.** Это ключи, которыми подписаны auth-cookie. Не перенесёшь — все сессии станут недействительны и все (то есть ты и все тестовые аккаунты) окажутся разлогинены в момент переключения. Имя приложения для этих ключей закреплено константой `Consts.DataProtectionApplicationName` ровно затем, чтобы они пережили переезд.
- [ ] `import-tmp` и `manual-backups` не нужны — первое рабочий мусор, второе уже лежит копией. `cedar-before-squash.db` перенести один раз в архив и на droplet не класть.
- [ ] Залить на droplet в `/home/martycow/cedarclerk/data/`, выставить `chown -R martycow:martycow`.
- [ ] Проверить целостность **до** первого старта: `sqlite3 cedar.db "PRAGMA integrity_check;"` → `ok`, и `SELECT COUNT(*) FROM Drafts;` совпадает с Pi.

---

## Фаза 4 — первый запуск, ещё без публики

- [ ] Задеплоить текущий `master`: `.\Scripts\deploy.ps1 -PiHost martycow@<ip>`.
      **Скрипт упадёт на health-check'е** — он проверяет `https://cedarclerk.mooexe.dev/api/health`, а тот адрес пока отвечает с Pi. Это ожидаемо и не значит, что деплой не прошёл; шаги 1–6 к тому моменту уже отработали.
- [ ] Запустить и проверить локально на droplet'е:
      ```
      ssh martycow@<ip> "sudo systemctl start cedarclerk && sleep 5 && curl -s localhost:8080/api/health"
      ```
      Ждём `"version": "<текущая>"`, `"env": "Production"`.
- [ ] В логах: **ни одной** строки `Applying migration` (база уже мигрирована на Pi) и ни одного `CREATE TABLE`. Если они есть — значит уехала не та база.
- [ ] Проверить, что бот **не** запустился: в логах строка про отключённый бот, а не попытки `getUpdates`.
- [ ] Через SSH-туннель (`ssh -L 8080:localhost:8080 martycow@<ip>`) открыть приложение в браузере и убедиться, что **ты залогинен** — это подтверждает, что `dataprotection-keys` доехали.

---

## Фаза 5 — переключение трафика

Cloudflare Tunnel — то, что публикует оба хоста. Переключение делается на стороне туннеля, DNS при этом не трогается: записи `cedarclerk` и `blog` — CNAME на туннель.

- [ ] Поставить `cloudflared` на droplet, авторизовать, создать **второй** туннель (например `cedardo`) и завести конфиг, маршрутизирующий **оба** хоста на `http://localhost:8080`:
      ```yaml
      ingress:
        - hostname: cedarclerk.mooexe.dev
          service: http://localhost:8080
        - hostname: blog.mooexe.dev
          service: http://localhost:8080
        - service: http_status:404
      ```
      **Блог — не отдельный сервис.** Он разведён по `Host` внутри того же Kestrel (`Program.cs`, `MapWhen`), и забыть второй hostname — самая вероятная ошибка этого шага: сайт заработает, а блог отдаст 404 туннеля.
- [ ] Переключить CNAME с `cedarpi` на `cedardo` (`cloudflared tunnel route dns cedardo cedarclerk.mooexe.dev`, то же для `blog`).
- [ ] **Только теперь** — токен бота: раскомментировать в drop-in на droplet'е, `systemctl restart cedarclerk`. Сервис на Pi к этому моменту уже остановлен с Фазы 3 и **не должен подниматься** (`sudo systemctl disable cedarclerk` на Pi, чтобы `Restart=always` и перезагрузка не вернули его к жизни).
- [ ] Проверить снаружи: `curl -s https://cedarclerk.mooexe.dev/api/health`, открыть блог, отправить тестовый пост в `@testingandfun`. **В Dev Dairy Diary не публиковать.**

---

## Фаза 6 — то, что переезжает не само

- [ ] **Бэкап.** Положить `~/bin/cedar-backup.sh`, поправить `DEST` и заменить проверку `mountpoint -q /mnt/backup` на то, что выбрано в Фазе 0. Поставить cron `30 3 * * *`. **Проверить запуском вручную и посмотреть, что файл появился** — скрипт молча выходит, если цель недоступна, и «cron стоит» без этого ничего не значит.
- [ ] **Индексация ассетов остаётся выключенной.** `Cedar:AssetIndex:Enabled` на сервере не включать никогда: эндпоинт заставляет сервер ходить по собственному диску — на ноутбуке это фича, на хостинге это раскрытие имён файлов. Десктоп ставит флаг сам.
- [ ] **`Cedar:Auth:Upstream` на сервере не появляется.** Это ключ десктопа, указывающий на сервер; на самом сервере он означал бы делегирование личности самому себе. После переезда — поменять его значение в десктопной сборке, если адрес сервера изменится (он не меняется: домен тот же).
- [ ] `unattended-upgrades` — на droplet'е с публичным IP это не роскошь.
- [ ] Мониторинг: хотя бы DO-алерт на диск >80% и на недоступность. На Pi эту роль выполняло то, что машина стоит дома; на droplet'е её не выполняет никто.
- [ ] Обновить `.claude/rules/production-environment.md` и `docs/ARCHITECTURE.md` — иначе следующая сессия будет чинить прод по описанию Raspberry Pi.
- [ ] Обновить `Scripts/deploy.ps1`: сменить дефолт `-PiHost` и переименовать параметр (`-Target`), а `$PiHost` в сообщениях об ошибках перестанет говорить «Pi».

---

## Откат

Он дешёвый ровно до тех пор, пока Pi цел, поэтому:

- [ ] **Pi не трогать неделю.** Не форматировать, не переустанавливать, не забирать microSD. Данные на нём остаются консистентным снимком на момент Фазы 3.
- [ ] Откат = вернуть CNAME на туннель `cedarpi`, `systemctl enable --now cedarclerk` на Pi, убрать токен из drop-in'а на droplet'е.
- [ ] **Цена отката растёт с каждым днём**: всё, что написано и опубликовано после переключения, живёт только в базе droplet'а. Через неделю откат уже означает потерю недели — поэтому окно и ограничено неделей, а не «пока не понадобится».

---

## Что скорее всего укусит

1. **Два процесса на одном токене** — 409, бот молчит у обоих. Единственная по-настоящему опасная ошибка этого переезда; вся Фаза 5 построена вокруг неё.
2. **Забытый `blog.mooexe.dev`** в ingress туннеля: сайт работает, блог отдаёт 404, и выглядит это как поломка приложения, хотя приложение ни при чём.
3. **Забытые `dataprotection-keys`** — «почему меня разлогинило» в момент, когда меньше всего хочется отлаживать вход.
4. **Копирование живой SQLite** мимо `.backup`: WAL-файлы рядом, и копия может оказаться битой не сразу, а через день.
5. **Молчаливый бэкап**: `cedar-backup.sh` выходит с кодом 1 и без единого слова, если `/mnt/backup` не смонтирован. На droplet'е он не смонтирован никогда.
6. **`Restart=always` на Pi**: остановленный, но не `disable`-нутый сервис вернётся после перезагрузки — и снова заберёт токен.

---

## Чего этот чек-лист сознательно не делает

- **Не переводит на PostgreSQL.** SQLite при одном пишущем процессе и таких объёмах — не узкое место, а переезд и смена СУБД одновременно означают, что первая же проблема будет иметь две возможные причины.
- **Не обновляет .NET** (см. Фазу 0).
- **Не переносит Freenove-проекты** и всё остальное, что живёт на том же Pi, — это отдельная жизнь, и после переезда Cedar Clerk она станет только свободнее.
- **Не настраивает CI/CD.** `deploy.ps1` с твоей машины продолжает работать ровно как работал; менять способ выкатки в день смены хоста — это снова две причины на одну проблему.
