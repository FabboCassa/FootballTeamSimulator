# Privacy Policy — Football Team Simulator

**Draft. Have a lawyer read this before publishing it.** It is written from what the code
actually does (see `data-safety.md`), which is the hard part; the legal wording is the part
a professional should check, particularly the GDPR sections, because the developer is
established in the EU.

_Last updated: [DATE] · Version [X.Y.Z]_

---

## Who is responsible

Football Team Simulator ("the Game") is developed and operated by Fabio Casarini
("we", "us"), an individual developer established in Italy. For the purposes of the EU
General Data Protection Regulation (GDPR), we are the data controller.

Contact: [CONTACT EMAIL]

## The short version

If you play the single-player career, we collect nothing at all. The Game runs on your
device and your save file never leaves it.

If you create an account to play online — private leagues with friends, or the ranked
ladder — we store the minimum needed to make that work: your email address, a hashed
password, the display name you choose, your game data, and a hashed form of your IP
address used to stop one person running several accounts against each other.

We do not show advertising. We do not use analytics or tracking services. We do not sell
or rent your data to anyone.

## What we collect and why

We only process data when you choose to create an account.

| What | Why | Legal basis (GDPR) |
|---|---|---|
| Email address | Identifying your account, signing you in, and sending you account-related messages such as a password reset | Performance of a contract (Art. 6(1)(b)) |
| Password | Signing you in. Stored only as a cryptographic hash — we never hold your password and cannot recover it | Performance of a contract |
| Display name | Shown to other players in leagues, standings and the leaderboard | Performance of a contract |
| Game data (clubs, squads, transfers, results, ratings, awards) | Running the game you are playing | Performance of a contract |
| A salted, hashed form of your IP address, and an optional device identifier | Detecting multiple accounts operated by the same person, which would otherwise let someone rig a league against honest players | Legitimate interests (Art. 6(1)(f)): keeping competition fair |
| Push notification token, if you allow notifications | Telling you that you were outbid or that your match is about to start | Consent (Art. 6(1)(a)) — you grant it in your device settings and can withdraw it there |
| Reports you file, and automatic integrity flags | Reviewing suspected collusion or abuse | Legitimate interests |

**We never store your IP address in readable form.** It is hashed with a secret salt
before it is written, and we can rotate that salt, which permanently severs any link
between old records and any address.

## What we do not collect

No advertising identifier. No location data. No contacts, photos, microphone or camera
access. No payment information — the Game contains no purchases. No third-party
advertising, attribution or analytics software of any kind.

## Who else sees your data

One service, and only if push notifications are enabled:

- **Google Firebase Cloud Messaging**, which receives your device's notification token and
  the text of the notification in order to deliver it. Google's own privacy terms apply to
  that processing.

Beyond that, your data is not shared with anyone. We may disclose information if we are
legally required to, and we would tell you unless the law forbade it.

Our servers are hosted in [HOSTING LOCATION — fill in before publishing]. If that is
outside the EU, transfers rely on the appropriate safeguards under Chapter V of the GDPR.

## How long we keep it

- Account data: for as long as your account exists.
- Refresh tokens: 30 days, or until you sign out.
- Integrity records (hashed signals, flags, reports): up to 12 months, then deleted. These
  deliberately outlive the league they refer to — a record of a rigged transfer is useless
  if it disappears with the league.
- Game data: for as long as your account exists.

When you delete your account, all of the above is deleted, except records we are legally
required to retain.

## Deleting your account

You can delete your account at any time, from inside the Game (Account → Delete account) or
from the public page at `<site>/delete-account.html`. Deletion is immediate and cannot be
undone. It removes your email, password, coach profile, sessions, registered devices,
private-league memberships and the hashed fingerprints used against multiple accounts; a
private league left with no members is closed with it.

Two things survive, no longer connected to you: the results of ranked seasons already
played, kept under an identifier that belongs to nobody so that other coaches' tables and
honours remain correct, and any club you were managing in a season under way, which carries
on as a computer-managed team.

## Your rights

Under the GDPR you may ask us to give you a copy of your data, correct it, delete it,
restrict or object to how we use it, or provide it in a portable form. You can also
complain to your national supervisory authority — in Italy, the Garante per la protezione
dei dati personali.

To exercise any of these, write to [CONTACT EMAIL]. You can delete your account from
within the Game at any time, which removes your data as described above.

## Children

The Game is not directed at children under 13 (or under 16 where local law sets that
threshold), and we do not knowingly collect data from them. If you believe a child has
created an account, contact us and we will delete it.

## Local storage on your device

The Game stores your single-player save and your settings on your own device — in the
browser this uses IndexedDB, on desktop and mobile the application's own storage folder.
This never reaches us. Clearing your browser data or uninstalling the app deletes it.

## Changes

If we change this policy we will update the date at the top and, for anything
significant, tell you in the Game the next time you sign in.
