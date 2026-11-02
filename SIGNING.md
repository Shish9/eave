# Getting Eave signed with SignPath (free for open source)

Signed builds run on PCs with Smart App Control and don't show "unknown publisher" warnings.
The code side is ready; these are the steps only you can do (accounts and identity).

Check each provider's current rules as you go; programs and eligibility change.

## 1. Put the code on GitHub (public)

1. Create a GitHub account if you don't have one, and turn on two-factor authentication.
2. Create a new public repository, for example `eave`.
3. Push this folder to it (I can do this for you once the repository exists, if you tell me to).

## 2. Apply to the SignPath Foundation

1. Go to <https://signpath.org> and read the conditions for open-source projects. At the time of writing
   they ask for an OSI-approved licence (Eave uses MIT), a public repository, builds made by CI
   (Eave uses GitHub Actions), and a code signing policy in the README (already added).
2. Check that they accept applicants from your country before spending time on it.
3. Apply with the repository link.

## 3. Once approved: connect GitHub to SignPath

In SignPath:
1. Install the SignPath GitHub app on your repository when prompted.
2. Create a project with the slug `eave`.
3. Create a signing policy with the slug `release-signing`.
4. Create an artifact configuration and paste the contents of `.signpath/artifact-configuration.xml`.
5. Create a CI user, and copy its API token.

In GitHub, repository → Settings → Secrets and variables → Actions:
- Secret `SIGNPATH_API_TOKEN`: the CI user's token
- Variable `SIGNPATH_ORGANIZATION_ID`: your SignPath organization ID
- (only if you used different names) variables `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_POLICY_SLUG`

## 4. Make a release

1. Raise the version in `ClaudeIsland/ClaudeIsland.csproj`, `ClaudeRelay/ClaudeRelay.csproj` and
   `ClaudeIsland/Brand.cs` (the workflow refuses to release if they disagree or don't match the tag), and add
   the changes to `CHANGELOG.md`.
2. Push a version tag, for example `v0.3.3`.
3. GitHub Actions builds Eave, sends `Eave.exe` and `relay.exe` to SignPath (approve the request in SignPath if
   your policy asks for approval), builds `Eave-Setup-0.3.3.exe` around the signed files, and publishes a
   GitHub release with the installer and `Eave-0.3.3-portable.zip`.

Until the SignPath secret exists the workflow still publishes the release, with "(unsigned)" in its title and
a note about the Windows warning, so there is nothing to attach by hand.

To try a build without releasing it, open Actions → release → Run workflow. The installer is attached to the run
as the `eave-package` artifact.

## 5. Optional: sign the installer too

The steps above sign the two program files. The installer wrapper (`Eave-Setup-….exe`) is not signed yet, so
Windows can still warn when it is run. To sign it as well, add a second artifact configuration in SignPath for
the installer (a single `pe-file` for `Eave-Setup-*.exe`) and a second `submit-signing-request` step after the
installer is built.

## 6. Install the signed build

Run the installer. It replaces an older copy, keeps your settings, and points the Claude Code hooks at the new
files. (A copy made with `Eave.exe --install` from the portable zip is replaced too, and Settings → Apps lists
Eave once.)
