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

1. Push a version tag, for example `v0.3.0`.
2. GitHub Actions builds Eave and sends it to SignPath. Approve the signing request in SignPath
   if your policy asks for approval.
3. The signed `Eave-v0.3.0-win-x64.zip` is attached to a new GitHub release.

Until the secret exists, the workflow still runs and produces an `-UNSIGNED` zip you can download
from the run, but it does not publish a release.

## 5. Install the signed build

Unzip, run `Eave.exe --install`. It replaces the old "Claude Island" install, keeps your settings,
and repoints the Claude Code hooks.
