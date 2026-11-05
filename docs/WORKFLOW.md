# 🛠️ Software Engineering & Git Flow Workflow

This document outlines the professional development lifecycle for **NUP Extractor**, covering the Kanban board setup, branch naming conventions, pull requests, automated CI verification, and production release flows.

---

## 📌 1. Git Branching Strategy

We follow a robust **Git Flow / Trunk-Based Integration** model:

```
[ feature/xyz ]  ──(PR + CI)──►  [ develop ]  ──(Release PR)──►  [ main ]
                                (Integration)                     (v1.0.x Release)
```

| Branch | Purpose | Protection |
|---|---|---|
| **`main`** | **Production Releases**. Every merge into `main` automatically cuts a new version tag (e.g. `v1.0.6`) and publishes a GitHub Release. | Protected (merge only from `develop`) |
| **`develop`** | **Active Integration Branch**. All feature branches and bug fixes are merged here after passing CI checks. | Protected (requires CI checks) |
| **`feature/*`** | Feature branches for new capabilities (e.g. `feature/dark-mode`, `feature/drag-drop-folder`). | Merges into `develop` |
| **`fix/*`** | Bug fix branches (e.g. `fix/large-file-streaming`, `fix/zstd-alloc`). | Merges into `develop` |

---

## 📋 2. GitHub Projects (Kanban Board) Setup

To organize work items professionally:

1. Go to your repository on GitHub: [`https://github.com/SPIN0ZAi/nup-extractor`](https://github.com/SPIN0ZAi/nup-extractor).
2. Click **Projects** in the top navigation tab.
3. Click **New Project** &rarr; select **Board (Kanban)** template.
4. Name the board: `NUP Extractor Engineering Board`.
5. Configure the columns:
   - 📥 **Backlog**: Ideas, planned enhancements, and reported bugs.
   - 🎯 **Todo**: Tasks prioritized for the current milestone.
   - ⏳ **In Progress**: Actively being coded on a dedicated branch.
   - 🔍 **In Review**: Pull Request opened, awaiting CI checks & review.
   - ✅ **Done**: Merged into `develop` or deployed to `main`.

---

## 🔄 3. Step-by-Step Task Development Lifecycle

### Step 1: Create or Pick a Task
1. In the GitHub Kanban board, create a new Issue using the templates:
   - **Bug Report** (`.github/ISSUE_TEMPLATE/bug_report.yml`)
   - **Feature Request** (`.github/ISSUE_TEMPLATE/feature_request.yml`)
   - **Engineering Task** (`.github/ISSUE_TEMPLATE/task.yml`)
2. Move the issue to **Todo**, assign yourself, and note the issue number (e.g. `#12`).

### Step 2: Create a Feature/Bugfix Branch
Always branch off the latest `develop`:

```bash
# 1. Switch to develop and pull latest changes
git checkout develop
git pull origin develop

# 2. Create and switch to your feature branch
git checkout -b feature/issue-12-task-name
# or for a bug:
git checkout -b fix/issue-12-pokopia-extract
```

### Step 3: Implement & Write Unit Tests (TDD)
1. Write the code in `src_native/` or `static/`.
2. Add corresponding automated unit tests in `tests_native/NupTests.cs`.
3. Verify all tests pass locally:
   ```bash
   dotnet test NupExtractor.slnx
   ```

### Step 4: Commit Cleanly
Follow Conventional Commits format:
```bash
git add .
git commit -m "feat(ui): add batch extraction selector (closes #12)"
# or for fixes:
git commit -m "fix(core): resolve 64-bit extent buffer overflow (closes #12)"
```

### Step 5: Push Branch & Open Pull Request
Push your branch to GitHub (only when you are ready to open the PR):
```bash
git push -u origin feature/issue-12-task-name
```

1. Go to GitHub and click **Compare & pull request**.
2. **Base branch**: Select **`develop`** (NOT `main`).
3. Fill in the Pull Request template:
   - Reference the issue: `Closes #12`.
   - Describe what was changed.

### Step 6: Automated CI Verification Check
Once the PR is opened, the **CI Verification (`ci.yml`)** workflow runs automatically on GitHub Actions:
- 🔨 **Restore & Compile**: Compiles .NET 10 solution in Release mode.
- 🧪 **Test Suite**: Executes all 19+ xUnit unit tests.
- 📦 **Packaging Dry-Run**: Verifies single-file standalone executable packaging.

If CI passes: GitHub displays a **green checkmark (`✓ All checks have passed`)**.

### Step 7: Review & Merge to `develop`
1. Review the diff and verify green CI checks.
2. Click **Merge pull request** &rarr; **Confirm merge**.
3. Move the Kanban card to **Done**.
4. Delete the feature branch on GitHub.

---

## 🚀 4. Cutting a Production Release

When `develop` has accumulated tested features and you are ready to publish a new version:

1. Open a Pull Request from **`develop` &rarr; `main`** titled:
   `release: version v1.x.x`
2. Review the changelog and merge into `main`.
3. The **Build & Release (`build-and-release.yml`)** workflow automatically:
   - Auto-increments the semantic version tag (`v1.0.6` &rarr; `v1.0.7`, etc.).
   - Packages `NUP_Extractor-windows-x64.zip` and `SHA256SUMS.txt`.
   - Publishes the new release on GitHub Releases.
   - Activates the in-app auto-updater for all existing users!
