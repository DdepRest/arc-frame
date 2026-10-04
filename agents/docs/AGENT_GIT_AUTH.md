# AGENT_GIT_AUTH.md — авторизация git у агентов (gh credential helper)

Как агент пушит в `main`, почему `git push` иногда **висит** и что для этого настроено на машине.
Документ про **окружение**, а не про код: в самом репозитории ничего не меняется.

## Проблема, которую это закрывает

В агентской оболочке нет `/dev/tty`. Git Credential Manager (GCM) из системного gitconfig
не находит сохранённые креды и уходит в интерактивный запрос логина:

```text
run-command (credential-manager)
bash -c 'cat >/dev/tty && read -r line </dev/tty ...'
bash: /dev/tty: No such device or address
```

Следствие: `git push` **виснет** до внешнего таймаута (замер: `timeout 120` → exit 124),
а не падает с ошибкой. `GIT_TERMINAL_PROMPT=0` + `GIT_ASKPASS=echo` проблему не решают.

Диагноз одной командой:

```bash
git config --show-origin --get-all credential.helper
# file:C:/Program Files/Git/etc/gitconfig    manager    ← это GCM на уровне системы
```

## Что настроено и где

```bash
gh auth setup-git --hostname github.com
```

`gh` 2.95.0, аккаунт `DdepRest`, токен — в keyring (в файлах не лежит).

Изменён **только** `C:/Users/Asus/.gitconfig` — конфиг пользователя Windows, **вне репозитория**,
поэтому он действует на все GitHub-репозитории машины, а не только на `arc-frame`:

```ini
[credential "https://github.com"]
	helper =
	helper = !'C:\Program Files\GitHub CLI\gh.exe' auth git-credential
[credential "https://gist.github.com"]
	helper =
	helper = !'C:\Program Files\GitHub CLI\gh.exe' auth git-credential
```

Ключевая деталь — **пустая строка `helper =`**: она сбрасывает унаследованный список хелперов,
поэтому системный GCM (`credential.helper=manager`) для `github.com` больше не вызывается.
Токен в конфиге не хранится: `gh` берёт его из keyring при каждом обращении.

Системный `C:/Program Files/Git/etc/gitconfig` и `.git/config` репозитория **не менялись** —
GCM продолжает работать для остальных хостов (например, Azure DevOps).

## Как проверить

```bash
# 1. Хелперы для github.com: пустая строка + gh (а не manager)
git config --show-origin --get-all credential.https://github.com.helper

# 2. Реальный push без -c-обёрток: должен завершиться за секунды, без запроса логина
timeout 90 git push origin main

# 3. Кто отдал креды — видно в трассе (ожидаемо: gh.exe auth git-credential get/store)
GIT_TRACE=1 git push origin main 2>&1 | grep 'gh.exe auth git-credential'
```

Замер, на котором это зафиксировано: `push` — 1 с, `fetch` — 2 с; до настройки `push` висел 120 с.

## Как откатить

`C:/Users/Asus/.gitconfig` создан `gh auth setup-git` с нуля, поэтому точный откат — удалить файл.
Если в нём появятся другие настройки — снять только секции хелпера:

```bash
git config --global --remove-section 'credential.https://github.com'
git config --global --remove-section 'credential.https://gist.github.com'
```

После отката для `github.com` снова начнёт вызываться GCM, и `git push` в агентской оболочке
опять будет виснуть на `/dev/tty`.

## Если авторизация сломалась

- `gh auth status` — кто залогинен, какие scopes.
- `gh auth login` — перелогин; новый токен helper подхватит сам, конфиг править не нужно.
- Проверь порядок хелперов: если перед gh-хелпером появился **непустой** `helper = ...`, строка
  сброса потерялась — повтори `gh auth setup-git --hostname github.com`.

**Не проверено (нет замеров):** поведение при недействительном/протухшем токене — падает быстро
или ждёт терминальный prompt в оболочке без `/dev/tty`. Проверка не делалась, чтобы не ломать
рабочую авторизацию.

## Source files

- `C:/Users/Asus/.gitconfig` — внешний конфиг (вне репозитория), создан `gh auth setup-git`
- `gh` (`C:/Program Files/GitHub CLI/gh.exe`) — credential helper

## Last verified
2026-10-04 (v3.54.3) — auto-synced from csproj (sync-version.ps1, CONTROL#13).

2026-10-04 (v3.54.2) — auto-synced from csproj (sync-version.ps1, CONTROL#13).

2026-10-04 (v3.54.0) — новый документ: авторизация git у агентов (gh credential helper) — что настроено, где, проверка, откат.

