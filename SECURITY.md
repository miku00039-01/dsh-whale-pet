# Security Policy

## Reporting a Vulnerability

Please do **not** open a public issue for security vulnerabilities.

- Prefer a **private report** via GitHub's [Security Advisories](https://github.com/miku00039-01/dsh-whale-pet/security/advisories/new) (visible only to maintainers).
- Alternatively, open a regular issue without sensitive details and request a private channel.

We'll acknowledge reports as soon as possible.

## Notes

- This project manages the **DeepSeek Harness** service on localhost; it never stores or transmits your API credentials — those live in DSH's own config (`~/.dsh/.credentials.yaml`), which this project's `.gitignore` also excludes defensively.
- The executable is built entirely from source by CI (`build.ps1`); always prefer building from the source in this repository or downloading from official Releases.
