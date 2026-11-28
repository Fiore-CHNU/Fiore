# Contributing to Fiore (backend)

These are the team's working agreements for the repository. The same rules apply to the frontend repository `Fiore-CHNU/Fiore-App`.

## Branching: GitHub Flow

- `main` is always releasable. Nobody pushes to it directly.
- Every change starts from the latest `main` on a short-lived branch and returns through a pull request.
- Branch names contain the Jira key so that Jira links the branch, commits, and PR to the issue:

| Type | Pattern | Example |
|---|---|---|
| Feature | `feature/EAN-<n>-<short-description>` | `feature/EAN-383-slot-buffer` |
| Bug fix | `fix/EAN-<n>-<short-description>` | `fix/EAN-612-slot-timezone` |
| Maintenance, CI, docs | `chore/EAN-<n>-<short-description>` | `chore/EAN-311-git-process` |

Delete the branch after the PR is merged.

## Commit messages: Conventional Commits

```
<type>(<scope>): <summary in the imperative>

<optional body: why, not how>

Refs: EAN-<n>
```

Types: `feat`, `fix`, `test`, `refactor`, `docs`, `ci`, `chore`, `perf`. Scope is the feature slice, for example `checkout`, `catalog`, `admin`, `auth`.

Examples:

- `feat(checkout): reject slots that start within 90 minutes`
- `fix(admin): accept camelCase status filter`
- `test(delivery): cover free delivery threshold`

## Pull requests

- Title: Conventional Commit style with the Jira key, for example `feat(checkout): price-change conflict (EAN-393)`.
- Fill in the template (`.github/pull_request_template.md`): Jira key, what changed, how it was tested.
- Keep PRs small: one story or task per PR.
- `main` is protected: at least **1 approval** and a **green CI** (`build-and-test`) are required before merging; force pushes and branch deletion are blocked.
- The reviewer checks the Definition of Done items in the template and leaves comments in the PR.

## Continuous integration

`.github/workflows/ci.yml` runs on every pull request and every push to `main`:

1. restore and build the solution (Release);
2. run all unit and API tests (`dotnet test`);
3. generate the idempotent EF Core migration script as a build artifact.

A red CI blocks the merge. Fix the failing step on the same branch; do not disable tests.

Dependabot (`.github/dependabot.yml`) opens weekly PRs for NuGet packages (minor and patch versions) and for GitHub Actions.

## Local checks before opening a PR

```bash
dotnet build Fiore.sln
dotnet test Fiore.sln
```
