# Security policy

Use the latest published version. The pre-1.0 series has no guaranteed support period.

Report a reproducible vulnerability through the repository's **Security → Report a vulnerability** page when private reporting is enabled. If unavailable, create an issue asking for a private reporting channel without including exploit details, secrets or private logs. Do not post credentials or upload your whole application-data folder.

The app runs as the current user, not elevated. It uses Windows APIs for accessibility, input hooks, normal window focus and app launching; it does not inject into another process or log keyboard content. User-authored favourites can execute programs by design, so do not import an untrusted favourites file without reviewing it.

Update requests are explicit, HTTPS-only and limited to this repository and GitHub release asset hosts. Installer size/time, release tag, expected filename and SHA-256 are checked. Checksums served by the same repository are corruption checks, not independent publisher authentication. Releases are currently unsigned; do not bypass Windows security or trust a build solely because it has a checksum.

Release workflows use a temporary GitHub token with the minimum job-level write permission. External Actions are pinned by commit SHA. Source archives, workflow logs and build-info records should be reviewed when investigating provenance. No account token is bundled in the app or publisher package.
