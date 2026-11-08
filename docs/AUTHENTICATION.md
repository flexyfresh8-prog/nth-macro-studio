# GitHub authentication & pushing this repository

This guide gets the code onto your GitHub account **without ever exposing a credential**.

## Golden rules

1. **Never paste tokens or passwords into chats, issues, commit messages or CI logs.**
2. **If a token was ever exposed anywhere, revoke it immediately** at <https://github.com/settings/tokens> — treat it as compromised, not "maybe fine".
3. Prefer interactive credential helpers over raw PATs; let the OS credential store hold secrets.

## Option A — GitHub CLI (recommended)

```bash
winget install GitHub.cli        # or download from https://cli.github.com
gh auth login                    # choose: GitHub.com → HTTPS → "Login with a web browser"
```

The CLI prints a one-time code and opens the browser — no token ever touches your clipboard or shell history. Then:

```bash
cd nth-macro-studio
git remote add origin https://github.com/<you>/nth-macro-studio.git
git push -u origin main
```

`gh auth setup-git` (run once) wires Git to use the CLI's credential helper automatically.

## Option B — Git Credential Manager (comes with Git for Windows)

Git for Windows includes GCM. On the first push a browser window opens and the credential is stored in **Windows Credential Manager**:

```bash
git remote add origin https://github.com/<you>/nth-macro-studio.git
git push -u origin main          # browser opens once; afterwards it is silent
```

## Option C — SSH keys

```bash
ssh-keygen -t ed25519 -C "your_email@example.com"
# add %USERPROFILE%\.ssh\id_ed25519.pub under github.com → Settings → SSH and GPG keys
git remote add origin git@github.com:<you>/nth-macro-studio.git
git push -u origin main
```

## If you must use a Personal Access Token

Use a **fine-grained PAT** with the minimum possible scope:

- Repository access: **only** `nth-macro-studio`
- Permissions: **Contents: Read and write** — nothing else
- Short expiration (30–90 days)

Do not put it in a command line or a script. Configure it once through Git Credential Manager (it will be stored encrypted) or use `gh auth login --with-token < file.txt` reading from a protected file — never an inline argument, which lands in shell history.

## This repository's secrets policy

- `.gitignore` excludes `*.env`, `*.pfx`, `*.key` — keep it that way.
- No API keys, tokens or passwords exist anywhere in the source tree.
- CI does not need any secrets to build; a release job only needs the implicit `GITHUB_TOKEN`.
- Code-signing certificates, if you add them later, go into GitHub Actions **secrets**, never into files (see `docs/RELEASING.md`).
